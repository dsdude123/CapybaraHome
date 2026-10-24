namespace CapybaraHome.Domain.Meals;

/// <summary>One of the meal's ingredients that a variant leaves out.</summary>
public class MealVariantRemoval
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Variant that leaves the ingredient out.</summary>
    public int MealVariantId { get; set; }

    /// <summary>Navigation to <see cref="MealVariantId"/>.</summary>
    public MealVariant? Variant { get; set; }

    /// <summary>The meal ingredient being left out.</summary>
    public int MealIngredientId { get; set; }

    /// <summary>Navigation to <see cref="MealIngredientId"/>.</summary>
    public MealIngredient? Ingredient { get; set; }
}
