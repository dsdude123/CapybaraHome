using System.Text.Json;
using System.Text.Json.Serialization;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.DataTransfer;

/// <summary>How an import treats the data already in the database.</summary>
public enum ImportMode
{
    /// <summary>Add and update by JIRA key, category name and tag name, leaving anything else in place.</summary>
    Merge,

    /// <summary>Delete every task, category, card and meal first, then load the document.</summary>
    Replace,
}

/// <summary>What an import changed.</summary>
/// <param name="CategoriesAdded">Categories created.</param>
/// <param name="TasksAdded">Tasks created.</param>
/// <param name="TasksUpdated">Tasks matched by JIRA key and overwritten.</param>
/// <param name="CardsAdded">Dashboard cards created.</param>
/// <param name="MealsAdded">Meals created.</param>
/// <param name="MealsUpdated">Meals matched by JIRA key and overwritten.</param>
public record ImportSummary(
    int CategoriesAdded,
    int TasksAdded,
    int TasksUpdated,
    int CardsAdded,
    int MealsAdded = 0,
    int MealsUpdated = 0);

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

        var tags = await context.MealTags.AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new MealTagRecord(t.Id, t.Name, t.Facet))
            .ToListAsync(cancellationToken);

        var meals = await context.Meals.AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new MealRecord(
                m.Id, m.Name, m.Directions, m.Status, m.IsOneTime, m.LastCookedAt,
                m.ImportedText, m.NeedsIngredientSplit, m.JiraKey,
                m.Ingredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => new MealIngredientRecord(i.Quantity, i.Item, i.SortOrder)).ToList(),
                m.Tags.OrderBy(t => t.Id).Select(t => t.Id).ToList(),
                m.CreatedAt, m.UpdatedAt))
            .ToListAsync(cancellationToken);

        var cards = await context.DashboardCards.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new DashboardCardRecord(c.Title, c.Kind, c.CategoryId, c.Status, c.DueWithinDays, c.SortOrder))
            .ToListAsync(cancellationToken);

        return new BackupDocument(BackupDocument.CurrentFormatVersion, clock.GetUtcNow(), categories, tasks, cards, meals, tags);
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

        if (document.FormatVersion is < BackupDocument.OldestReadableFormatVersion or > BackupDocument.CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"The file is format version {document.FormatVersion}; this build reads versions "
                + $"{BackupDocument.OldestReadableFormatVersion} to {BackupDocument.CurrentFormatVersion}.");
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
            context.Meals.RemoveRange(await context.Meals.Include(m => m.Tags).ToListAsync(cancellationToken));
            context.MealTags.RemoveRange(await context.MealTags.ToListAsync(cancellationToken));
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

        var (mealsAdded, mealsUpdated) = await ImportMealsAsync(document, cancellationToken);
        return new ImportSummary(categoriesAdded, tasksAdded, tasksUpdated, cardsAdded, mealsAdded, mealsUpdated);
    }

    /// <summary>
    /// Loads the meals in <paramref name="document"/>. Tags are matched by name and meals by JIRA
    /// key, so a merge of an export taken from this installation updates rather than duplicates;
    /// a meal with no JIRA key is always added, since nothing else identifies it.
    /// </summary>
    private async Task<(int Added, int Updated)> ImportMealsAsync(BackupDocument document, CancellationToken cancellationToken)
    {
        if (document.Meals is not { Count: > 0 } && document.MealTags is not { Count: > 0 })
        {
            return (0, 0);
        }

        var tagByExportedId = new Dictionary<int, MealTag>();
        var tagByName = await context.MealTags.ToDictionaryAsync(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var record in document.MealTags ?? [])
        {
            if (!tagByName.TryGetValue(record.Name, out var tag))
            {
                tag = new MealTag { Name = record.Name, Facet = record.Facet };
                context.MealTags.Add(tag);
                tagByName[record.Name] = tag;
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                tag.Facet = record.Facet;
            }

            tagByExportedId[record.Id] = tag;
        }

        var added = 0;
        var updated = 0;
        var existingByJiraKey = await context.Meals
            .Include(m => m.Ingredients)
            .Include(m => m.Tags)
            .Where(m => m.JiraKey != null)
            .ToDictionaryAsync(m => m.JiraKey!, m => m, cancellationToken);

        foreach (var record in document.Meals ?? [])
        {
            var tags = record.TagIds
                .Where(tagByExportedId.ContainsKey)
                .Select(id => tagByExportedId[id])
                .ToList();

            var ingredients = record.Ingredients
                .Select(i => new MealIngredient { Quantity = i.Quantity, Item = i.Item, SortOrder = i.SortOrder })
                .ToList();

            if (record.JiraKey is { Length: > 0 } key && existingByJiraKey.TryGetValue(key, out var existing))
            {
                existing.Name = record.Name;
                existing.Directions = record.Directions;
                existing.Status = record.Status;
                existing.IsOneTime = record.IsOneTime;
                existing.LastCookedAt = record.LastCookedAt;
                existing.ImportedText = record.ImportedText;
                existing.NeedsIngredientSplit = record.NeedsIngredientSplit;
                context.MealIngredients.RemoveRange(existing.Ingredients);
                existing.Ingredients = ingredients;
                existing.Tags = tags;
                updated++;
                continue;
            }

            var meal = new Meal
            {
                Name = record.Name,
                Directions = record.Directions,
                Status = record.Status,
                IsOneTime = record.IsOneTime,
                LastCookedAt = record.LastCookedAt,
                ImportedText = record.ImportedText,
                NeedsIngredientSplit = record.NeedsIngredientSplit,
                JiraKey = record.JiraKey,
                Ingredients = ingredients,
                Tags = tags,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
            };

            context.Meals.Add(meal);
            if (record.JiraKey is { Length: > 0 } newKey)
            {
                existingByJiraKey[newKey] = meal;
            }

            added++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return (added, updated);
    }
}
