namespace CapybaraHome.Application.Homes;

/// <summary>
/// Turns a property into a score. Fees and drive times are scored as differences from the home
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
    public static PropertyScore Score(
        PropertyView property,
        PropertyView? current,
        IReadOnlyList<SearchDestinationView> destinations,
        HomeSearchSettingsView settings)
    {
        var sameProperty = current is not null && current.Id == property.Id;
        var baseline = sameProperty ? null : current;

        var monthlyDifference = baseline is null ? (decimal?)null : baseline.MonthlyTotal - property.MonthlyTotal;
        var depositDifference = baseline is null || baseline.Deposit is null || property.Deposit is null
            ? (decimal?)null
            : baseline.Deposit.Value - property.Deposit.Value;

        var driveLines = DriveLines(property, baseline, destinations, settings);

        return new PropertyScore(
            PropertyId: property.Id,
            MonthlyFees: (monthlyDifference ?? 0m) * settings.MonthlyFeePointsPerDollar,
            Deposit: (depositDifference ?? 0m) * settings.DepositPointsPerDollar,
            DriveTimes: driveLines.Sum(line => line.Points),
            Amenities: property.Amenities.Count * settings.AmenityPoints,
            Pros: property.Pros.Count() * settings.ProPoints,
            Cons: -property.Cons.Count() * settings.ConPoints,
            Priorities: property.Priorities.Where(p => p.Meets == true).Sum(p => p.Points),
            DriveTimeLines: driveLines,
            MonthlyDifference: monthlyDifference,
            DepositDifference: depositDifference);
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
            var mine = property.DriveTimes.FirstOrDefault(d => d.DestinationId == destination.Id)?.Minutes;
            var theirs = baseline?.DriveTimes.FirstOrDefault(d => d.DestinationId == destination.Id)?.Minutes;

            var points = mine is { } minutes && theirs is { } current
                ? (decimal)(current - minutes) * settings.DriveTimePointsPerMinute * destination.Weight
                : 0m;

            lines.Add(new DriveTimeScore(destination.Id, destination.Name, mine, theirs, points));
        }

        return lines;
    }
}
