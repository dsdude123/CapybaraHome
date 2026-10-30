namespace CapybaraHome.Domain.Grocery;

/// <summary>Something bought often enough to be asked about every time a list is generated.</summary>
public class FrequentGroceryItem
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>How much is usually bought, as written, or null.</summary>
    public string? Quantity { get; set; }

    /// <summary>What the item is.</summary>
    public required string Item { get; set; }

    /// <summary>Position in the list offered; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
