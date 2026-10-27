using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.DataTransfer;

/// <summary>Everything an export carries, and everything an import reads.</summary>
/// <param name="FormatVersion">Schema version of the document, so a later import can tell what it is reading.</param>
/// <param name="ExportedAt">When the export was taken, in UTC.</param>
/// <param name="Categories">Every category.</param>
/// <param name="Tasks">Every task.</param>
/// <param name="DashboardCards">Every dashboard card.</param>
/// <param name="Meals">Every meal. Absent from a version 1 document, which carried no meals.</param>
/// <param name="MealTags">Every meal tag. Absent from a version 1 document.</param>
/// <param name="TaskAutomations">Every task automation. Absent from a document before version 3.</param>
public record BackupDocument(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<CategoryRecord> Categories,
    IReadOnlyList<TaskRecord> Tasks,
    IReadOnlyList<DashboardCardRecord> DashboardCards,
    IReadOnlyList<MealRecord>? Meals = null,
    IReadOnlyList<MealTagRecord>? MealTags = null,
    IReadOnlyList<TaskAutomationRecord>? TaskAutomations = null)
{
    /// <summary>The version this build writes.</summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>The oldest version this build still reads, from before meals existed.</summary>
    public const int OldestReadableFormatVersion = 1;
}

/// <summary>A meal tag in an export. Ids are carried so meals can point at them.</summary>
/// <param name="Id">Primary key as exported.</param>
/// <param name="Name">Display name.</param>
/// <param name="Facet">Which side of a meal the tag describes.</param>
public record MealTagRecord(int Id, string Name, MealTagFacet Facet);

/// <summary>One line of a meal's ingredient list in an export.</summary>
/// <param name="Quantity">How much is needed, as written, or null.</param>
/// <param name="Item">What the ingredient is.</param>
/// <param name="SortOrder">Position in the list.</param>
public record MealIngredientRecord(string? Quantity, string Item, int SortOrder);

/// <summary>A meal in an export.</summary>
/// <param name="Id">Primary key as exported.</param>
/// <param name="Name">What the meal is called.</param>
/// <param name="Directions">How to cook it, held as markdown.</param>
/// <param name="Status">Where the meal stood.</param>
/// <param name="IsOneTime">Whether it was a one-time cook.</param>
/// <param name="LastCookedAt">Day it was last cooked, or null.</param>
/// <param name="ImportedText">The imported description, untouched, or null.</param>
/// <param name="NeedsIngredientSplit">Whether that description still has to be split.</param>
/// <param name="JiraKey">Key of the JIRA issue it came from, or null.</param>
/// <param name="Ingredients">The ingredient list, in order.</param>
/// <param name="Variants">The meal's variants, in order.</param>
/// <param name="TagIds">Ids of the tags on it.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record MealRecord(
    int Id,
    string Name,
    string? Directions,
    MealStatus Status,
    bool IsOneTime,
    DateOnly? LastCookedAt,
    string? ImportedText,
    bool NeedsIngredientSplit,
    string? JiraKey,
    IReadOnlyList<MealIngredientRecord> Ingredients,
    IReadOnlyList<MealVariantRecord> Variants,
    IReadOnlyList<int> TagIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A meal's variant in an export.</summary>
/// <param name="Name">What the variant is called.</param>
/// <param name="DirectionsModifications">What it changes about the directions.</param>
/// <param name="AddedIngredients">Ingredients it adds, in order.</param>
/// <param name="RemovedIngredients">The meal's ingredients it leaves out, by their position in that list.</param>
/// <param name="SortOrder">Position in the meal's list of variants.</param>
public record MealVariantRecord(
    string Name,
    string? DirectionsModifications,
    IReadOnlyList<MealIngredientRecord> AddedIngredients,
    IReadOnlyList<int> RemovedIngredients,
    int SortOrder);

/// <summary>A category in an export. Ids are carried so tasks and cards can point at them.</summary>
/// <param name="Id">Primary key as exported.</param>
/// <param name="Name">Display name.</param>
/// <param name="ColorHex">Hex colour, or null.</param>
/// <param name="SortOrder">Position in hand-ordered lists.</param>
public record CategoryRecord(int Id, string Name, string? ColorHex, int SortOrder);

/// <summary>A task in an export.</summary>
/// <param name="Id">Primary key as exported.</param>
/// <param name="Title">Short description of the work.</param>
/// <param name="Notes">Free-form detail, held as markdown.</param>
/// <param name="CategoryId">Category it was filed under, or null.</param>
/// <param name="Status">State it was in.</param>
/// <param name="DueDate">Day it is due, or null.</param>
/// <param name="CompletedAt">When it was completed, or null.</param>
/// <param name="JiraKey">Key of the JIRA issue it came from, or null.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record TaskRecord(
    int Id,
    string Title,
    string? Notes,
    int? CategoryId,
    TaskItemStatus Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    string? JiraKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A task automation in an export.</summary>
/// <param name="Title">Title it gives the tasks it creates.</param>
/// <param name="Notes">Notes it copies onto them.</param>
/// <param name="CategoryId">Category they were filed under, or null.</param>
/// <param name="IntervalUnit">Unit the repeat interval is counted in.</param>
/// <param name="IntervalCount">How many of that unit there are between runs.</param>
/// <param name="StartsOn">Day the interval is counted from.</param>
/// <param name="TimeOfDay">Time of day each run happens at.</param>
/// <param name="DueInDays">How many days after it is created the task is due, or null.</param>
/// <param name="SkipWhenOpenTaskExists">Whether a run does nothing when a task with the same title is already open.</param>
/// <param name="IsEnabled">Whether it runs at all.</param>
/// <param name="LastRunAt">When it last ran, or null.</param>
/// <param name="LastRunSkipped">Whether that run skipped its task.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record TaskAutomationRecord(
    string Title,
    string? Notes,
    int? CategoryId,
    TaskIntervalUnit IntervalUnit,
    int IntervalCount,
    DateOnly StartsOn,
    TimeOnly TimeOfDay,
    int? DueInDays,
    bool SkipWhenOpenTaskExists,
    bool IsEnabled,
    DateTimeOffset? LastRunAt,
    bool LastRunSkipped,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A dashboard card in an export.</summary>
/// <param name="Title">Heading shown on the card.</param>
/// <param name="Kind">What the card shows.</param>
/// <param name="CategoryId">Category it reports on, or null.</param>
/// <param name="Status">Status it filters on, or null.</param>
/// <param name="DueWithinDays">Days-away bound it filters on, or null.</param>
/// <param name="SortOrder">Position on the dashboard.</param>
public record DashboardCardRecord(
    string Title,
    DashboardCardKind Kind,
    int? CategoryId,
    TaskItemStatus? Status,
    int? DueWithinDays,
    int SortOrder);
