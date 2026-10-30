namespace CapybaraHome.Domain.Homes;

/// <summary>
/// The one row holding the scoring weights and the routing service the search uses. Every weight
/// starts at zero, so nothing is scored until the numbers are filled in.
/// </summary>
public class HomeSearchSettings
{
    /// <summary>Primary key of the single row.</summary>
    public int Id { get; set; } = 1;

    /// <summary>Points per dollar a month cheaper than the current home; negative fees cost points.</summary>
    public decimal MonthlyFeePointsPerDollar { get; set; }

    /// <summary>Points per dollar of deposit below the current home's, counted once.</summary>
    public decimal DepositPointsPerDollar { get; set; }

    /// <summary>Points per minute of drive saved against the current home, before the destination's weight.</summary>
    public decimal DriveTimePointsPerMinute { get; set; }

    /// <summary>Points for each amenity.</summary>
    public decimal AmenityPoints { get; set; }

    /// <summary>Points for each pro.</summary>
    public decimal ProPoints { get; set; }

    /// <summary>Points taken off for each con.</summary>
    public decimal ConPoints { get; set; }

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
