using System.Globalization;
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
/// Loads a read-only snapshot of the JIRA CAPY project into tasks and categories. The import is
/// a straight copy: one issue becomes one task, titles verbatim, nothing deduplicated or grouped.
/// Re-running it upserts on the JIRA key, so a fixed snapshot can be loaded again.
/// </summary>
public class JiraImportService(ICapybaraHomeDbContext context)
{
    /// <summary>Reads a snapshot file and loads it.</summary>
    /// <param name="json">The snapshot: a JIRA search response, an array of responses, or an array of issues.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<JiraImportReport> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(json);
        var issues = Issues(document.RootElement).ToList();

        var warnings = new List<string>();
        var epics = issues.Where(i => TypeName(i) == "Epic").ToList();
        var categoryIdByEpicKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var categoriesAdded = 0;

        var existingCategories = await context.Categories.ToListAsync(cancellationToken);
        var categoryByName = existingCategories.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var epic in epics)
        {
            var key = Key(epic);
            var name = Text(epic, "summary")?.Trim();
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
            if (TypeName(issue) == "Epic")
            {
                continue;
            }

            var key = Key(issue);
            var title = Text(issue, "summary")?.Trim();
            if (key is null || string.IsNullOrEmpty(title))
            {
                skipped++;
                continue;
            }

            var statusName = Status(issue);
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
            task.DueDate = Date(issue, "duedate");
            task.CompletedAt = Timestamp(issue, "resolutiondate");
            task.CreatedAt = Timestamp(issue, "created") ?? task.CreatedAt;
            task.UpdatedAt = Timestamp(issue, "updated") ?? task.UpdatedAt;
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

    private static IEnumerable<JsonElement> Issues(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    yield return issue;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var element in root.EnumerateArray())
        {
            if (element.TryGetProperty("issues", out _))
            {
                foreach (var issue in Issues(element))
                {
                    yield return issue;
                }
            }
            else
            {
                yield return element;
            }
        }
    }

    private static JsonElement Fields(JsonElement issue) =>
        issue.TryGetProperty("fields", out var fields) ? fields : default;

    private static string? Key(JsonElement issue) =>
        issue.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;

    private static string? Text(JsonElement issue, string field)
    {
        var fields = Fields(issue);
        return fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty(field, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string? TypeName(JsonElement issue)
    {
        var fields = Fields(issue);
        return fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("issuetype", out var type)
            && type.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
    }

    private static string? Status(JsonElement issue)
    {
        var fields = Fields(issue);
        return fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("status", out var status)
            && status.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
    }

    private static string? Notes(JsonElement issue)
    {
        var fields = Fields(issue);
        var markdown = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("description", out var description)
            ? AdfToMarkdown.Convert(description)
            : null;

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
        var fields = Fields(issue);
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

    private static DateOnly? Date(JsonElement issue, string field)
    {
        var text = Text(issue, field);
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date) ? date : null;
    }

    private static DateTimeOffset? Timestamp(JsonElement issue, string field)
    {
        var text = Text(issue, field);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
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
