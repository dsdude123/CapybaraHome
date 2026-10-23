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

        if (request.Status is { } moved && moved is not (MealStatus.Planned or MealStatus.PlannedShared))
        {
            meal.PlannedSlot = null;
            meal.PlannedVariantId = null;
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

    /// <summary>
    /// Puts a meal, or one of its variants, on the plan. Returns null when no meal has that id.
    /// </summary>
    /// <param name="mealId">The meal to plan.</param>
    /// <param name="slot">Which meal of the day it fills.</param>
    /// <param name="variantId">The variant to plan, or null for the meal itself.</param>
    /// <param name="shared">Whether it is one of the shared meals.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentException">The variant belongs to another meal.</exception>
    public async Task<MealView?> PlanAsync(
        int mealId,
        MealSlot slot,
        int? variantId = null,
        bool shared = false,
        CancellationToken cancellationToken = default)
    {
        var meal = await context.Meals
            .Include(m => m.Variants)
            .SingleOrDefaultAsync(m => m.Id == mealId, cancellationToken);

        if (meal is null)
        {
            return null;
        }

        if (variantId is { } wanted && meal.Variants.All(v => v.Id != wanted))
        {
            throw new ArgumentException($"Variant {wanted} is not a variant of this meal.", nameof(variantId));
        }

        meal.Status = shared ? MealStatus.PlannedShared : MealStatus.Planned;
        meal.PlannedSlot = slot;
        meal.PlannedVariantId = variantId;

        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(mealId, cancellationToken);
    }

    /// <summary>
    /// Returns what the planning screens can choose from: every matching meal, and every variant
    /// of those meals as a row of its own, since a variant is picked the same way a meal is.
    /// </summary>
    public async Task<IReadOnlyList<MealPlanOption>> ListOptionsAsync(
        MealFilter filter,
        MealSort sort = MealSort.LastCooked,
        CancellationToken cancellationToken = default)
    {
        var meals = await ListAsync(filter, sort, cancellationToken);
        var options = new List<MealPlanOption>();

        foreach (var meal in meals)
        {
            options.Add(new MealPlanOption(
                meal.Id, null, meal.Name, meal.Status, meal.LastCookedAt, meal.Tags, meal.Variants.Count > 0));

            options.AddRange(meal.Variants.Select(variant => new MealPlanOption(
                meal.Id, variant.Id, $"{meal.Name} ({variant.Name})", meal.Status, meal.LastCookedAt, meal.Tags, true)));
        }

        return options;
    }

    /// <summary>Adds a variant to a meal, or returns null when no meal has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank, or an ingredient to leave out is not the meal's.</exception>
    public async Task<MealVariantView?> CreateVariantAsync(int mealId, MealVariantRequest request, CancellationToken cancellationToken = default)
    {
        var meal = await context.Meals
            .Include(m => m.Ingredients)
            .Include(m => m.Variants)
            .SingleOrDefaultAsync(m => m.Id == mealId, cancellationToken);

        if (meal is null)
        {
            return null;
        }

        var variant = new MealVariant
        {
            MealId = mealId,
            Name = VariantName(request),
            DirectionsModifications = Blank(request.DirectionsModifications),
            SortOrder = meal.Variants.Count,
            AddedIngredients = AddedLines(request.AddedIngredients),
            RemovedIngredients = Removals(meal, request.RemovedIngredientIds),
        };

        context.MealVariants.Add(variant);

        // The meal's own row is touched so that a change to one of its variants shows up as a
        // change to the meal, which is the only timestamp either of them has.
        meal.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return await GetVariantAsync(variant.Id, cancellationToken);
    }

    /// <summary>Returns one variant, or null when no variant has that id.</summary>
    public async Task<MealVariantView?> GetVariantAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.MealVariants.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(VariantProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Replaces a variant's fields, or returns null when no variant has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank, or an ingredient to leave out is not the meal's.</exception>
    public async Task<MealVariantView?> UpdateVariantAsync(int id, MealVariantRequest request, CancellationToken cancellationToken = default)
    {
        var variant = await context.MealVariants
            .Include(v => v.AddedIngredients)
            .Include(v => v.RemovedIngredients)
            .Include(v => v.Meal!.Ingredients)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (variant is null)
        {
            return null;
        }

        variant.Name = VariantName(request);
        variant.DirectionsModifications = Blank(request.DirectionsModifications);

        context.MealVariantIngredients.RemoveRange(variant.AddedIngredients);
        variant.AddedIngredients = AddedLines(request.AddedIngredients);

        context.MealVariantRemovals.RemoveRange(variant.RemovedIngredients);
        variant.RemovedIngredients = Removals(variant.Meal!, request.RemovedIngredientIds);

        variant.Meal!.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return await GetVariantAsync(id, cancellationToken);
    }

    /// <summary>Deletes a variant. Returns false when no variant has that id.</summary>
    public async Task<bool> DeleteVariantAsync(int id, CancellationToken cancellationToken = default)
    {
        var variant = await context.MealVariants.Include(v => v.Meal).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (variant is null)
        {
            return false;
        }

        context.MealVariants.Remove(variant);
        variant.Meal!.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string VariantName(MealVariantRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        return name.Length > 0 ? name : throw new ArgumentException("A variant needs a name.", nameof(request));
    }

    private static List<MealVariantIngredient> AddedLines(IReadOnlyList<MealIngredientInput>? ingredients)
    {
        return Lines(ingredients)
            .Select(line => new MealVariantIngredient { Quantity = line.Quantity, Item = line.Item, SortOrder = line.SortOrder })
            .ToList();
    }

    private static List<MealVariantRemoval> Removals(Meal meal, IReadOnlyCollection<int>? removedIngredientIds)
    {
        if (removedIngredientIds is not { Count: > 0 })
        {
            return [];
        }

        var removals = new List<MealVariantRemoval>();

        foreach (var ingredientId in removedIngredientIds.Distinct())
        {
            if (meal.Ingredients.All(i => i.Id != ingredientId))
            {
                throw new ArgumentException(
                    $"Ingredient {ingredientId} is not on this meal.", nameof(removedIngredientIds));
            }

            removals.Add(new MealVariantRemoval { MealIngredientId = ingredientId });
        }

        return removals;
    }

    private static readonly Expression<Func<MealVariant, MealVariantView>> VariantProjection = v => new MealVariantView(
        v.Id,
        v.MealId,
        v.Name,
        v.DirectionsModifications,
        v.AddedIngredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new MealIngredientView(i.Id, i.Quantity, i.Item, i.SortOrder)).ToList(),
        v.RemovedIngredients.Select(r => r.MealIngredientId).ToList(),
        v.SortOrder);

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
        m.PlannedSlot,
        m.PlannedVariantId,
        m.PlannedVariant == null ? null : m.PlannedVariant.Name,
        m.ImportedText,
        m.NeedsIngredientSplit,
        m.JiraKey,
        m.Ingredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new MealIngredientView(i.Id, i.Quantity, i.Item, i.SortOrder)).ToList(),
        m.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id)
            .Select(v => new MealVariantView(
                v.Id,
                v.MealId,
                v.Name,
                v.DirectionsModifications,
                v.AddedIngredients.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => new MealIngredientView(i.Id, i.Quantity, i.Item, i.SortOrder)).ToList(),
                v.RemovedIngredients.Select(r => r.MealIngredientId).ToList(),
                v.SortOrder)).ToList(),
        m.Tags.OrderBy(t => t.Facet).ThenBy(t => t.Name)
            .Select(t => new MealTagView(t.Id, t.Name, t.Facet)).ToList(),
        m.CreatedAt,
        m.UpdatedAt);
}
