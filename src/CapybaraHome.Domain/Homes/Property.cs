namespace CapybaraHome.Domain.Homes;

/// <summary>
/// A home being tracked: either a candidate or the one lived in now, which is the baseline the
/// candidates' fees and drive times are scored against.
/// </summary>
public class Property
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What the place is called, such as the building or the listing name.</summary>
    public required string Name { get; set; }

    /// <summary>Street address, which the geocoder reads to place it on the map.</summary>
    public string? Address { get; set; }

    /// <summary>Whether this is the home lived in now rather than a candidate.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Monthly rent.</summary>
    public decimal? Rent { get; set; }

    /// <summary>Monthly parking fee.</summary>
    public decimal? ParkingFee { get; set; }

    /// <summary>Monthly pet rent.</summary>
    public decimal? PetRent { get; set; }

    /// <summary>Deposit, which is paid once rather than monthly.</summary>
    public decimal? Deposit { get; set; }

    /// <summary>What internet the place provides, as written, such as "fibre included, 1 Gbps".</summary>
    public string? Internet { get; set; }

    /// <summary>Latitude the address was resolved to, or null when it has not been placed.</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude the address was resolved to, or null when it has not been placed.</summary>
    public double? Longitude { get; set; }

    /// <summary>The monthly utilities, broken down by kind.</summary>
    public ICollection<PropertyUtility> Utilities { get; set; } = [];

    /// <summary>The amenities the place offers.</summary>
    public ICollection<PropertyAmenity> Amenities { get; set; } = [];

    /// <summary>The written pros and cons.</summary>
    public ICollection<PropertyPoint> Points { get; set; } = [];

    /// <summary>Notes written while looking at the place.</summary>
    public ICollection<PropertyNote> Notes { get; set; } = [];

    /// <summary>Pictures of the place.</summary>
    public ICollection<PropertyPhoto> Photos { get; set; } = [];

    /// <summary>The measured or typed drive times to the destinations.</summary>
    public ICollection<PropertyDriveTime> DriveTimes { get; set; } = [];

    /// <summary>Which priorities the place was marked against.</summary>
    public ICollection<PropertyPriority> Priorities { get; set; } = [];

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
