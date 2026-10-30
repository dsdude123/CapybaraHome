namespace CapybaraHome.Domain.Homes;

/// <summary>A written pro or con about a property, which the score counts.</summary>
public class PropertyPoint
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property this point is about.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>Which way the point cuts.</summary>
    public PropertyPointKind Kind { get; set; }

    /// <summary>What the point is.</summary>
    public required string Text { get; set; }

    /// <summary>Position in its list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
