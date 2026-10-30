namespace CapybaraHome.Domain.Grocery;

/// <summary>One thing to buy, with the quantity kept apart from the item the way ingredients are.</summary>
public class GroceryItem
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>List this item is on.</summary>
    public int GroceryListId { get; set; }

    /// <summary>Navigation to <see cref="GroceryListId"/>.</summary>
    public GroceryList? List { get; set; }

    /// <summary>How much to buy, as written, for example "2 cups".</summary>
    public string? Quantity { get; set; }

    /// <summary>What to buy.</summary>
    public required string Item { get; set; }

    /// <summary>Where the item came from, such as the meal it is an ingredient of, or null when it was typed in.</summary>
    public string? Source { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
