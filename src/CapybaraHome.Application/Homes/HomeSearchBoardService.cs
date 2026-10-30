namespace CapybaraHome.Application.Homes;

/// <summary>A property with the score it earned.</summary>
/// <param name="Property">The property.</param>
/// <param name="Score">Its score, broken down.</param>
/// <param name="Driving">What the usual week's driving from it costs.</param>
public record ScoredProperty(PropertyView Property, PropertyScore Score, PropertyDrivingCost Driving);

/// <summary>
/// Everything a screen needs to show the search at once: the home lived in now, the candidates in
/// score order, and the setup the scores came from.
/// </summary>
/// <param name="Current">The home lived in now, or null when none is marked.</param>
/// <param name="Candidates">The other properties, best score first.</param>
/// <param name="CurrentScore">The current home's own score, or null when none is marked.</param>
/// <param name="Destinations">The destinations drives are measured to.</param>
/// <param name="Priorities">What a home has to do.</param>
/// <param name="Vehicles">The cars the drives are made in.</param>
/// <param name="TollRoads">The toll roads and what each crossing costs.</param>
/// <param name="CurrentDriving">What the current home's driving costs, or null when none is marked.</param>
/// <param name="Settings">The weights the scores were worked out under.</param>
public record HomeSearchBoard(
    PropertyView? Current,
    IReadOnlyList<ScoredProperty> Candidates,
    PropertyScore? CurrentScore,
    IReadOnlyList<SearchDestinationView> Destinations,
    IReadOnlyList<SearchPriorityView> Priorities,
    IReadOnlyList<VehicleView> Vehicles,
    IReadOnlyList<TollRoadView> TollRoads,
    PropertyDrivingCost? CurrentDriving,
    HomeSearchSettingsView Settings);

/// <summary>Puts the properties, the setup and the scores together.</summary>
public class HomeSearchBoardService(PropertyService properties, HomeSearchService search)
{
    /// <summary>Returns the whole search, with every property scored.</summary>
    public async Task<HomeSearchBoard> GetAsync(CancellationToken cancellationToken = default)
    {
        var all = await properties.ListAsync(cancellationToken);
        var destinations = await search.ListDestinationsAsync(cancellationToken);
        var priorities = await search.ListPrioritiesAsync(cancellationToken);
        var vehicles = await search.ListVehiclesAsync(cancellationToken);
        var tollRoads = await search.ListTollRoadsAsync(cancellationToken);
        var settings = await search.GetSettingsAsync(cancellationToken);

        var current = all.FirstOrDefault(p => p.IsCurrent);

        var candidates = all
            .Where(p => !p.IsCurrent)
            .Select(p => new ScoredProperty(
                p,
                PropertyScorer.Score(p, current, destinations, settings),
                DrivingCostCalculator.Cost(p, destinations, vehicles, tollRoads)))
            .OrderByDescending(scored => scored.Score.Total)
            .ThenBy(scored => scored.Property.Name)
            .ToList();

        var currentScore = current is null
            ? null
            : PropertyScorer.Score(current, current, destinations, settings);

        var currentDriving = current is null
            ? null
            : DrivingCostCalculator.Cost(current, destinations, vehicles, tollRoads);

        return new HomeSearchBoard(
            current, candidates, currentScore, destinations, priorities, vehicles, tollRoads, currentDriving, settings);
    }

    /// <summary>Returns one property with its score, or null when no property has that id.</summary>
    public async Task<ScoredProperty?> GetScoredAsync(int id, CancellationToken cancellationToken = default)
    {
        var property = await properties.GetAsync(id, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var current = await properties.GetCurrentAsync(cancellationToken);
        var destinations = await search.ListDestinationsAsync(cancellationToken);
        var vehicles = await search.ListVehiclesAsync(cancellationToken);
        var tollRoads = await search.ListTollRoadsAsync(cancellationToken);
        var settings = await search.GetSettingsAsync(cancellationToken);

        return new ScoredProperty(
            property,
            PropertyScorer.Score(property, current, destinations, settings),
            DrivingCostCalculator.Cost(property, destinations, vehicles, tollRoads));
    }
}
