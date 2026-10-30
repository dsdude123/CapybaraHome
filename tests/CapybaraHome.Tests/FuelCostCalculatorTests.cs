using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the maths that turns the week's round trips into a fuel bill.</summary>
public class FuelCostCalculatorTests
{
    [Fact]
    public void A_three_day_week_counts_six_one_way_drives()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 12d)]),
            [Destination(1, "Work", trips: 3)],
            []);

        Assert.Equal(72d, cost.MilesPerWeek);
        Assert.Equal(72d, cost.Lines.Single().MilesPerWeek);
        Assert.True(cost.AnyDriving);
    }

    [Fact]
    public void Each_car_costs_the_same_miles_at_its_own_mileage_and_price()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 10d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Thirsty", mpg: 20d, price: 4m), Vehicle(2, "Thrifty", mpg: 50d, price: 4m)]);

        var thirsty = cost.Vehicles.Single(v => v.VehicleId == 1);
        var thrifty = cost.Vehicles.Single(v => v.VehicleId == 2);

        Assert.Equal(3d, thirsty.GallonsPerWeek);
        Assert.Equal(12m, thirsty.WeeklyCost);
        Assert.Equal(20.80m, thrifty.MonthlyCost, 2);
        Assert.Equal(thrifty.MonthlyCost, cost.LowestMonthlyCost);
    }

    [Fact]
    public void A_week_becomes_a_month_at_fifty_two_weeks_a_year()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 10d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)]);

        var vehicle = cost.Vehicles.Single();

        Assert.Equal(9m, vehicle.WeeklyCost, 2);
        Assert.Equal(39m, vehicle.MonthlyCost, 2);
    }

    [Fact]
    public void A_destination_driven_to_no_days_a_week_is_left_out()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 12d), (2, 5d)]),
            [Destination(1, "Work", trips: 3), Destination(2, "Gym", trips: 0)],
            []);

        Assert.Equal("Work", Assert.Single(cost.Lines).Name);
    }

    [Fact]
    public void A_drive_with_no_distance_adds_nothing_and_is_named()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 12d)], miles: null),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Car", mpg: 30d, price: 4m)]);

        Assert.Equal(0d, cost.MilesPerWeek);
        Assert.False(cost.AnyDriving);
        Assert.Equal(["Work"], cost.MissingMiles);
        Assert.Equal(0m, cost.Vehicles.Single().WeeklyCost);
    }

    [Fact]
    public void A_car_missing_its_mileage_or_its_price_is_not_costed()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 12d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "No mileage", mpg: 0d, price: 4m), Vehicle(2, "No price", mpg: 30d, price: 0m)]);

        Assert.Empty(cost.Vehicles);
        Assert.Null(cost.LowestMonthlyCost);
    }

    [Fact]
    public void Several_destinations_add_up_over_the_week()
    {
        var cost = FuelCostCalculator.Cost(
            Property([(1, 12d), (2, 4d)]),
            [Destination(1, "Work", trips: 3), Destination(2, "Gym", trips: 2)],
            [Vehicle(1, "Car", mpg: 25d, price: 4m)]);

        Assert.Equal(88d, cost.MilesPerWeek);
        Assert.Equal(3.52d, cost.Vehicles.Single().GallonsPerWeek, 2);
        Assert.Equal(14.08m, cost.Vehicles.Single().WeeklyCost, 2);
    }

    private static SearchDestinationView Destination(int id, string name, int trips) =>
        new(id, name, null, 1m, trips, null, null, id);

    private static VehicleView Vehicle(int id, string name, double mpg, decimal price) =>
        new(id, name, mpg, price, id);

    private static PropertyView Property(
        IReadOnlyList<(int DestinationId, double Miles)> drives,
        double? miles = 1d)
    {
        return new PropertyView(
            1,
            "Somewhere",
            null,
            IsCurrent: false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [],
            [],
            drives
                .Select(d => new PropertyDriveTimeView(
                    d.DestinationId,
                    $"Destination {d.DestinationId}",
                    20d,
                    miles is null ? null : d.Miles,
                    DriveTimeSource.Manual,
                    default))
                .ToList(),
            [],
            default,
            default);
    }
}
