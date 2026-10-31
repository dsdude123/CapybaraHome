namespace CapybaraHome.Domain.Homes;

/// <summary>
/// One line of a property's utility breakdown. The kind is a column rather than part of the text
/// so the same utility can be compared between properties.
/// </summary>
public class PropertyUtility
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property this line belongs to.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>Which utility it is.</summary>
    public UtilityKind Kind { get; set; }

    /// <summary>What the line is called, which names a <see cref="UtilityKind.Other"/> line.</summary>
    public string? Label { get; set; }

    /// <summary>The average monthly cost.</summary>
    public decimal MonthlyCost { get; set; }
}
