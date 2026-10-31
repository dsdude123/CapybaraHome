namespace CapybaraHome.Domain.Homes;

/// <summary>Something a home has to do, worth its own points when a property meets it.</summary>
public class SearchPriority
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What the priority is.</summary>
    public required string Name { get; set; }

    /// <summary>Points a property earns for meeting it, which can be negative.</summary>
    public decimal Points { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }

    /// <summary>How the properties were marked against this priority.</summary>
    public ICollection<PropertyPriority> Properties { get; set; } = [];
}
