using System.Linq.Expressions;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Meals;

/// <summary>Reads and writes meals. The only place meal rules live.</summary>
public class MealService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>Returns the meals matching <paramref name="filter"/> in the order <paramref name="sort"/> asks for.</summary>
    public async Task<IReadOnlyList<MealView>> ListAsync(MealFilter filter, MealSort sort = MealSort.LastCooked, CancellationToken cancellationToken = default)
    {
        var query = Order(Filter(context.Meals.AsNoTracking(), filter), sort);

        if (filter.Skip > 0)
        {
            query = query.Skip(filter.Skip);
        }

        if (filter.Limit is > 0)
        {
            query = query.Take(filter.Limit.Value);
        }

        return await query.Select(Projection).ToListAsync(cancellationToken);
    }

    /// <summary>Returns one meal, or null when no meal has that id.</summary>
    public async Task<MealView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.Meals.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Counts the meals matching <paramref name="filter"/>.</summary>
    public Task<int> CountAsync(MealFilter filter, CancellationToken cancellationToken = default)
    {
        return Filter(context.Meals.AsNoTracking(), filter).CountAsync(cancellationToken);
    }

    /// <summary>Creates a meal and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<MealView> CreateAsync(CreateMealRequest request, CancellationToken cancellationToken = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A meal needs a name.", nameof(request));
        }

        var meal = new Meal
        {
            Name = name,
            Directions = Blank(request.Directions),
            Status = request.Status,
            IsOneTime = request.IsOneTime,
            LastCookedAt = request.LastCookedAt,
            Ingredients = Lines(request.Ingredients),
            Tags = await TagsAsync(request.TagIds, cancellationToken),
        };

        context.Meals.Add(meal);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(meal.Id, cancellationToken) ?? throw new InvalidOperationException("The new meal could not be read back.");
    }

    /// <summary>Applies the set members of <paramref name="request"/>, or returns null when no meal has that id.</summary>
    public async Task<MealView?> UpdateAsync(int id, UpdateMealRequest request, CancellationToken cancellationToken = default)
    {
        var meal = await context.Meals
            .Include(m => m.Ingredients)
            .Include(m => m.Tags)
            .SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (meal is null)
        {
            return null;
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("A meal needs a name.", nameof(request));
            }

            meal.Name = name;
        }

        if (request.Directions is not null)
        {
            meal.Directions = Blank(request.Directions);
        }

        if (request.ImportedText is not null)
        {
            meal.ImportedText = Blank(request.ImportedText);
        }

        if (request.NeedsIngredientSplit is { } needsSplit)
        {
            meal.NeedsIngredientSplit = needsSplit;
        }

        if (request.IsOneTime is { } oneTime)
        {
            meal.IsOneTime = oneTime;
        }

        // Cooking a meal is recorded by sending it back to available or pooled, so that move is
        // what stamps the date. An explicit date in the request wins over the stamp.
        if (request.Status is { } status && status != meal.Status)
        {
            var cooked = status is MealStatus.Available or MealStatus.Pooled;
            meal.Status = status;

            if (cooked)
            {
                meal.LastCookedAt = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            }
        }

        if (request.ClearLastCookedAt)
        {
            meal.LastCookedAt = null;
        }
        else if (request.LastCookedAt is { } lastCooked)
        {
            meal.LastCookedAt = lastCooked;
        }

        if (request.Ingredients is not null)
        {
            context.MealIngredients.RemoveRange(meal.Ingredients);
            meal.Ingredients = Lines(request.Ingredients);
        }

        if (request.TagIds is not null)
        {
            meal.Tags = await TagsAsync(request.TagIds, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a meal and its ingredients. Returns false when no meal has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var meal = await context.Meals.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (meal is null)
        {
            return false;
        }

        context.Meals.Remove(meal);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<List<MealTag>> TagsAsync(IReadOnlyCollection<int>? tagIds, CancellationToken cancellationToken)
    {
        if (tagIds is not { Count: > 0 })
        {
            return [];
        }

        var wanted = tagIds.Distinct().ToList();
        var found = await context.MealTags.Where(t => wanted.Contains(t.Id)).ToListAsync(cancellationToken);

        if (found.Count != wanted.Count)
        {
            var missing = wanted.Except(found.Select(t => t.Id)).First();
            throw new ArgumentException($"No tag has id {missing}.", nameof(tagIds));
        }

        return found;
    }

    private static List<MealIngredient> Lines(IReadOnlyList<MealIngredientInput>? ingredients)
    {
        if (ingredients is not { Count: > 0 })
        {
            return [];
        }

        return ingredients
            .Select(line => new { Item = (line.Item ?? string.Empty).Trim(), Quantity = Blank(line.Quantity)?.Trim() })
            .Where(line => line.Item.Length > 0)
            .Select((line, index) => new MealIngredient { Item = line.Item, Quantity = line.Quantity, SortOrder = index })
            .ToList();
    }

    private static IQueryable<Meal> Order(IQueryable<Meal> query, MealSort sort) => sort switch
    {
        MealSort.LastCookedDescending => query
            .OrderBy(m => m.LastCookedAt == null)
            .ThenByDescending(m => m.LastCookedAt)
            .ThenBy(m => m.Name)
            .ThenBy(m => m.Id),
        MealSort.Name => query
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Id),
        MealSort.NameDescending => query
            .OrderByDescending(m => m.Name)
            .ThenBy(m => m.Id),
        _ => query
            .OrderByDescending(m => m.LastCookedAt == null)
            .ThenBy(m => m.LastCookedAt)
            .ThenBy(m => m.Name)
            .ThenBy(m => m.Id),
    };

    private static IQueryable<Meal> Filter(IQueryable<Meal> query, MealFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(m => EF.Functions.Like(m.Name, $"%{text}%")
                || (m.Directions != null && EF.Functions.Like(m.Directions, $"%{text}%"))
                || m.Ingredients.Any(i => EF.Functions.Like(i.Item, $"%{text}%")));
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.Distinct().ToList();
            query = query.Where(m => wanted.Contains(m.Status));
        }

        // Each facet's picker contributes one tag, and a meal has to carry all of them, so the
        // filters narrow each other rather than widening the result.
        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            foreach (var tagId in tagIds.Distinct())
            {
                var wanted = tagId;
                query = query.Where(m => m.Tags.Any(t => t.Id == wanted));
            }
        }

        if (filter.NeedsIngredientSplit is { } needsSplit)
        {
            query = query.Where(m => m.NeedsIngredientSplit == needsSplit);
        }

        return query;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static readonly Expression<Func<Meal, MealView>> Projection = m => new MealView(
        m.Id,
        m.Name,
        m.Directions,
        m.Status,
        m.IsOneTime,
        m.LastCookedAt,
        m.NeedsIngredientSplit,
        m.JiraKey,
        m.Ingredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new MealIngredientView(i.Id, i.Quantity, i.Item, i.SortOrder)).ToList(),
        m.Tags.OrderBy(t => t.Facet).ThenBy(t => t.Name)
            .Select(t => new MealTagView(t.Id, t.Name, t.Facet)).ToList(),
        m.CreatedAt,
        m.UpdatedAt);
}
