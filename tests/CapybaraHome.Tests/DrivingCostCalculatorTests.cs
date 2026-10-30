using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the maths that turns the week's round trips into fuel, tolls and carpool credit.</summary>
public class DrivingCostCalculatorTests
{
    [Fact]
    public void A_three_day_week_counts_six_one_way_drives()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 12d)]),
            [Destination(1, "Work", trips: 3)],
            [],
            []);

        Assert.Equal(72d, cost.MilesPerWeek);
        Assert.Equal(72d, cost.Lines.Single().MilesPerWeek);
        Assert.True(cost.AnyDriving);
    }

    [Fact]
    public void Each_car_costs_the_same_miles_at_its_own_mileage_and_price()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Thirsty", mpg: 20d, price: 4m), Vehicle(2, "Thrifty", mpg: 50d, price: 4m)],
            []);

        var thirsty = cost.Vehicles.Single(v => v.VehicleId == 1);
        var thrifty = cost.Vehicles.Single(v => v.VehicleId == 2);

        Assert.Equal(3d, thirsty.GallonsPerWeek);
        Assert.Equal(12m, thirsty.FuelPerWeek);
        Assert.Equal(12m, thirsty.WeeklyCost);
        Assert.Equal(20.80m, thrifty.MonthlyCost, 2);
        Assert.Equal(thrifty.MonthlyCost, cost.LowestMonthlyCost);
    }

    [Fact]
    public void A_week_becomes_a_month_at_fifty_two_weeks_a_year()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)],
            []);

        var vehicle = cost.Vehicles.Single();

        Assert.Equal(9m, vehicle.WeeklyCost, 2);
        Assert.Equal(39m, vehicle.MonthlyCost, 2);
    }

    [Fact]
    public void A_destination_driven_to_no_days_a_week_is_left_out()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 12d), Drive(2, 5d)]),
            [Destination(1, "Work", trips: 3), Destination(2, "Gym", trips: 0)],
            [],
            []);

        Assert.Equal("Work", Assert.Single(cost.Lines).Name);
    }

    [Fact]
    public void A_drive_with_no_distance_adds_no_fuel_and_is_named()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, null)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Car", mpg: 30d, price: 4m)],
            []);

        Assert.Equal(0d, cost.MilesPerWeek);
        Assert.False(cost.AnyDriving);
        Assert.Equal(["Work"], cost.MissingMiles);
        Assert.Equal(0m, cost.Vehicles.Single().WeeklyCost);
    }

    [Fact]
    public void A_car_missing_its_mileage_or_its_price_is_not_costed()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 12d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "No mileage", mpg: 0d, price: 4m), Vehicle(2, "No price", mpg: 30d, price: 0m)],
            []);

        Assert.Empty(cost.Vehicles);
        Assert.Null(cost.LowestMonthlyCost);
    }

    [Fact]
    public void Several_destinations_add_up_over_the_week()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 12d), Drive(2, 4d)]),
            [Destination(1, "Work", trips: 3), Destination(2, "Gym", trips: 2)],
            [Vehicle(1, "Car", mpg: 25d, price: 4m)],
            []);

        Assert.Equal(88d, cost.MilesPerWeek);
        Assert.Equal(3.52d, cost.Vehicles.Single().GallonsPerWeek, 2);
        Assert.Equal(14.08m, cost.Vehicles.Single().WeeklyCost, 2);
    }

    [Fact]
    public void A_toll_is_paid_each_way_on_every_round_trip()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d, "Evergreen Point Floating Bridge (SR 520)")]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)],
            [Toll(1, "520", 4.50m), Toll(2, "99", 2.25m)]);

        var line = cost.Lines.Single();

        Assert.Equal(["520"], line.Tolls.Select(t => t.Road));
        Assert.Equal(27m, line.TollsPerWeek);
        Assert.Equal(27m, cost.TollsPerWeek);
        Assert.Equal(36m, cost.Vehicles.Single().WeeklyCost, 2);
        Assert.Empty(line.UnpricedTolledRoads);
    }

    [Fact]
    public void Two_tolls_on_one_drive_are_both_paid()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d, "SR 99 Tunnel", "WA 520")]),
            [Destination(1, "Work", trips: 1)],
            [],
            [Toll(1, "520", 4.50m), Toll(2, "99", 2.25m)]);

        Assert.Equal(13.50m, cost.TollsPerWeek);
    }

    [Fact]
    public void Two_names_for_the_same_toll_road_are_paid_once()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d, "WA 520", "Evergreen Point Floating Bridge (520)")]),
            [Destination(1, "Work", trips: 1)],
            [],
            [Toll(1, "520", 4.50m)]);

        Assert.Equal(9m, cost.TollsPerWeek);
    }

    [Fact]
    public void A_tolled_road_nothing_prices_is_named_rather_than_charged()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d, "Tacoma Narrows Bridge")]),
            [Destination(1, "Work", trips: 3)],
            [],
            [Toll(1, "520", 4.50m)]);

        Assert.Equal(0m, cost.TollsPerWeek);
        Assert.Equal(["Tacoma Narrows Bridge"], cost.Lines.Single().UnpricedTolledRoads);
    }

    [Fact]
    public void A_toll_road_matches_whatever_case_the_route_writes_it_in()
    {
        var toll = Toll(1, "evergreen point", 4.50m);

        Assert.True(toll.Prices("Evergreen Point Floating Bridge"));
        Assert.False(toll.Prices("Interstate 5"));
    }

    [Fact]
    public void The_carpool_credit_comes_off_each_day_driven_there()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d, "WA 520")]),
            [Destination(1, "Work", trips: 3, carpool: 5m)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)],
            [Toll(1, "520", 4.50m)]);

        // $9 of fuel and $27 of tolls, less three days at $5.
        Assert.Equal(15m, cost.CarpoolCreditPerWeek);
        Assert.Equal(21m, cost.Vehicles.Single().WeeklyCost, 2);
        Assert.Equal(91m, cost.Vehicles.Single().MonthlyCost, 2);
        Assert.Equal(52m, cost.MonthlyTollsLessCredit, 2);
    }

    [Fact]
    public void A_credit_larger_than_the_cost_leaves_the_week_in_profit()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 2d)]),
            [Destination(1, "Work", trips: 3, carpool: 5m)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)],
            []);

        Assert.True(cost.Vehicles.Single().WeeklyCost < 0m);
    }

    [Fact]
    public void A_carpool_credit_alone_still_counts_as_driving_to_cost()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([]),
            [Destination(1, "Work", trips: 3, carpool: 5m)],
            [],
            []);

        Assert.True(cost.AnyDriving);
        Assert.Equal(["Work"], cost.MissingMiles);
    }

    private static (int DestinationId, double? Miles, string[] Tolled) Drive(
        int destinationId,
        double? miles,
        params string[] tolled) => (destinationId, miles, tolled);

    private static SearchDestinationView Destination(int id, string name, int trips, decimal carpool = 0m) =>
        new(id, name, null, 1m, trips, carpool, null, null, id);

    private static VehicleView Vehicle(int id, string name, double mpg, decimal price) =>
        new(id, name, mpg, price, id);

    private static TollRoadView Toll(int id, string road, decimal price) => new(id, road, price, id);

    private static PropertyView Property(IReadOnlyList<(int DestinationId, double? Miles, string[] Tolled)> drives)
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
                    d.Miles,
                    d.Tolled,
                    DriveTimeSource.Manual,
                    default))
                .ToList(),
            [],
            default,
            default);
    }
}
