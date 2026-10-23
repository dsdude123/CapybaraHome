using System.ComponentModel;
using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;
using ModelContextProtocol.Server;

namespace CapybaraHome.Web.Mcp;

/// <summary>
/// The meal tools the Claude connector calls. They are a thin layer over the same service the
/// web UI uses, so the two can never disagree about what a meal is.
/// </summary>
[McpServerToolType]
public class MealTools
{
    /// <summary>Finds meals by text, status or tag.</summary>
    /// <param name="meals">Meal service.</param>
    /// <param name="text">Words to look for in the name, the directions or the ingredients.</param>
    /// <param name="status">Keep only meals in this state: Pooled, Available, Queued, Planned, PlannedShared or Closed.</param>
    /// <param name="tagIds">Keep only meals carrying every one of these tags.</param>
    /// <param name="sort">Order to read them in; defaults to the longest since cooking first.</param>
    /// <param name="limit">Most meals to return; defaults to 25.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "search_meals")]
    [Description("Search the meals by text, status or tag.")]
    public static async Task<IReadOnlyList<MealView>> SearchMealsAsync(
        MealService meals,
        string? text = null,
        MealStatus? status = null,
        int[]? tagIds = null,
        MealSort sort = MealSort.LastCooked,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var filter = new MealFilter(
            Text: text,
            Statuses: status is { } one ? [one] : null,
            TagIds: tagIds,
            Limit: Math.Clamp(limit, 1, 200));

        return await meals.ListAsync(filter, sort, cancellationToken);
    }

    /// <summary>Reads one meal in full, with its ingredients and tags.</summary>
    /// <param name="meals">Meal service.</param>
    /// <param name="id">Id of the meal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_meal")]
    [Description("Get one meal by its id, with its ingredients and tags.")]
    public static Task<MealView?> GetMealAsync(
        MealService meals,
        int id,
        CancellationToken cancellationToken = default)
    {
        return meals.GetAsync(id, cancellationToken);
    }

    /// <summary>Adds a meal.</summary>
    /// <param name="meals">Meal service.</param>
    /// <param name="name">What the meal is called.</param>
    /// <param name="directions">How to cook it, as markdown.</param>
    /// <param name="status">State to start in; defaults to Pooled.</param>
    /// <param name="lastCookedAt">Day it was last cooked, as yyyy-MM-dd.</param>
    /// <param name="ingredients">The ingredient list, each line a quantity and an item.</param>
    /// <param name="tagIds">Tags to put on it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "create_meal")]
    [Description("Add a meal, with its ingredients kept apart from its directions.")]
    public static Task<MealView> CreateMealAsync(
        MealService meals,
        string name,
        string? directions = null,
        MealStatus status = MealStatus.Pooled,
        DateOnly? lastCookedAt = null,
        MealIngredientInput[]? ingredients = null,
        int[]? tagIds = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateMealRequest(name, directions, status, false, lastCookedAt, ingredients, tagIds);
        return meals.CreateAsync(request, cancellationToken);
    }

    /// <summary>Changes a meal. Anything left out keeps its current value.</summary>
    /// <param name="meals">Meal service.</param>
    /// <param name="id">Id of the meal to change.</param>
    /// <param name="name">New name.</param>
    /// <param name="directions">New directions.</param>
    /// <param name="status">New state. Moving it to Available or Pooled stamps today as the last cooked date.</param>
    /// <param name="lastCookedAt">New last cooked date, as yyyy-MM-dd.</param>
    /// <param name="ingredients">The whole ingredient list, replacing the one held.</param>
    /// <param name="tagIds">The whole set of tags, replacing the one held.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "update_meal")]
    [Description("Change a meal. Fields left out keep their current value.")]
    public static Task<MealView?> UpdateMealAsync(
        MealService meals,
        int id,
        string? name = null,
        string? directions = null,
        MealStatus? status = null,
        DateOnly? lastCookedAt = null,
        MealIngredientInput[]? ingredients = null,
        int[]? tagIds = null,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateMealRequest(
            Name: name,
            Directions: directions,
            Status: status,
            LastCookedAt: lastCookedAt,
            Ingredients: ingredients,
            TagIds: tagIds);

        return meals.UpdateAsync(id, request, cancellationToken);
    }

    /// <summary>Adds a variant to a meal.</summary>
    /// <param name="meals">Meal service.</param>
    /// <param name="mealId">Id of the meal the variant belongs to.</param>
    /// <param name="name">What the variant is called.</param>
    /// <param name="directionsModifications">What it changes about the directions.</param>
    /// <param name="addedIngredients">Ingredients it adds, each a quantity and an item.</param>
    /// <param name="removedIngredientIds">Ids of the meal's ingredients it leaves out.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "create_meal_variant")]
    [Description("Add a variant of a meal. A variant shares its meal's status and last cooked date.")]
    public static Task<MealVariantView?> CreateMealVariantAsync(
        MealService meals,
        int mealId,
        string name,
        string? directionsModifications = null,
        MealIngredientInput[]? addedIngredients = null,
        int[]? removedIngredientIds = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MealVariantRequest(name, directionsModifications, addedIngredients, removedIngredientIds);
        return meals.CreateVariantAsync(mealId, request, cancellationToken);
    }

    /// <summary>Lists the meal tags and the facet each one describes.</summary>
    /// <param name="tags">Meal tag service.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "list_meal_tags")]
    [Description("List the meal tags and the facet each one describes.")]
    public static async Task<IReadOnlyList<MealTagView>> ListMealTagsAsync(
        MealTagService tags,
        CancellationToken cancellationToken = default)
    {
        return await tags.ListAsync(cancellationToken);
    }
}
