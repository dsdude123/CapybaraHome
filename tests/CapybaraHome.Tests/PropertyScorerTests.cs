using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the maths that turns a property into a score.</summary>
public class PropertyScorerTests
{
    private static readonly HomeSearchSettingsView Weights = new(
        MonthlyFeePointsPerDollar: 0.05m,
        DepositPointsPerDollar: 0.01m,
        DriveTimePointsPerMinute: 1.5m,
        AmenityPoints: 3m,
        ProPoints: 2m,
        ConPoints: 4m,
        RoutingBaseUrl: null,
        HasRoutingApiKey: false);

    [Fact]
    public void A_cheaper_month_earns_points_and_a_dearer_one_loses_them()
    {
        var current = Property(1, "Now", rent: 2000m, utilities: 100m);
        var cheaper = Property(2, "Cheaper", rent: 1800m, utilities: 100m);
        var dearer = Property(3, "Dearer", rent: 2300m, utilities: 100m);

        Assert.Equal(10m, PropertyScorer.Score(cheaper, current, [], Weights).MonthlyFees);
        Assert.Equal(-15m, PropertyScorer.Score(dearer, current, [], Weights).MonthlyFees);
    }

    [Fact]
    public void The_month_counts_rent_parking_pet_rent_and_the_utilities()
    {
        var property = Property(1, "All of it", rent: 1000m, parking: 100m, petRent: 50m, utilities: 75m);

        Assert.Equal(1225m, property.MonthlyTotal);
        Assert.Equal(75m, property.UtilitiesTotal);
    }

    [Fact]
    public void A_deposit_only_scores_when_both_are_known()
    {
        var current = Property(1, "Now", deposit: 1000m);
        var known = Property(2, "Known", deposit: 400m);
        var unknown = Property(3, "Unknown");

        Assert.Equal(6m, PropertyScorer.Score(known, current, [], Weights).Deposit);

        var missing = PropertyScorer.Score(unknown, current, [], Weights);
        Assert.Equal(0m, missing.Deposit);
        Assert.Null(missing.DepositDifference);
    }

    [Fact]
    public void A_shorter_drive_earns_points_in_proportion_to_the_destination_weight()
    {
        var destinations = new List<SearchDestinationView>
        {
            new(1, "Work", null, 3m, null, null, 0),
            new(2, "Gym", null, 1m, null, null, 1),
        };

        var current = Property(1, "Now", drives: [(1, 30d), (2, 10d)]);
        var candidate = Property(2, "There", drives: [(1, 20d), (2, 16d)]);

        var score = PropertyScorer.Score(candidate, current, destinations, Weights);

        // Ten minutes saved on a weight of three, six minutes lost on a weight of one.
        Assert.Equal(45m, score.DriveTimeLines.Single(l => l.DestinationId == 1).Points);
        Assert.Equal(-9m, score.DriveTimeLines.Single(l => l.DestinationId == 2).Points);
        Assert.Equal(36m, score.DriveTimes);
    }

    [Theory]
    [InlineData(true, true, 15)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 0)]
    public void A_drive_only_scores_when_both_ends_are_known(bool mine, bool theirs, decimal expected)
    {
        var destinations = new List<SearchDestinationView> { new(1, "Work", null, 1m, null, null, 0) };

        var current = Property(1, "Now", drives: theirs ? [(1, 30d)] : []);
        var candidate = Property(2, "There", drives: mine ? [(1, 20d)] : []);

        var score = PropertyScorer.Score(candidate, current, destinations, Weights);

        Assert.Equal(expected, score.DriveTimes);
        Assert.Equal(mine ? 20d : null, score.DriveTimeLines.Single().Minutes);
        Assert.Equal(theirs ? 30d : null, score.DriveTimeLines.Single().CurrentMinutes);
    }

    [Fact]
    public void Amenities_pros_and_cons_score_on_their_own_without_a_current_home()
    {
        var property = Property(
            1,
            "Alone",
            amenities: ["Gym", "Pool"],
            pros: ["Quiet"],
            cons: ["Dark", "No parking"]);

        var score = PropertyScorer.Score(property, null, [], Weights);

        Assert.Equal(6m, score.Amenities);
        Assert.Equal(2m, score.Pros);
        Assert.Equal(-8m, score.Cons);
        Assert.Null(score.MonthlyDifference);
        Assert.Equal(0m, score.Total);
    }

    [Fact]
    public void Only_the_priorities_marked_as_met_are_counted()
    {
        var property = Property(1, "Marked", priorities: [(1, 10m, true), (2, 20m, false), (3, 5m, null)]);

        Assert.Equal(10m, PropertyScorer.Score(property, null, [], Weights).Priorities);
    }

    [Fact]
    public void The_current_home_is_not_compared_with_itself()
    {
        var destinations = new List<SearchDestinationView> { new(1, "Work", null, 1m, null, null, 0) };
        var current = Property(1, "Now", rent: 2000m, deposit: 500m, drives: [(1, 30d)], amenities: ["Deck"]);

        var score = PropertyScorer.Score(current, current, destinations, Weights);

        Assert.Equal(0m, score.MonthlyFees);
        Assert.Equal(0m, score.Deposit);
        Assert.Equal(0m, score.DriveTimes);
        Assert.Null(score.MonthlyDifference);
        Assert.Equal(3m, score.Amenities);
    }

    [Fact]
    public void Every_weight_at_zero_scores_nothing()
    {
        var none = new HomeSearchSettingsView(0m, 0m, 0m, 0m, 0m, 0m, null, false);
        var current = Property(1, "Now", rent: 3000m);
        var candidate = Property(2, "Cheap", rent: 900m, amenities: ["Gym"], pros: ["Nice"], cons: ["Far"]);

        Assert.Equal(0m, PropertyScorer.Score(candidate, current, [], none).Total);
        Assert.False(none.AnyWeightSet);
        Assert.True(Weights.AnyWeightSet);
    }

    [Fact]
    public void The_total_is_every_component_added_up()
    {
        var destinations = new List<SearchDestinationView> { new(1, "Work", null, 1m, null, null, 0) };
        var current = Property(1, "Now", rent: 2000m, deposit: 1000m, drives: [(1, 30d)]);
        var candidate = Property(
            2, "There", rent: 1900m, deposit: 800m, drives: [(1, 25d)],
            amenities: ["Gym"], pros: ["Quiet"], cons: ["Dark"], priorities: [(1, 10m, true)]);

        var score = PropertyScorer.Score(candidate, current, destinations, Weights);

        Assert.Equal(5m, score.MonthlyFees);
        Assert.Equal(2m, score.Deposit);
        Assert.Equal(7.5m, score.DriveTimes);
        Assert.Equal(5m + 2m + 7.5m + 3m + 2m - 4m + 10m, score.Total);
    }

    private static PropertyView Property(
        int id,
        string name,
        decimal? rent = null,
        decimal? parking = null,
        decimal? petRent = null,
        decimal? deposit = null,
        decimal utilities = 0m,
        IReadOnlyList<string>? amenities = null,
        IReadOnlyList<string>? pros = null,
        IReadOnlyList<string>? cons = null,
        IReadOnlyList<(int DestinationId, double Minutes)>? drives = null,
        IReadOnlyList<(int PriorityId, decimal Points, bool? Meets)>? priorities = null)
    {
        var points = (pros ?? [])
            .Select((text, index) => new PropertyPointView(index + 1, PropertyPointKind.Pro, text, index))
            .Concat((cons ?? [])
                .Select((text, index) => new PropertyPointView(100 + index, PropertyPointKind.Con, text, index)))
            .ToList();

        return new PropertyView(
            id,
            name,
            null,
            IsCurrent: false,
            rent,
            parking,
            petRent,
            deposit,
            null,
            null,
            null,
            utilities == 0m
                ? []
                : [new PropertyUtilityView(1, UtilityKind.Electricity, null, utilities)],
            (amenities ?? []).Select((text, index) => new PropertyAmenityView(index + 1, text, index)).ToList(),
            points,
            [],
            [],
            (drives ?? [])
                .Select(d => new PropertyDriveTimeView(
                    d.DestinationId, $"Destination {d.DestinationId}", d.Minutes, null, DriveTimeSource.Manual, default))
                .ToList(),
            (priorities ?? [])
                .Select(p => new PropertyPriorityView(p.PriorityId, $"Priority {p.PriorityId}", p.Points, p.Meets))
                .ToList(),
            default,
            default);
    }
}
