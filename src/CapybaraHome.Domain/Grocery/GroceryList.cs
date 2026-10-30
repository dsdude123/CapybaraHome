namespace CapybaraHome.Domain.Grocery;

/// <summary>
/// A shopping list. One list is open at a time and stays open until a fresh one is started by
/// hand, so an item thought of mid-week has somewhere to go.
/// </summary>
public class GroceryList
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>When the list was started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When a fresh list replaced this one, or null while it is the open list.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>What is on the list.</summary>
    public ICollection<GroceryItem> Items { get; set; } = [];
}
