namespace CapybaraHome.Domain.Homes;

/// <summary>Somewhere the drive from each property is measured to, such as work.</summary>
public class SearchDestination
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What the place is called.</summary>
    public required string Name { get; set; }

    /// <summary>Street address, which the geocoder reads to place it on the map.</summary>
    public string? Address { get; set; }

    /// <summary>
    /// How much this destination's drive counts next to the others. One is ordinary; work is
    /// usually driven to every day, so it is worth setting higher.
    /// </summary>
    public decimal Weight { get; set; } = 1m;

    /// <summary>Latitude the address was resolved to, or null when it has not been placed.</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude the address was resolved to, or null when it has not been placed.</summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// How many round trips a week are driven to it, which is what the fuel cost counts. Three
    /// covers a three-day work week, a third covers somewhere visited every three weeks, and zero
    /// leaves the destination out of the fuel cost.
    /// </summary>
    public decimal RoundTripsPerWeek { get; set; }

    /// <summary>
    /// The car this drive is made in, or null when it is shared out: whoever drives varies, so the
    /// fuel is averaged over every car that can be costed.
    /// </summary>
    public int? VehicleId { get; set; }

    /// <summary>The car named by <see cref="VehicleId"/>.</summary>
    public Vehicle? Vehicle { get; set; }

    /// <summary>
    /// What every drive to this destination is multiplied by, for a commute the routing service
    /// reckons shorter than the traffic actually met. One leaves the times alone, and a time typed
    /// in by hand is stretched like a measured one.
    /// </summary>
    public double DriveTimeMultiplier { get; set; } = 1d;

    /// <summary>What each day driven there pays back, such as a carpool credit from work.</summary>
    public decimal CarpoolCreditPerDay { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }

    /// <summary>The drive times measured to this destination.</summary>
    public ICollection<PropertyDriveTime> DriveTimes { get; set; } = [];
}
