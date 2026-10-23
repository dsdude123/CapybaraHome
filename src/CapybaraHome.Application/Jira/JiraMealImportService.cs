using System.Text.Json;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Jira;

/// <summary>What one run of the meal import did.</summary>
/// <param name="MealsAdded">Meals created.</param>
/// <param name="MealsUpdated">Meals matched on their JIRA key and overwritten.</param>
/// <param name="TagsAdded">Tags created from labels.</param>
/// <param name="Skipped">Issues that carried no key or no summary and were left out.</param>
/// <param name="Warnings">Anything worth reading after the run, such as statuses with no mapping.</param>
public record JiraMealImportReport(
    int MealsAdded,
    int MealsUpdated,
    int TagsAdded,
    int Skipped,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Loads issues read from a JIRA project into meals. Like the task import it is a straight copy:
/// one issue becomes one meal, names verbatim, nothing deduplicated. The description is carried
/// across whole, into both the directions and the untouched imported text; splitting it into
/// ingredients is a screen of its own, never something the import guesses at.
/// </summary>
public class JiraMealImportService(ICapybaraHomeDbContext context)
{
    /// <summary>The marker a one-time meal's summary starts with in the JIRA data.</summary>
    private const string OneTimeMarker = "[One-Time]";

    /// <summary>Returns the labels used across <paramref name="issues"/>, in alphabetical order.</summary>
    public static IReadOnlyList<string> Labels(IReadOnlyList<JsonElement> issues)
    {
        return issues
            .SelectMany(JiraFields.Labels)
            .Select(label => label.Trim())
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Loads issues already read from JIRA, in the shape the REST search returns them.</summary>
    /// <param name="issues">The issues to import as meals.</param>
    /// <param name="facets">The facet to file each label under; a label missing from it becomes a trait.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<JiraMealImportReport> ImportAsync(
        IReadOnlyList<JsonElement> issues,
        IReadOnlyDictionary<string, MealTagFacet>? facets = null,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var unmappedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tagsAdded = 0;
        var added = 0;
        var updated = 0;
        var skipped = 0;

        var tagByName = await context.MealTags.ToDictionaryAsync(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var label in Labels(issues))
        {
            if (tagByName.ContainsKey(label))
            {
                continue;
            }

            var tag = new MealTag { Name = label, Facet = Facet(facets, label) };
            context.MealTags.Add(tag);
            tagByName[label] = tag;
            tagsAdded++;
        }

        await context.SaveChangesAsync(cancellationToken);

        var existing = await context.Meals
            .Include(m => m.Tags)
            .Where(m => m.JiraKey != null)
            .ToDictionaryAsync(m => m.JiraKey!, m => m, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var issue in issues)
        {
            var key = JiraFields.Key(issue);
            var summary = JiraFields.Text(issue, "summary")?.Trim();
            if (key is null || string.IsNullOrEmpty(summary))
            {
                skipped++;
                continue;
            }

            var statusName = JiraFields.Named(issue, "status");
            var status = MapStatus(statusName);
            if (status is null && statusName is { Length: > 0 })
            {
                unmappedStatuses.Add(statusName);
            }

            var oneTime = summary.StartsWith(OneTimeMarker, StringComparison.OrdinalIgnoreCase);
            var name = oneTime ? summary[OneTimeMarker.Length..].Trim() : summary;
            if (name.Length == 0)
            {
                skipped++;
                continue;
            }

            if (existing.TryGetValue(key, out var meal))
            {
                updated++;
            }
            else
            {
                meal = new Meal { Name = name, JiraKey = key };
                context.Meals.Add(meal);
                existing[key] = meal;
                added++;
            }

            var description = JiraFields.Description(issue);

            meal.Name = name;
            meal.IsOneTime = oneTime;
            meal.Status = status ?? MealStatus.Pooled;
            meal.Directions = description;
            meal.ImportedText = description;
            meal.NeedsIngredientSplit = description is not null;
            meal.CreatedAt = JiraFields.Timestamp(issue, "created") ?? meal.CreatedAt;
            meal.UpdatedAt = JiraFields.Timestamp(issue, "updated") ?? meal.UpdatedAt;

            // Ordering by when a meal was last cooked is the whole point of the planning screens,
            // and the nearest thing the JIRA data has to that is when the issue last changed.
            meal.LastCookedAt = meal.LastCookedAt
                ?? (JiraFields.Timestamp(issue, "updated") is { } changed ? DateOnly.FromDateTime(changed.UtcDateTime) : null);

            meal.Tags = JiraFields.Labels(issue)
                .Select(label => label.Trim())
                .Where(label => label.Length > 0 && tagByName.ContainsKey(label))
                .Select(label => tagByName[label])
                .Distinct()
                .ToList();
        }

        await context.SaveChangesAsync(cancellationToken);

        if (unmappedStatuses.Count > 0)
        {
            warnings.Add($"Imported as Pooled because the status has no mapping: {string.Join(", ", unmappedStatuses.Order())}.");
        }

        if (skipped > 0)
        {
            warnings.Add($"{skipped} issue(s) had no key or no summary and were left out.");
        }

        return new JiraMealImportReport(added, updated, tagsAdded, skipped, warnings);
    }

    private static MealTagFacet Facet(IReadOnlyDictionary<string, MealTagFacet>? facets, string label) =>
        facets is not null && facets.TryGetValue(label, out var facet) ? facet : MealTagFacet.Trait;

    private static MealStatus? MapStatus(string? name) =>
        (name ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant() switch
        {
            "pooled" => MealStatus.Pooled,
            "available" => MealStatus.Available,
            "queued" => MealStatus.Queued,
            "planned" => MealStatus.Planned,
            "plannedshared" => MealStatus.PlannedShared,
            "closed" => MealStatus.Closed,
            _ => null,
        };
}
