namespace CapybaraHome.Domain.Homes;

/// <summary>
/// One scenario of the search: its scoring weights, and the destinations, cars and toll roads the
/// scores and the driving costs are worked out from. One profile is active at a time, and copying
/// a profile is how a scenario that differs in one detail is set up.
/// </summary>
public class SearchProfile
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What the scenario is called, such as "Two cars" or "Three days in the office".</summary>
    public required string Name { get; set; }

    /// <summary>Whether this is the profile the pages are showing.</summary>
    public bool IsActive { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }

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

    /// <summary>Points per square foot larger than the current home; a smaller place costs points.</summary>
    public decimal SizePointsPerSquareFoot { get; set; }

    /// <summary>When the profile last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The destinations this scenario drives to.</summary>
    public ICollection<SearchDestination> Destinations { get; set; } = [];

    /// <summary>The cars this scenario drives.</summary>
    public ICollection<Vehicle> Vehicles { get; set; } = [];

    /// <summary>The toll roads this scenario pays.</summary>
    public ICollection<TollRoad> TollRoads { get; set; } = [];
}
