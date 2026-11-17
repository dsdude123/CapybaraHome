namespace CapybaraHome.Domain.Homes;

/// <summary>
/// The one row holding the routing service the search uses, which is one account whichever
/// scenario is active; the scoring weights belong to a <see cref="SearchProfile"/>.
/// </summary>
public class HomeSearchSettings
{
    /// <summary>Primary key of the single row.</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Base address of the OpenRouteService instance, which both places an address and measures
    /// the drive. Empty until it is set, and then every drive time is typed in by hand.
    /// </summary>
    public string? RoutingBaseUrl { get; set; }

    /// <summary>Key the service is called with.</summary>
    public string? RoutingApiKey { get; set; }

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
