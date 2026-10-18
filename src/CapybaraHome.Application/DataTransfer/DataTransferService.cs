using System.Text.Json;
using System.Text.Json.Serialization;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.DataTransfer;

/// <summary>How an import treats the data already in the database.</summary>
public enum ImportMode
{
    /// <summary>Add and update by JIRA key and category name, leaving anything else in place.</summary>
    Merge,

    /// <summary>Delete every task, category and card first, then load the document.</summary>
    Replace,
}

/// <summary>What an import changed.</summary>
/// <param name="CategoriesAdded">Categories created.</param>
/// <param name="TasksAdded">Tasks created.</param>
/// <param name="TasksUpdated">Tasks matched by JIRA key and overwritten.</param>
/// <param name="CardsAdded">Dashboard cards created.</param>
public record ImportSummary(int CategoriesAdded, int TasksAdded, int TasksUpdated, int CardsAdded);

/// <summary>Exports the whole database to one JSON document and reads it back.</summary>
public class DataTransferService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>JSON settings the export and the import both use, so a round trip is exact.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads the whole database into a document.</summary>
    public async Task<BackupDocument> ExportAsync(CancellationToken cancellationToken = default)
    {
        var categories = await context.Categories.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CategoryRecord(c.Id, c.Name, c.ColorHex, c.SortOrder))
            .ToListAsync(cancellationToken);

        var tasks = await context.Tasks.AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new TaskRecord(
                t.Id, t.Title, t.Notes, t.CategoryId, t.Status, t.DueDate,
                t.CompletedAt, t.JiraKey, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(cancellationToken);

        var cards = await context.DashboardCards.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new DashboardCardRecord(c.Title, c.Kind, c.CategoryId, c.Status, c.DueWithinDays, c.SortOrder))
            .ToListAsync(cancellationToken);

        return new BackupDocument(BackupDocument.CurrentFormatVersion, clock.GetUtcNow(), categories, tasks, cards);
    }

    /// <summary>Serializes an export to JSON.</summary>
    public async Task<string> ExportToJsonAsync(CancellationToken cancellationToken = default)
    {
        var document = await ExportAsync(cancellationToken);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <summary>Reads a JSON export back in.</summary>
    /// <exception cref="InvalidOperationException">The JSON is not a backup document this build understands.</exception>
    public async Task<ImportSummary> ImportFromJsonAsync(string json, ImportMode mode, CancellationToken cancellationToken = default)
    {
        var document = JsonSerializer.Deserialize<BackupDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("The file holds no backup document.");

        if (document.FormatVersion != BackupDocument.CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"The file is format version {document.FormatVersion}; this build reads version {BackupDocument.CurrentFormatVersion}.");
        }

        return await ImportAsync(document, mode, cancellationToken);
    }

    /// <summary>
    /// Loads <paramref name="document"/>. Categories are matched by name and tasks by JIRA key,
    /// so a merge of an export taken from this installation updates rather than duplicates;
    /// tasks with no JIRA key are always added, since nothing else identifies them.
    /// </summary>
    public async Task<ImportSummary> ImportAsync(BackupDocument document, ImportMode mode, CancellationToken cancellationToken = default)
    {
        if (mode == ImportMode.Replace)
        {
            context.DashboardCards.RemoveRange(await context.DashboardCards.ToListAsync(cancellationToken));
            context.Tasks.RemoveRange(await context.Tasks.ToListAsync(cancellationToken));
            context.Categories.RemoveRange(await context.Categories.ToListAsync(cancellationToken));
            await context.SaveChangesAsync(cancellationToken);
        }

        var categoriesAdded = 0;
        var categoryIdByExportedId = new Dictionary<int, int>();
        var existingByName = await context.Categories
            .ToDictionaryAsync(c => c.Name, c => c, cancellationToken);

        foreach (var record in document.Categories)
        {
            if (existingByName.TryGetValue(record.Name, out var match))
            {
                match.ColorHex = record.ColorHex;
                match.SortOrder = record.SortOrder;
                categoryIdByExportedId[record.Id] = match.Id;
                continue;
            }

            var created = new Category { Name = record.Name, ColorHex = record.ColorHex, SortOrder = record.SortOrder };
            context.Categories.Add(created);
            existingByName[record.Name] = created;
            categoriesAdded++;
            await context.SaveChangesAsync(cancellationToken);
            categoryIdByExportedId[record.Id] = created.Id;
        }

        var tasksAdded = 0;
        var tasksUpdated = 0;
        var existingByJiraKey = await context.Tasks
            .Where(t => t.JiraKey != null)
            .ToDictionaryAsync(t => t.JiraKey!, t => t, cancellationToken);

        foreach (var record in document.Tasks)
        {
            var categoryId = record.CategoryId is { } exportedId && categoryIdByExportedId.TryGetValue(exportedId, out var mapped)
                ? mapped
                : (int?)null;

            if (record.JiraKey is { Length: > 0 } key && existingByJiraKey.TryGetValue(key, out var existing))
            {
                existing.Title = record.Title;
                existing.Notes = record.Notes;
                existing.CategoryId = categoryId;
                existing.Status = record.Status;
                existing.DueDate = record.DueDate;
                existing.CompletedAt = record.CompletedAt;
                tasksUpdated++;
                continue;
            }

            var task = new TaskItem
            {
                Title = record.Title,
                Notes = record.Notes,
                CategoryId = categoryId,
                Status = record.Status,
                DueDate = record.DueDate,
                CompletedAt = record.CompletedAt,
                JiraKey = record.JiraKey,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
            };

            context.Tasks.Add(task);
            if (record.JiraKey is { Length: > 0 } newKey)
            {
                existingByJiraKey[newKey] = task;
            }

            tasksAdded++;
        }

        var cardsAdded = 0;
        var haveCards = await context.DashboardCards.AnyAsync(cancellationToken);
        if (!haveCards)
        {
            foreach (var record in document.DashboardCards)
            {
                var categoryId = record.CategoryId is { } exportedId && categoryIdByExportedId.TryGetValue(exportedId, out var mapped)
                    ? mapped
                    : (int?)null;

                context.DashboardCards.Add(new DashboardCard
                {
                    Title = record.Title,
                    Kind = record.Kind,
                    CategoryId = categoryId,
                    Status = record.Status,
                    DueWithinDays = record.DueWithinDays,
                    SortOrder = record.SortOrder,
                });

                cardsAdded++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return new ImportSummary(categoriesAdded, tasksAdded, tasksUpdated, cardsAdded);
    }
}
