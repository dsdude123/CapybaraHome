using System.Text.Json;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Jira;

/// <summary>What one run of the JIRA import did.</summary>
/// <param name="CategoriesAdded">Categories created from epics.</param>
/// <param name="TasksAdded">Tasks created.</param>
/// <param name="TasksUpdated">Tasks matched on their JIRA key and overwritten.</param>
/// <param name="Skipped">Issues that carried no key or no title and were left out.</param>
/// <param name="Warnings">Anything worth reading after the run, such as statuses with no mapping.</param>
public record JiraImportReport(
    int CategoriesAdded,
    int TasksAdded,
    int TasksUpdated,
    int Skipped,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Loads issues read from a JIRA project into tasks and categories. The import is
/// a straight copy: one issue becomes one task, titles verbatim, nothing deduplicated or grouped.
/// Re-running it upserts on the JIRA key, so a fixed snapshot can be loaded again.
/// </summary>
public class JiraImportService(ICapybaraHomeDbContext context)
{
    /// <summary>Reads a snapshot and loads it.</summary>
    /// <param name="json">The snapshot: a JIRA search response, an array of responses, or an array of issues.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<JiraImportReport> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(json);
        return await ImportAsync(JiraFields.Issues(document.RootElement).ToList(), cancellationToken);
    }

    /// <summary>Loads issues already read from JIRA, in the shape the REST search returns them.</summary>
    /// <param name="issues">The issues, epics among them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<JiraImportReport> ImportAsync(IReadOnlyList<JsonElement> issues, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var epics = issues.Where(i => JiraFields.Named(i, "issuetype") == "Epic").ToList();
        var categoryIdByEpicKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var categoriesAdded = 0;

        var existingCategories = await context.Categories.ToListAsync(cancellationToken);
        var categoryByName = existingCategories.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var epic in epics)
        {
            var key = JiraFields.Key(epic);
            var name = JiraFields.Text(epic, "summary")?.Trim();
            if (key is null || string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!categoryByName.TryGetValue(name, out var category))
            {
                category = new Category { Name = name, SortOrder = categoryByName.Count };
                context.Categories.Add(category);
                categoryByName[name] = category;
                categoriesAdded++;
                await context.SaveChangesAsync(cancellationToken);
            }

            categoryIdByEpicKey[key] = category.Id;
        }

        var existingTasks = await context.Tasks
            .Where(t => t.JiraKey != null)
            .ToDictionaryAsync(t => t.JiraKey!, t => t, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var unmappedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in issues)
        {
            if (JiraFields.Named(issue, "issuetype") == "Epic")
            {
                continue;
            }

            var key = JiraFields.Key(issue);
            var title = JiraFields.Text(issue, "summary")?.Trim();
            if (key is null || string.IsNullOrEmpty(title))
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

            var notes = Notes(issue);
            var categoryId = CategoryId(issue, categoryIdByEpicKey);

            if (existingTasks.TryGetValue(key, out var task))
            {
                updated++;
            }
            else
            {
                task = new TaskItem { Title = title, JiraKey = key };
                context.Tasks.Add(task);
                existingTasks[key] = task;
                added++;
            }

            task.Title = title;
            task.Notes = notes;
            task.CategoryId = categoryId;
            task.Status = status ?? TaskItemStatus.ToDo;
            task.DueDate = JiraFields.Date(issue, "duedate");
            task.CompletedAt = JiraFields.Timestamp(issue, "resolutiondate");
            task.CreatedAt = JiraFields.Timestamp(issue, "created") ?? task.CreatedAt;
            task.UpdatedAt = JiraFields.Timestamp(issue, "updated") ?? task.UpdatedAt;
        }

        await context.SaveChangesAsync(cancellationToken);

        if (unmappedStatuses.Count > 0)
        {
            warnings.Add($"Imported as To Do because the status has no mapping: {string.Join(", ", unmappedStatuses.Order())}.");
        }

        if (skipped > 0)
        {
            warnings.Add($"{skipped} issue(s) had no key or no summary and were left out.");
        }

        return new JiraImportReport(categoriesAdded, added, updated, skipped, warnings);
    }

    private static string? Notes(JsonElement issue)
    {
        var markdown = JiraFields.Description(issue);
        var parent = Parent(issue);

        // A JIRA subtask has a parent that is itself a task. The app has no parent link, so the
        // parent's key goes at the top of the notes rather than the relationship being dropped.
        if (parent is { IsEpic: false, Key: { Length: > 0 } parentKey })
        {
            var reference = $"Part of {parentKey}";
            return markdown is { Length: > 0 } ? $"{reference}\n\n{markdown}" : reference;
        }

        return markdown;
    }

    private static int? CategoryId(JsonElement issue, Dictionary<string, int> categoryIdByEpicKey)
    {
        var parent = Parent(issue);
        if (parent is null || parent.Key is null)
        {
            return null;
        }

        return categoryIdByEpicKey.TryGetValue(parent.Key, out var id) ? id : null;
    }

    private static ParentRef? Parent(JsonElement issue)
    {
        var fields = JiraFields.Fields(issue);
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("parent", out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var key = parent.TryGetProperty("key", out var keyNode) ? keyNode.GetString() : null;
        var isEpic = parent.TryGetProperty("fields", out var parentFields)
            && parentFields.TryGetProperty("issuetype", out var type)
            && type.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "Epic", StringComparison.OrdinalIgnoreCase);

        return new ParentRef(key, isEpic);
    }

    private static TaskItemStatus? MapStatus(string? name) =>
        (name ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant() switch
        {
            "todo" => TaskItemStatus.ToDo,
            "inprogress" => TaskItemStatus.InProgress,
            "onhold" => TaskItemStatus.OnHold,
            "done" => TaskItemStatus.Done,
            _ => null,
        };

    private record ParentRef(string? Key, bool IsEpic);
}
