using System.Linq.Expressions;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Grocery;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Grocery;

/// <summary>Reads and writes the shopping lists and the frequent items. The only place grocery rules live.</summary>
public class GroceryService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>What the generator calls the group of frequent items.</summary>
    public const string FrequentGroupName = "Frequent items";

    /// <summary>Returns the open list, or null when none has been started.</summary>
    public async Task<GroceryListView?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        return await context.GroceryLists.AsNoTracking()
            .Where(l => l.ClosedAt == null)
            .OrderByDescending(l => l.Id)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Returns the open list, starting one when there is none.</summary>
    public async Task<GroceryListView> EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        return await GetCurrentAsync(cancellationToken) ?? await OpenAsync(cancellationToken);
    }

    /// <summary>Closes the open list and starts an empty one. Nothing but this starts a fresh list.</summary>
    public async Task<GroceryListView> StartFreshAsync(CancellationToken cancellationToken = default)
    {
        var open = await context.GroceryLists.Where(l => l.ClosedAt == null).ToListAsync(cancellationToken);

        foreach (var list in open)
        {
            list.ClosedAt = clock.GetUtcNow();
        }

        // Saved before the new list is added, so the database never holds two open lists even
        // for the length of one transaction, which is what its unique index refuses.
        await context.SaveChangesAsync(cancellationToken);

        return await OpenAsync(cancellationToken);
    }

    /// <summary>Returns the lists, newest first, with the closed ones left out unless asked for.</summary>
    public async Task<IReadOnlyList<GroceryListView>> ListAsync(bool includeClosed = false, CancellationToken cancellationToken = default)
    {
        var query = context.GroceryLists.AsNoTracking();

        if (!includeClosed)
        {
            query = query.Where(l => l.ClosedAt == null);
        }

        return await query.OrderByDescending(l => l.Id).Select(Projection).ToListAsync(cancellationToken);
    }

    /// <summary>Returns one list, or null when no list has that id.</summary>
    public async Task<GroceryListView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.GroceryLists.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Puts one item on the open list, starting a list when there is none.</summary>
    /// <exception cref="ArgumentException">The item is blank.</exception>
    public async Task<GroceryItemView> AddItemAsync(GroceryItemInput input, CancellationToken cancellationToken = default)
    {
        var item = (input.Item ?? string.Empty).Trim();
        if (item.Length == 0)
        {
            throw new ArgumentException("An item needs a name.", nameof(input));
        }

        var list = await EnsureCurrentAsync(cancellationToken);

        var row = new GroceryItem
        {
            GroceryListId = list.Id,
            Item = item,
            Quantity = Blank(input.Quantity),
            Source = Blank(input.Source),
            SortOrder = list.Items.Count == 0 ? 0 : list.Items.Max(i => i.SortOrder) + 1,
        };

        context.GroceryItems.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        return new GroceryItemView(row.Id, row.Quantity, row.Item, row.Source, row.SortOrder);
    }

    /// <summary>
    /// Puts the chosen items on the open list and returns how many went on. An item whose name is
    /// on the list already is passed over, so answering the prompts twice cannot double the list.
    /// </summary>
    public async Task<int> AddItemsAsync(IReadOnlyList<GroceryItemInput> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return 0;
        }

        var list = await EnsureCurrentAsync(cancellationToken);
        var held = list.Items.Select(i => i.Item).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = list.Items.Count == 0 ? 0 : list.Items.Max(i => i.SortOrder) + 1;
        var added = 0;

        foreach (var input in inputs)
        {
            var item = (input.Item ?? string.Empty).Trim();
            if (item.Length == 0 || !held.Add(item))
            {
                continue;
            }

            context.GroceryItems.Add(new GroceryItem
            {
                GroceryListId = list.Id,
                Item = item,
                Quantity = Blank(input.Quantity),
                Source = Blank(input.Source),
                SortOrder = next++,
            });

            added++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>Takes an item off its list. Returns false when no item has that id.</summary>
    public async Task<bool> RemoveItemAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await context.GroceryItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        context.GroceryItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Returns what to ask about: the frequent items, then the ingredients of every planned meal,
    /// each marked with whether the open list already holds an item of that name.
    /// </summary>
    public async Task<GroceryCandidates> CandidatesAsync(CancellationToken cancellationToken = default)
    {
        var current = await GetCurrentAsync(cancellationToken);
        var held = (current?.Items ?? []).Select(i => i.Item).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groups = new List<GroceryCandidateGroup>();

        var frequent = await ListFrequentAsync(cancellationToken);
        if (frequent.Count > 0)
        {
            groups.Add(new GroceryCandidateGroup(
                FrequentGroupName,
                frequent.Select(f => new GroceryCandidate(f.Item, f.Quantity, null, held.Contains(f.Item))).ToList()));
        }

        var planned = await context.Meals.AsNoTracking()
            .Where(m => m.Status == MealStatus.Planned || m.Status == MealStatus.PlannedShared)
            .Select(m => new PlannedMeal(
                m.Name,
                m.PlannedSlot,
                m.PlannedVariantId,
                m.PlannedVariant == null ? null : m.PlannedVariant.Name,
                m.Ingredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => new PlannedLine(i.Id, i.Quantity, i.Item)).ToList(),
                m.Variants.Where(v => v.Id == m.PlannedVariantId)
                    .SelectMany(v => v.AddedIngredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
                    .Select(i => new PlannedLine(i.Id, i.Quantity, i.Item)).ToList(),
                m.Variants.Where(v => v.Id == m.PlannedVariantId)
                    .SelectMany(v => v.RemovedIngredients.Select(r => r.MealIngredientId)).ToList()))
            .ToListAsync(cancellationToken);

        // The slot is stored by name, so the database would sort the groups alphabetically; they
        // are put back into the order of the day here, with anything unslotted last.
        var inDayOrder = planned
            .OrderBy(m => m.Slot is null)
            .ThenBy(m => m.Slot)
            .ThenBy(m => m.Name)
            .ToList();

        foreach (var meal in inDayOrder)
        {
            var name = meal.VariantName is { } variant ? $"{meal.Name} ({variant})" : meal.Name;

            // The planned variant decides the list: it drops the parent lines it leaves out and
            // adds its own, which is what would actually be cooked.
            var lines = meal.Ingredients
                .Where(line => !meal.RemovedIngredientIds.Contains(line.Id))
                .Concat(meal.AddedIngredients)
                .Select(line => new GroceryCandidate(line.Item, line.Quantity, name, held.Contains(line.Item)))
                .ToList();

            if (lines.Count > 0)
            {
                groups.Add(new GroceryCandidateGroup(name, lines));
            }
        }

        return new GroceryCandidates(groups);
    }

    /// <summary>Returns the frequent items in the order they are offered.</summary>
    public async Task<IReadOnlyList<FrequentGroceryItemView>> ListFrequentAsync(CancellationToken cancellationToken = default)
    {
        return await context.FrequentGroceryItems.AsNoTracking()
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(f => new FrequentGroceryItemView(f.Id, f.Quantity, f.Item, f.SortOrder))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Adds a frequent item, or returns the one of that name that is already held.</summary>
    /// <exception cref="ArgumentException">The item is blank.</exception>
    public async Task<FrequentGroceryItemView> AddFrequentAsync(GroceryItemInput input, CancellationToken cancellationToken = default)
    {
        var item = (input.Item ?? string.Empty).Trim();
        if (item.Length == 0)
        {
            throw new ArgumentException("An item needs a name.", nameof(input));
        }

        var lowered = item.ToLowerInvariant();
        var held = await context.FrequentGroceryItems
            .FirstOrDefaultAsync(f => f.Item.ToLower() == lowered, cancellationToken);

        if (held is not null)
        {
            return new FrequentGroceryItemView(held.Id, held.Quantity, held.Item, held.SortOrder);
        }

        var count = await context.FrequentGroceryItems.CountAsync(cancellationToken);
        var row = new FrequentGroceryItem { Item = item, Quantity = Blank(input.Quantity), SortOrder = count };

        context.FrequentGroceryItems.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        return new FrequentGroceryItemView(row.Id, row.Quantity, row.Item, row.SortOrder);
    }

    /// <summary>Drops a frequent item, so it is no longer asked about. Returns false when no item has that id.</summary>
    public async Task<bool> RemoveFrequentAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await context.FrequentGroceryItems.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        context.FrequentGroceryItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<GroceryListView> OpenAsync(CancellationToken cancellationToken)
    {
        var list = new GroceryList { StartedAt = clock.GetUtcNow() };

        context.GroceryLists.Add(list);
        await context.SaveChangesAsync(cancellationToken);

        return new GroceryListView(list.Id, list.StartedAt, null, []);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly Expression<Func<GroceryList, GroceryListView>> Projection = l => new GroceryListView(
        l.Id,
        l.StartedAt,
        l.ClosedAt,
        l.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new GroceryItemView(i.Id, i.Quantity, i.Item, i.Source, i.SortOrder)).ToList());

    private record PlannedLine(int Id, string? Quantity, string Item);

    private record PlannedMeal(
        string Name,
        MealSlot? Slot,
        int? VariantId,
        string? VariantName,
        IReadOnlyList<PlannedLine> Ingredients,
        IReadOnlyList<PlannedLine> AddedIngredients,
        IReadOnlyList<int> RemovedIngredientIds);
}
