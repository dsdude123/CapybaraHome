namespace CapybaraHome.Domain.Meals;

/// <summary>
/// One line of a meal's ingredient list. The quantity and the item are separate columns on
/// purpose, so that the items can later be gathered without parsing the text again.
/// </summary>
public class MealIngredient
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Meal this line belongs to.</summary>
    public int MealId { get; set; }

    /// <summary>Navigation to <see cref="MealId"/>.</summary>
    public Meal? Meal { get; set; }

    /// <summary>How much of it is needed, as written, for example "2 cups".</summary>
    public string? Quantity { get; set; }

    /// <summary>What the ingredient is.</summary>
    public required string Item { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
