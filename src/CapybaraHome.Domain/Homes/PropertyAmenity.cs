namespace CapybaraHome.Domain.Homes;

/// <summary>Something the property offers, such as a gym, a pool or a lounge.</summary>
public class PropertyAmenity
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property this amenity belongs to.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>What the amenity is.</summary>
    public required string Name { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
