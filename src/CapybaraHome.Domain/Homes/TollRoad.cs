namespace CapybaraHome.Domain.Homes;

/// <summary>A toll road and what crossing it costs at the peak.</summary>
public class TollRoad : IProfileOwned
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>The scenario this toll road belongs to.</summary>
    public int ProfileId { get; set; }

    /// <summary>The profile named by <see cref="ProfileId"/>.</summary>
    public SearchProfile? Profile { get; set; }

    /// <summary>
    /// What to look for in the names of the tolled roads a route reports; any road whose name
    /// contains it is charged this toll, so "520" covers the SR 520 bridge.
    /// </summary>
    public required string Road { get; set; }

    /// <summary>What one crossing costs at the peak, in either direction.</summary>
    public decimal PeakToll { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
