using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Categories;

/// <summary>Reads and writes categories, which stand in for the epics of the JIRA setup this replaces.</summary>
public class CategoryService(ICapybaraHomeDbContext context)
{
    /// <summary>Returns every category in sort order, each with its task counts.</summary>
    public async Task<IReadOnlyList<CategoryView>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryView(
                c.Id,
                c.Name,
                c.ColorHex,
                c.SortOrder,
                c.Tasks.Count,
                c.Tasks.Count(t => t.Status == TaskItemStatus.Done)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns one category, or null when no category has that id.</summary>
    public async Task<CategoryView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(cancellationToken);
        return all.SingleOrDefault(c => c.Id == id);
    }

    /// <summary>Creates a category and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<CategoryView> CreateAsync(CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var name = Name(request);
        var category = new Category { Name = name, ColorHex = request.ColorHex, SortOrder = request.SortOrder };

        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        return new CategoryView(category.Id, category.Name, category.ColorHex, category.SortOrder, 0, 0);
    }

    /// <summary>Replaces a category's fields, or returns null when no category has that id.</summary>
    public async Task<CategoryView?> UpdateAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        category.Name = Name(request);
        category.ColorHex = request.ColorHex;
        category.SortOrder = request.SortOrder;

        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a category, leaving its tasks uncategorized. Returns false when no category has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return false;
        }

        context.Categories.Remove(category);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Name(CategoryRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        return name.Length == 0
            ? throw new ArgumentException("A category needs a name.", nameof(request))
            : name;
    }
}
