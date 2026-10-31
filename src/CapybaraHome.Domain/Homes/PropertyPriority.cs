namespace CapybaraHome.Domain.Homes;

/// <summary>
/// How one property was marked against one priority. A property with no row for a priority has
/// not been marked yet, which is not the same as not meeting it.
/// </summary>
public class PropertyPriority
{
    /// <summary>The property, and half the primary key.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>The priority, and the other half of the primary key.</summary>
    public int PriorityId { get; set; }

    /// <summary>Navigation to <see cref="PriorityId"/>.</summary>
    public SearchPriority? Priority { get; set; }

    /// <summary>Whether the property meets it.</summary>
    public bool Meets { get; set; }
}
