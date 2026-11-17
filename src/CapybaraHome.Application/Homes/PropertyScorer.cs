namespace CapybaraHome.Application.Homes;

/// <summary>
/// Turns a property into a score. Fees, size and drive times are scored as differences from the home
/// lived in now, so a search with no current home scores only what stands on its own: the
/// amenities, the pros and cons, and the priorities met.
/// </summary>
public static class PropertyScorer
{
    /// <summary>Scores <paramref name="property"/> against <paramref name="current"/> under <paramref name="settings"/>.</summary>
    /// <param name="property">The property to score.</param>
    /// <param name="current">The home lived in now, or null when none is marked.</param>
    /// <param name="destinations">The destinations drives are compared for.</param>
    /// <param name="settings">The weights to apply.</param>
    /// <param name="vehicles">The cars the driving is costed in, or none to leave the fuel out of it.</param>
    /// <param name="tollRoads">The toll roads and what each crossing costs, or none for no tolls.</param>
    public static PropertyScore Score(
        PropertyView property,
        PropertyView? current,
        IReadOnlyList<SearchDestinationView> destinations,
        HomeSearchSettingsView settings,
        IReadOnlyList<VehicleView>? vehicles = null,
        IReadOnlyList<TollRoadView>? tollRoads = null)
    {
        var sameProperty = current is not null && current.Id == property.Id;
        var baseline = sameProperty ? null : current;

        var monthlyDifference = baseline is null ? (decimal?)null : baseline.MonthlyTotal - property.MonthlyTotal;
        var depositDifference = baseline is null || baseline.Deposit is null || property.Deposit is null
            ? (decimal?)null
            : baseline.Deposit.Value - property.Deposit.Value;

        var sizeDifference = baseline?.SquareFeet is { } theirs && property.SquareFeet is { } mine
            ? mine - theirs
            : (int?)null;

        var drivingDifference = Driving(property, baseline, destinations, vehicles ?? [], tollRoads ?? []);

        var driveLines = DriveLines(property, baseline, destinations, settings);

        return new PropertyScore(
            PropertyId: property.Id,
            MonthlyFees: (monthlyDifference ?? 0m) * settings.MonthlyFeePointsPerDollar,
            Deposit: (depositDifference ?? 0m) * settings.DepositPointsPerDollar,
            Driving: (drivingDifference ?? 0m) * settings.MonthlyFeePointsPerDollar,
            Size: (sizeDifference ?? 0) * settings.SizePointsPerSquareFoot,
            DriveTimes: driveLines.Sum(line => line.Points),
            Amenities: property.Amenities.Count * settings.AmenityPoints,
            Pros: property.Pros.Count() * settings.ProPoints,
            Cons: -property.Cons.Count() * settings.ConPoints,
            Priorities: property.Priorities.Where(p => p.Meets == true).Sum(p => p.Points),
            DriveTimeLines: driveLines,
            MonthlyDifference: monthlyDifference,
            DepositDifference: depositDifference,
            DrivingDifference: drivingDifference,
            SizeDifference: sizeDifference);
    }

    // What a month's driving costs is money out like the fees, so it scores at the same points a
    // dollar. A property whose distances are not all known would look cheap to drive from, so the
    // difference only counts where both ends have every distance.
    private static decimal? Driving(
        PropertyView property,
        PropertyView? baseline,
        IReadOnlyList<SearchDestinationView> destinations,
        IReadOnlyList<VehicleView> vehicles,
        IReadOnlyList<TollRoadView> tollRoads)
    {
        if (baseline is null)
        {
            return null;
        }

        var mine = DrivingCostCalculator.Cost(property, destinations, vehicles, tollRoads);
        var theirs = DrivingCostCalculator.Cost(baseline, destinations, vehicles, tollRoads);

        return mine.MissingMiles.Count > 0 || theirs.MissingMiles.Count > 0
            ? null
            : theirs.MonthlyCost - mine.MonthlyCost;
    }

    // A destination only scores where both drives are known: with one of them missing there is no
    // difference to weigh, and treating the missing one as zero would invent a saving.
    private static List<DriveTimeScore> DriveLines(
        PropertyView property,
        PropertyView? baseline,
        IReadOnlyList<SearchDestinationView> destinations,
        HomeSearchSettingsView settings)
    {
        var lines = new List<DriveTimeScore>();

        foreach (var destination in destinations)
        {
            // The adjusted minutes are what the drive is reckoned to take, so they are what a
            // saving is weighed in.
            var mine = property.DriveTimes.FirstOrDefault(d => d.DestinationId == destination.Id)?.AdjustedMinutes;
            var theirs = baseline?.DriveTimes.FirstOrDefault(d => d.DestinationId == destination.Id)?.AdjustedMinutes;

            var points = mine is { } minutes && theirs is { } current
                ? (decimal)(current - minutes) * settings.DriveTimePointsPerMinute * destination.Weight
                : 0m;

            lines.Add(new DriveTimeScore(destination.Id, destination.Name, mine, theirs, points));
        }

        return lines;
    }
}
