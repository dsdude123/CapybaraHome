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

    /// <summary>Delete every task, automation, category, card and meal first, then load the document.</summary>
    Replace,
}

/// <summary>What an import changed.</summary>
/// <param name="CategoriesAdded">Categories created.</param>
/// <param name="TasksAdded">Tasks created.</param>
/// <param name="TasksUpdated">Tasks matched by JIRA key and overwritten.</param>
/// <param name="CardsAdded">Dashboard cards created.</param>
/// <param name="MealsAdded">Meals created.</param>
/// <param name="MealsUpdated">Meals matched by JIRA key and overwritten.</param>
/// <param name="AutomationsAdded">Task automations created.</param>
/// <param name="AutomationsUpdated">Task automations matched by title and overwritten.</param>
public record ImportSummary(
    int CategoriesAdded,
    int TasksAdded,
    int TasksUpdated,
    int CardsAdded,
    int MealsAdded = 0,
    int MealsUpdated = 0,
    int AutomationsAdded = 0,
    int AutomationsUpdated = 0);

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
                m.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id)
                    .Select(v => new MealVariantRecord(
                        v.Name,
                        v.DirectionsModifications,
                        v.AddedIngredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                            .Select(i => new MealIngredientRecord(i.Quantity, i.Item, i.SortOrder)).ToList(),
                        v.RemovedIngredients.Select(r => r.Ingredient!.SortOrder).ToList(),
                        v.SortOrder)).ToList(),
                m.Tags.OrderBy(t => t.Id).Select(t => t.Id).ToList(),
                m.CreatedAt, m.UpdatedAt))
            .ToListAsync(cancellationToken);

        var automations = await context.TaskAutomations.AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new TaskAutomationRecord(
                a.Title, a.Notes, a.CategoryId, a.Frequency, a.StartsOn, a.TimeOfDay, a.DueInDays,
                a.SkipWhenOpenTaskExists, a.IsEnabled, a.LastRunAt, a.LastRunSkipped,
                a.CreatedAt, a.UpdatedAt))
            .ToListAsync(cancellationToken);

        var cards = await context.DashboardCards.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new DashboardCardRecord(c.Title, c.Kind, c.CategoryId, c.Status, c.DueWithinDays, c.SortOrder))
            .ToListAsync(cancellationToken);

        return new BackupDocument(
            BackupDocument.CurrentFormatVersion, clock.GetUtcNow(), categories, tasks, cards, meals, tags, automations);
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
            context.TaskAutomations.RemoveRange(await context.TaskAutomations.ToListAsync(cancellationToken));
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
        var (automationsAdded, automationsUpdated) = await ImportAutomationsAsync(document, categoryIdByExportedId, cancellationToken);

        return new ImportSummary(
            categoriesAdded, tasksAdded, tasksUpdated, cardsAdded,
            mealsAdded, mealsUpdated, automationsAdded, automationsUpdated);
    }

    /// <summary>
    /// Loads the automations in <paramref name="document"/>. An automation has no imported key, so
    /// a merge matches on the title it gives its tasks, which is the one field that has to be
    /// unique in practice for the skip option to mean anything.
    /// </summary>
    private async Task<(int Added, int Updated)> ImportAutomationsAsync(
        BackupDocument document,
        Dictionary<int, int> categoryIdByExportedId,
        CancellationToken cancellationToken)
    {
        if (document.TaskAutomations is not { Count: > 0 } records)
        {
            return (0, 0);
        }

        var added = 0;
        var updated = 0;
        var existingByTitle = await context.TaskAutomations
            .ToDictionaryAsync(a => a.Title, a => a, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var record in records)
        {
            var categoryId = record.CategoryId is { } exportedId && categoryIdByExportedId.TryGetValue(exportedId, out var mapped)
                ? mapped
                : (int?)null;

            if (existingByTitle.TryGetValue(record.Title, out var existing))
            {
                existing.Notes = record.Notes;
                existing.CategoryId = categoryId;
                existing.Frequency = record.Frequency;
                existing.StartsOn = record.StartsOn;
                existing.TimeOfDay = record.TimeOfDay;
                existing.DueInDays = record.DueInDays;
                existing.SkipWhenOpenTaskExists = record.SkipWhenOpenTaskExists;
                existing.IsEnabled = record.IsEnabled;
                existing.LastRunAt = record.LastRunAt;
                existing.LastRunSkipped = record.LastRunSkipped;
                existing.UpdatedAt = record.UpdatedAt;
                updated++;
                continue;
            }

            var automation = new TaskAutomation
            {
                Title = record.Title,
                Notes = record.Notes,
                CategoryId = categoryId,
                Frequency = record.Frequency,
                StartsOn = record.StartsOn,
                TimeOfDay = record.TimeOfDay,
                DueInDays = record.DueInDays,
                SkipWhenOpenTaskExists = record.SkipWhenOpenTaskExists,
                IsEnabled = record.IsEnabled,
                LastRunAt = record.LastRunAt,
                LastRunSkipped = record.LastRunSkipped,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
            };

            context.TaskAutomations.Add(automation);
            existingByTitle[record.Title] = automation;
            added++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return (added, updated);
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
            .Include(m => m.Variants)
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

            var variants = Variants(record, ingredients);

            if (record.JiraKey is { Length: > 0 } key && existingByJiraKey.TryGetValue(key, out var existing))
            {
                existing.Name = record.Name;
                existing.Directions = record.Directions;
                existing.Status = record.Status;
                existing.IsOneTime = record.IsOneTime;
                existing.LastCookedAt = record.LastCookedAt;
                existing.ImportedText = record.ImportedText;
                existing.NeedsIngredientSplit = record.NeedsIngredientSplit;
                context.MealVariants.RemoveRange(existing.Variants);
                context.MealIngredients.RemoveRange(existing.Ingredients);
                existing.Ingredients = ingredients;
                existing.Variants = variants;
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
                Variants = variants,
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

    /// <summary>
    /// Rebuilds a meal's variants. An export names the ingredients a variant leaves out by their
    /// position in the meal's list rather than by id, because the ids are new on this side; the
    /// rows are linked by reference so that EF fills the keys in once the meal is saved.
    /// </summary>
    private static List<MealVariant> Variants(MealRecord record, List<MealIngredient> ingredients)
    {
        var byPosition = ingredients.ToDictionary(i => i.SortOrder, i => i);

        return (record.Variants ?? [])
            .Select(variant => new MealVariant
            {
                Name = variant.Name,
                DirectionsModifications = variant.DirectionsModifications,
                SortOrder = variant.SortOrder,
                AddedIngredients = variant.AddedIngredients
                    .Select(i => new MealVariantIngredient { Quantity = i.Quantity, Item = i.Item, SortOrder = i.SortOrder })
                    .ToList(),
                RemovedIngredients = variant.RemovedIngredients
                    .Where(byPosition.ContainsKey)
                    .Select(position => new MealVariantRemoval { Ingredient = byPosition[position] })
                    .ToList(),
            })
            .ToList();
    }
}
