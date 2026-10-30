using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Application.Meals;

/// <summary>One line of an ingredient list as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Quantity">How much is needed, as written, or null.</param>
/// <param name="Item">What the ingredient is.</param>
/// <param name="SortOrder">Position in the list.</param>
public record MealIngredientView(int Id, string? Quantity, string Item, int SortOrder);

/// <summary>A tag as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">Display name.</param>
/// <param name="Facet">Which side of a meal the tag describes.</param>
public record MealTagView(int Id, string Name, MealTagFacet Facet);

/// <summary>A tag with the number of meals carrying it, for the screen that manages them.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">Display name.</param>
/// <param name="Facet">Which side of a meal the tag describes.</param>
/// <param name="MealCount">How many meals carry the tag.</param>
public record MealTagUsageView(int Id, string Name, MealTagFacet Facet, int MealCount);

/// <summary>A meal carrying a tag, named so a deletion can say what it will change.</summary>
/// <param name="Id">Primary key of the meal.</param>
/// <param name="Name">What the meal is called.</param>
public record MealTagHolderView(int Id, string Name);

/// <summary>A variant as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="MealId">Meal the variant belongs to.</param>
/// <param name="Name">What the variant is called.</param>
/// <param name="DirectionsModifications">What it changes about the directions.</param>
/// <param name="AddedIngredients">Ingredients it adds, in order.</param>
/// <param name="RemovedIngredientIds">Ids of the meal's ingredients it leaves out.</param>
/// <param name="SortOrder">Position in the meal's list of variants.</param>
public record MealVariantView(
    int Id,
    int MealId,
    string Name,
    string? DirectionsModifications,
    IReadOnlyList<MealIngredientView> AddedIngredients,
    IReadOnlyList<int> RemovedIngredientIds,
    int SortOrder);

/// <summary>Fields accepted when creating or updating a variant.</summary>
/// <param name="Name">What the variant is called; required.</param>
/// <param name="DirectionsModifications">What it changes about the directions.</param>
/// <param name="AddedIngredients">The whole list of ingredients it adds.</param>
/// <param name="RemovedIngredientIds">Ids of the meal's ingredients it leaves out.</param>
public record MealVariantRequest(
    string Name,
    string? DirectionsModifications = null,
    IReadOnlyList<MealIngredientInput>? AddedIngredients = null,
    IReadOnlyCollection<int>? RemovedIngredientIds = null);

/// <summary>A meal as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the meal is called.</param>
/// <param name="Directions">How to cook it, held as markdown.</param>
/// <param name="Status">Where the meal stands.</param>
/// <param name="IsOneTime">Whether it was marked as a one-time cook in the data it came from.</param>
/// <param name="LastCookedAt">Day it was last cooked, or null.</param>
/// <param name="PlannedSlot">Which meal of the day it is planned for, or null.</param>
/// <param name="PlannedVariantId">The variant that was planned, or null for the meal itself.</param>
/// <param name="PlannedVariantName">Name of that variant, or null.</param>
/// <param name="ImportedText">The imported description, untouched, or null.</param>
/// <param name="NeedsIngredientSplit">Whether its imported description still has to be split.</param>
/// <param name="JiraKey">Key of the JIRA issue it came from, or null.</param>
/// <param name="Ingredients">The ingredient list, in order.</param>
/// <param name="Variants">The meal's variants, in order.</param>
/// <param name="Tags">The tags on the meal.</param>
/// <param name="CreatedAt">When the meal was created.</param>
/// <param name="UpdatedAt">When the meal last changed.</param>
public record MealView(
    int Id,
    string Name,
    string? Directions,
    MealStatus Status,
    bool IsOneTime,
    DateOnly? LastCookedAt,
    MealSlot? PlannedSlot,
    int? PlannedVariantId,
    string? PlannedVariantName,
    string? ImportedText,
    bool NeedsIngredientSplit,
    string? JiraKey,
    IReadOnlyList<MealIngredientView> Ingredients,
    IReadOnlyList<MealVariantView> Variants,
    IReadOnlyList<MealTagView> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Filters for a meal listing. Every member is optional and narrows the result.</summary>
/// <param name="Text">Case-insensitive substring matched against the name, the directions and the ingredients.</param>
/// <param name="Statuses">Keep only meals in one of these states; null or empty keeps every state.</param>
/// <param name="TagIds">Keep only meals carrying every one of these tags.</param>
/// <param name="NeedsIngredientSplit">Keep only meals whose imported description is or is not still to be split.</param>
/// <param name="Limit">Maximum number of meals to return.</param>
/// <param name="Skip">Number of matching meals to pass over before the first one returned.</param>
public record MealFilter(
    string? Text = null,
    IReadOnlyCollection<MealStatus>? Statuses = null,
    IReadOnlyCollection<int>? TagIds = null,
    bool? NeedsIngredientSplit = null,
    int? Limit = null,
    int Skip = 0);

/// <summary>The orders a meal listing can be read in.</summary>
public enum MealSort
{
    /// <summary>Longest since it was cooked first, with meals never cooked before the rest.</summary>
    LastCooked,

    /// <summary>Most recently cooked first, with meals never cooked last.</summary>
    LastCookedDescending,

    /// <summary>Name from A to Z.</summary>
    Name,

    /// <summary>Name from Z to A.</summary>
    NameDescending,
}

/// <summary>One line of an ingredient list as it is written.</summary>
/// <param name="Item">What the ingredient is; required.</param>
/// <param name="Quantity">How much is needed, as written, or null.</param>
public record MealIngredientInput(string Item, string? Quantity = null);

/// <summary>Fields accepted when creating a meal.</summary>
/// <param name="Name">What the meal is called; required.</param>
/// <param name="Directions">How to cook it, held as markdown.</param>
/// <param name="Status">State to start in; defaults to <see cref="MealStatus.Pooled"/>.</param>
/// <param name="IsOneTime">Whether it is a one-time cook.</param>
/// <param name="LastCookedAt">Day it was last cooked, or null.</param>
/// <param name="Ingredients">The ingredient list, in the order given.</param>
/// <param name="TagIds">Tags to put on it.</param>
public record CreateMealRequest(
    string Name,
    string? Directions = null,
    MealStatus Status = MealStatus.Pooled,
    bool IsOneTime = false,
    DateOnly? LastCookedAt = null,
    IReadOnlyList<MealIngredientInput>? Ingredients = null,
    IReadOnlyCollection<int>? TagIds = null);

/// <summary>Fields accepted when updating a meal. A null member leaves that field alone.</summary>
/// <param name="Name">New name.</param>
/// <param name="Directions">New directions; pass an empty string to clear them.</param>
/// <param name="Status">New state.</param>
/// <param name="IsOneTime">Whether it is a one-time cook.</param>
/// <param name="LastCookedAt">New last cooked date, which overrides the date a status change would stamp.</param>
/// <param name="ClearLastCookedAt">When true, removes the last cooked date.</param>
/// <param name="Ingredients">The whole ingredient list, replacing the one held.</param>
/// <param name="TagIds">The whole set of tags, replacing the one held.</param>
/// <param name="ImportedText">New imported text; pass an empty string to clear it.</param>
/// <param name="NeedsIngredientSplit">Whether the imported description still has to be split.</param>
public record UpdateMealRequest(
    string? Name = null,
    string? Directions = null,
    MealStatus? Status = null,
    bool? IsOneTime = null,
    DateOnly? LastCookedAt = null,
    bool ClearLastCookedAt = false,
    IReadOnlyList<MealIngredientInput>? Ingredients = null,
    IReadOnlyCollection<int>? TagIds = null,
    string? ImportedText = null,
    bool? NeedsIngredientSplit = null);

/// <summary>One meal the planning screens can put on the plan.</summary>
/// <param name="MealId">The meal.</param>
/// <param name="Name">What the meal is called.</param>
/// <param name="Status">The meal's status, which its variants share.</param>
/// <param name="LastCookedAt">Day the meal was last cooked, which its variants share.</param>
/// <param name="Tags">The meal's tags.</param>
/// <param name="HasVariants">Whether the meal has variants to choose between.</param>
public record MealPlanOption(
    int MealId,
    string Name,
    MealStatus Status,
    DateOnly? LastCookedAt,
    IReadOnlyList<MealTagView> Tags,
    bool HasVariants);

/// <summary>How many meals of each kind a planning session is aiming for, and how many are planned.</summary>
/// <param name="Slot">The meal of the day.</param>
/// <param name="Target">How many are wanted.</param>
/// <param name="Planned">How many are on the plan.</param>
public record MealPlanSlotCount(MealSlot Slot, int Target, int Planned)
{
    /// <summary>How many are still to be chosen, never below zero.</summary>
    public int Remaining => Math.Max(0, Target - Planned);
}

/// <summary>Fields accepted when creating or updating a tag.</summary>
/// <param name="Name">Display name; required.</param>
/// <param name="Facet">Which side of a meal the tag describes.</param>
public record MealTagRequest(string Name, MealTagFacet Facet = MealTagFacet.Trait);
