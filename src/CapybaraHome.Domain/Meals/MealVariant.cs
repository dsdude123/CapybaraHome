namespace CapybaraHome.Domain.Meals;

/// <summary>
/// A version of a meal that changes its directions or its ingredients. A variant has no dates
/// and no status of its own: it shares its parent's, so cooking one variant counts as cooking
/// the meal.
/// </summary>
public class MealVariant
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Meal this variant belongs to.</summary>
    public int MealId { get; set; }

    /// <summary>Navigation to <see cref="MealId"/>.</summary>
    public Meal? Meal { get; set; }

    /// <summary>What the variant is called.</summary>
    public required string Name { get; set; }

    /// <summary>What the variant changes about the directions, held as markdown.</summary>
    public string? DirectionsModifications { get; set; }

    /// <summary>Position in the meal's list of variants; lower sorts first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Ingredients the variant adds to the meal's list.</summary>
    public ICollection<MealVariantIngredient> AddedIngredients { get; set; } = [];

    /// <summary>The meal's ingredients the variant leaves out.</summary>
    public ICollection<MealVariantRemoval> RemovedIngredients { get; set; } = [];
}
