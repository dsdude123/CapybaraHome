using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Meals;

/// <summary>Reads and writes the tags meals are filtered by.</summary>
public class MealTagService(ICapybaraHomeDbContext context)
{
    /// <summary>Returns every tag, grouped by facet and alphabetical within it.</summary>
    public async Task<IReadOnlyList<MealTagView>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.MealTags.AsNoTracking()
            .OrderBy(t => t.Facet)
            .ThenBy(t => t.Name)
            .Select(t => new MealTagView(t.Id, t.Name, t.Facet))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns the tag with this name, whatever its case, creating it when there is none.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<MealTagView> EnsureAsync(MealTagRequest request, CancellationToken cancellationToken = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A tag needs a name.", nameof(request));
        }

        var lowered = name.ToLowerInvariant();
        var existing = await context.MealTags.SingleOrDefaultAsync(t => t.Name.ToLower() == lowered, cancellationToken);
        if (existing is not null)
        {
            return new MealTagView(existing.Id, existing.Name, existing.Facet);
        }

        var tag = new MealTag { Name = name, Facet = request.Facet };
        context.MealTags.Add(tag);
        await context.SaveChangesAsync(cancellationToken);

        return new MealTagView(tag.Id, tag.Name, tag.Facet);
    }

    /// <summary>Renames a tag or moves it to another facet, or returns null when no tag has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<MealTagView?> UpdateAsync(int id, MealTagRequest request, CancellationToken cancellationToken = default)
    {
        var tag = await context.MealTags.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag is null)
        {
            return null;
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A tag needs a name.", nameof(request));
        }

        tag.Name = name;
        tag.Facet = request.Facet;
        await context.SaveChangesAsync(cancellationToken);

        return new MealTagView(tag.Id, tag.Name, tag.Facet);
    }

    /// <summary>Deletes a tag, taking it off every meal. Returns false when no tag has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var tag = await context.MealTags.Include(t => t.Meals).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag is null)
        {
            return false;
        }

        tag.Meals.Clear();
        context.MealTags.Remove(tag);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
