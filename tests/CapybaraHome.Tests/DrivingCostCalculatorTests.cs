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

        // Sixty miles a week: three gallons in the thirsty one at $12, 1.2 in the thrifty one at
        // $4.80. With no car named the drive is shared out, so the fuel is the average, $8.40.
        var line = cost.Lines.Single();

        Assert.Equal(["Thirsty", "Thrifty"], line.Cars);
        Assert.True(line.IsAveraged);
        Assert.Equal(2.1d, line.GallonsPerWeek, 2);
        Assert.Equal(8.40m, line.FuelPerWeek, 2);
        Assert.Equal(8.40m, cost.FuelPerWeek, 2);
        Assert.Equal(36.40m, cost.MonthlyCost, 2);
    }

    [Fact]
    public void A_week_becomes_a_month_at_fifty_two_weeks_a_year()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)],
            []);

        Assert.Equal(9m, cost.WeeklyCost, 2);
        Assert.Equal(39m, cost.MonthlyCost, 2);
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
        Assert.Equal(0m, cost.WeeklyCost);
    }

    [Fact]
    public void A_car_missing_its_mileage_or_its_price_is_not_costed()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 12d)]),
            [Destination(1, "Work", trips: 3)],
            [Vehicle(1, "No mileage", mpg: 0d, price: 4m), Vehicle(2, "No price", mpg: 30d, price: 0m)],
            []);

        Assert.Equal(0m, cost.FuelPerWeek);
        Assert.Equal(0m, cost.MonthlyCost);
        Assert.Equal(["Work"], cost.MissingCars);
        Assert.Empty(cost.Lines.Single().Cars);
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
        Assert.Equal(3.52d, cost.GallonsPerWeek, 2);
        Assert.Equal(14.08m, cost.WeeklyCost, 2);
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
        Assert.Equal(36m, cost.WeeklyCost, 2);
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
    public void A_toll_road_marked_by_hand_is_charged_though_the_route_found_none()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Marked(1, 10d, [1])]),
            [Destination(1, "Work", trips: 3)],
            [],
            [Toll(1, "520", 4.50m), Toll(2, "99", 2.25m)]);

        var line = cost.Lines.Single();
        Assert.Equal(["520"], line.Tolls.Select(t => t.Road));
        Assert.Equal(27m, line.TollsPerWeek);
        Assert.Empty(line.UnpricedTolledRoads);
    }

    [Fact]
    public void Marking_a_drive_takes_the_route_out_of_it_altogether()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Marked(1, 10d, [2], "WA 520", "Tacoma Narrows Bridge")]),
            [Destination(1, "Work", trips: 3)],
            [],
            [Toll(1, "520", 4.50m), Toll(2, "99", 2.25m)]);

        var line = cost.Lines.Single();

        // The 520 the route named is not charged, the 99 marked by hand is, and a tolled road
        // nothing prices is no longer worth flagging: the marks decide.
        Assert.Equal(["99"], line.Tolls.Select(t => t.Road));
        Assert.Equal(13.50m, line.TollsPerWeek);
        Assert.Empty(line.UnpricedTolledRoads);
    }

    [Fact]
    public void Marking_no_toll_road_charges_none_of_them()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Marked(1, 10d, [], "WA 520")]),
            [Destination(1, "Work", trips: 3)],
            [],
            [Toll(1, "520", 4.50m)]);

        Assert.Empty(cost.Lines.Single().Tolls);
        Assert.Equal(0m, cost.TollsPerWeek);
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
        Assert.Equal(21m, cost.WeeklyCost, 2);
        Assert.Equal(91m, cost.MonthlyCost, 2);
        Assert.Equal(12m, cost.TollsLessCreditPerWeek);
        Assert.Equal(12m, cost.Lines.Single().TollsLessCreditPerWeek);
        Assert.Equal(52m, cost.MonthlyTollsLessCredit, 2);
    }

    [Fact]
    public void The_credit_comes_off_the_tolls_even_with_no_car_to_cost_the_fuel_in()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d, "WA 520")]),
            [Destination(1, "Work", trips: 3, carpool: 5m)],
            [],
            [Toll(1, "520", 4.50m)]);

        Assert.Equal(0m, cost.FuelPerWeek);
        Assert.Equal(27m, cost.TollsPerWeek);
        Assert.Equal(15m, cost.CarpoolCreditPerWeek);
        Assert.Equal(12m, cost.TollsLessCreditPerWeek);
        Assert.Equal(52m, cost.MonthlyTollsLessCredit, 2);
    }

    [Fact]
    public void A_credit_bigger_than_the_tolls_leaves_them_owing_nothing_and_then_some()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d)]),
            [Destination(1, "Work", trips: 3, carpool: 5m)],
            [],
            []);

        Assert.Equal(-15m, cost.TollsLessCreditPerWeek);
        Assert.Equal(-65m, cost.MonthlyTollsLessCredit, 2);
    }

    [Fact]
    public void A_credit_larger_than_the_cost_leaves_the_week_in_profit()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 2d)]),
            [Destination(1, "Work", trips: 3, carpool: 5m)],
            [Vehicle(1, "Car", mpg: 30d, price: 4.50m)],
            []);

        Assert.True(cost.WeeklyCost < 0m);
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

    [Fact]
    public void A_drive_with_a_car_of_its_own_is_costed_in_that_car_alone()
    {
        var cars = new List<VehicleView> { Vehicle(1, "Thirsty", 20d, 4m), Vehicle(2, "Thrifty", 50d, 4m) };

        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d), Drive(2, 10d)]),
            [
                Destination(1, "Parents", trips: 3, vehicleId: 1),
                Destination(2, "Work", trips: 3),
            ],
            cars,
            []);

        var parents = cost.Lines.Single(l => l.DestinationId == 1);
        var work = cost.Lines.Single(l => l.DestinationId == 2);

        // The parents' drive is his own car's three gallons at $4; the work drive alternates, so
        // it is the average of $12 and $4.80.
        Assert.Equal(["Thirsty"], parents.Cars);
        Assert.False(parents.IsAveraged);
        Assert.Equal(12m, parents.FuelPerWeek, 2);

        Assert.Equal(["Thirsty", "Thrifty"], work.Cars);
        Assert.Equal(8.40m, work.FuelPerWeek, 2);
        Assert.Equal(20.40m, cost.FuelPerWeek, 2);
        Assert.Empty(cost.MissingCars);
    }

    [Fact]
    public void A_drive_whose_car_cannot_be_costed_is_named_rather_than_averaged()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 10d)]),
            [Destination(1, "Parents", trips: 1, vehicleId: 1)],
            [Vehicle(1, "No price", 30d, 0m), Vehicle(2, "Costable", 30d, 4m)],
            []);

        Assert.Empty(cost.Lines.Single().Cars);
        Assert.Equal(0m, cost.FuelPerWeek);
        Assert.Equal(["Parents"], cost.MissingCars);
    }

    [Fact]
    public void A_fraction_of_a_round_trip_a_week_costs_that_fraction()
    {
        var cost = DrivingCostCalculator.Cost(
            Property([Drive(1, 30d, "WA 520")]),
            [Destination(1, "Parents", trips: 0.5m, carpool: 4m)],
            [Vehicle(1, "Car", 30d, 4.50m)],
            [Toll(1, "520", 4m)]);

        var line = cost.Lines.Single();

        // Half a round trip a week: 30 miles, one gallon at $4.50, one crossing each way at $4,
        // less half a day's $4 credit.
        Assert.Equal(0.5m, line.RoundTripsPerWeek);
        Assert.Equal(30d, cost.MilesPerWeek);
        Assert.Equal(4.50m, cost.FuelPerWeek, 2);
        Assert.Equal(4m, cost.TollsPerWeek, 2);
        Assert.Equal(2m, cost.CarpoolCreditPerWeek, 2);
        Assert.Equal(6.50m, cost.WeeklyCost, 2);
    }

    private static (int DestinationId, double? Miles, string[] Tolled, int[]? Marked) Drive(
        int destinationId,
        double? miles,
        params string[] tolled) => (destinationId, miles, tolled, null);

    /// <summary>A drive whose toll roads were marked by hand rather than taken from the route.</summary>
    private static (int DestinationId, double? Miles, string[] Tolled, int[]? Marked) Marked(
        int destinationId,
        double? miles,
        int[] marked,
        params string[] tolled) => (destinationId, miles, tolled, marked);

    private static SearchDestinationView Destination(
        int id, string name, decimal trips, decimal carpool = 0m, int? vehicleId = null) =>
        new(id, name, null, 1m, trips, carpool, null, null, id, 1d, vehicleId);

    private static VehicleView Vehicle(int id, string name, double mpg, decimal price) =>
        new(id, name, mpg, price, id);

    private static TollRoadView Toll(int id, string road, decimal price) => new(id, road, price, id);

    private static PropertyView Property(IReadOnlyList<(int DestinationId, double? Miles, string[] Tolled, int[]? Marked)> drives)
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
                    default,
                    20d,
                    d.Marked))
                .ToList(),
            [],
            default,
            default);
    }
}
