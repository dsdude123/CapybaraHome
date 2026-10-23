namespace CapybaraHome.Domain.Meals;

/// <summary>One line a variant adds to its meal's ingredient list.</summary>
public class MealVariantIngredient
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Variant this line belongs to.</summary>
    public int MealVariantId { get; set; }

    /// <summary>Navigation to <see cref="MealVariantId"/>.</summary>
    public MealVariant? Variant { get; set; }

    /// <summary>How much of it is needed, as written, for example "2 cups".</summary>
    public string? Quantity { get; set; }

    /// <summary>What the ingredient is.</summary>
    public required string Item { get; set; }

    /// <summary>Position in the list; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
