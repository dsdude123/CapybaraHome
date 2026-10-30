namespace CapybaraHome.Domain.Homes;

/// <summary>How long the drive is from one property to one destination.</summary>
public class PropertyDriveTime
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>The property driven from.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>The destination driven to.</summary>
    public int DestinationId { get; set; }

    /// <summary>Navigation to <see cref="DestinationId"/>.</summary>
    public SearchDestination? Destination { get; set; }

    /// <summary>How long the drive takes, in minutes.</summary>
    public double Minutes { get; set; }

    /// <summary>How far the drive is, in miles, when it was measured or typed in.</summary>
    public double? Miles { get; set; }

    /// <summary>
    /// Names of the tolled roads the drive uses: what a measured route reported, or the toll roads
    /// ticked against a drive typed in by hand.
    /// </summary>
    public List<string> TolledRoads { get; set; } = [];

    /// <summary>Whether the time was measured or typed in.</summary>
    public DriveTimeSource Source { get; set; }

    /// <summary>When the time was measured or last typed, in UTC.</summary>
    public DateTimeOffset MeasuredAt { get; set; }
}
