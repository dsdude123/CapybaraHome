namespace CapybaraHome.Application.Meals;

/// <summary>
/// Works out what a variant changes about its meal, so the screens that list, choose and print
/// one all read the same answer.
/// </summary>
public static class MealVariants
{
    /// <summary>Returns the meal's ingredients the variant leaves out, in the meal's order.</summary>
    /// <param name="meal">The meal the variant belongs to.</param>
    /// <param name="variant">The variant.</param>
    public static IReadOnlyList<MealIngredientView> Removed(MealView meal, MealVariantView variant)
    {
        if (variant.RemovedIngredientIds.Count == 0)
        {
            return [];
        }

        return meal.Ingredients.Where(i => variant.RemovedIngredientIds.Contains(i.Id)).ToList();
    }

    /// <summary>
    /// Returns the ingredient list the variant is cooked from: the meal's, without what it leaves
    /// out, then what it adds.
    /// </summary>
    /// <param name="meal">The meal the variant belongs to.</param>
    /// <param name="variant">The variant.</param>
    public static IReadOnlyList<MealIngredientView> Ingredients(MealView meal, MealVariantView variant)
    {
        var kept = meal.Ingredients.Where(i => !variant.RemovedIngredientIds.Contains(i.Id));
        return kept.Concat(variant.AddedIngredients).ToList();
    }

    /// <summary>Returns an ingredient as one line of text, with no quantity when it has none.</summary>
    /// <param name="ingredient">The ingredient.</param>
    public static string Line(MealIngredientView ingredient) =>
        $"{ingredient.Quantity} {ingredient.Item}".Trim();
}
