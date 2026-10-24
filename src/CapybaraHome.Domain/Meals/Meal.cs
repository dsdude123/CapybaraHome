namespace CapybaraHome.Domain.Meals;

/// <summary>A meal that can be cooked, with its ingredients kept apart from its directions.</summary>
public class Meal
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What the meal is called.</summary>
    public required string Name { get; set; }

    /// <summary>How to cook it, held as markdown.</summary>
    public string? Directions { get; set; }

    /// <summary>Where the meal stands.</summary>
    public MealStatus Status { get; set; } = MealStatus.Pooled;

    /// <summary>Whether the meal was marked as a one-time cook in the data it came from.</summary>
    public bool IsOneTime { get; set; }

    /// <summary>Day the meal was last cooked, stamped when it is sent back to available or pooled.</summary>
    public DateOnly? LastCookedAt { get; set; }

    /// <summary>Which meal of the day this is planned for, or null when it is not planned.</summary>
    public MealSlot? PlannedSlot { get; set; }

    /// <summary>The variant that was planned, or null when the meal itself was.</summary>
    public int? PlannedVariantId { get; set; }

    /// <summary>Navigation to <see cref="PlannedVariantId"/>.</summary>
    public MealVariant? PlannedVariant { get; set; }

    /// <summary>The imported description, kept untouched so a split of it can always be started over.</summary>
    public string? ImportedText { get; set; }

    /// <summary>Whether the imported description still has to be split into ingredients and directions.</summary>
    public bool NeedsIngredientSplit { get; set; }

    /// <summary>Key of the JIRA issue this meal was imported from, used to keep imports re-runnable.</summary>
    public string? JiraKey { get; set; }

    /// <summary>The ingredient list.</summary>
    public ICollection<MealIngredient> Ingredients { get; set; } = [];

    /// <summary>The meal's variants.</summary>
    public ICollection<MealVariant> Variants { get; set; } = [];

    /// <summary>The tags on the meal.</summary>
    public ICollection<MealTag> Tags { get; set; } = [];

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
