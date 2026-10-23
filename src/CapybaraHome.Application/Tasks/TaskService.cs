using System.Linq.Expressions;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Tasks;

/// <summary>Reads and writes tasks. The only place task rules live.</summary>
public class TaskService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>Returns the tasks matching <paramref name="filter"/> in the order <paramref name="sort"/> asks for.</summary>
    public async Task<IReadOnlyList<TaskView>> ListAsync(TaskFilter filter, TaskSort sort = TaskSort.DueDate, CancellationToken cancellationToken = default)
    {
        var matching = Filter(context.Tasks.AsNoTracking(), filter);

        if (sort == TaskSort.RecentlyUpdated)
        {
            return await PageByChangeAsync(matching, filter, cancellationToken);
        }

        var query = Order(matching, sort);

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

    /// <summary>
    /// Returns the asked-for page of <paramref name="matching"/> ordered by when each task last
    /// changed. SQLite refuses a DateTimeOffset in ORDER BY, so the order is settled here instead:
    /// the first query reads only the id and the timestamp of every matching row, and the second
    /// reads the page's rows in full, which keeps the whole listing off the wire.
    /// </summary>
    private async Task<IReadOnlyList<TaskView>> PageByChangeAsync(IQueryable<TaskItem> matching, TaskFilter filter, CancellationToken cancellationToken)
    {
        var keys = await matching
            .Select(t => new { t.Id, t.UpdatedAt })
            .ToListAsync(cancellationToken);

        IEnumerable<int> ordered = keys
            .OrderByDescending(k => k.UpdatedAt)
            .ThenBy(k => k.Id)
            .Select(k => k.Id);

        if (filter.Skip > 0)
        {
            ordered = ordered.Skip(filter.Skip);
        }

        if (filter.Limit is > 0)
        {
            ordered = ordered.Take(filter.Limit.Value);
        }

        var ids = ordered.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var page = await context.Tasks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(Projection)
            .ToListAsync(cancellationToken);

        var byId = page.ToDictionary(t => t.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>Returns one task, or null when no task has that id.</summary>
    public async Task<TaskView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.Tasks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Counts the tasks matching <paramref name="filter"/>.</summary>
    public Task<int> CountAsync(TaskFilter filter, CancellationToken cancellationToken = default)
    {
        return Filter(context.Tasks.AsNoTracking(), filter).CountAsync(cancellationToken);
    }

    /// <summary>Creates a task and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The title is blank.</exception>
    public async Task<TaskView> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("A task needs a title.", nameof(request));
        }

        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        var task = new TaskItem
        {
            Title = title,
            Notes = Blank(request.Notes),
            Status = request.Status,
            CategoryId = request.CategoryId,
            DueDate = request.DueDate,
            CompletedAt = request.Status == TaskItemStatus.Done ? clock.GetUtcNow() : null,
        };

        context.Tasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(task.Id, cancellationToken) ?? throw new InvalidOperationException("The new task could not be read back.");
    }

    /// <summary>Applies the set members of <paramref name="request"/>, or returns null when no task has that id.</summary>
    public async Task<TaskView?> UpdateAsync(int id, UpdateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var task = await context.Tasks.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (title.Length == 0)
            {
                throw new ArgumentException("A task needs a title.", nameof(request));
            }

            task.Title = title;
        }

        if (request.Notes is not null)
        {
            task.Notes = Blank(request.Notes);
        }

        if (request.ClearCategory)
        {
            task.CategoryId = null;
        }
        else if (request.CategoryId is not null)
        {
            await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);
            task.CategoryId = request.CategoryId;
        }

        if (request.ClearDueDate)
        {
            task.DueDate = null;
        }
        else if (request.DueDate is not null)
        {
            task.DueDate = request.DueDate;
        }

        if (request.Status is { } status && status != task.Status)
        {
            task.Status = status;
            task.CompletedAt = status == TaskItemStatus.Done ? clock.GetUtcNow() : null;
        }

        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a task. Returns false when no task has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var task = await context.Tasks.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null)
        {
            return false;
        }

        context.Tasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IQueryable<TaskItem> Order(IQueryable<TaskItem> query, TaskSort sort) => sort switch
    {
        TaskSort.DueDateDescending => query
            .OrderBy(t => t.DueDate == null)
            .ThenByDescending(t => t.DueDate)
            .ThenBy(t => t.Title)
            .ThenBy(t => t.Id),
        TaskSort.Title => query
            .OrderBy(t => t.Title)
            .ThenBy(t => t.Id),
        TaskSort.TitleDescending => query
            .OrderByDescending(t => t.Title)
            .ThenBy(t => t.Id),
        TaskSort.Category => query
            .OrderBy(t => t.CategoryId == null)
            .ThenBy(t => t.Category!.Name)
            .ThenBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Id),
        _ => query
            .OrderBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .ThenBy(t => t.Id),
    };

    private IQueryable<TaskItem> Filter(IQueryable<TaskItem> query, TaskFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            query = query.Where(t => EF.Functions.Like(t.Title, $"%{text}%")
                || (t.Notes != null && EF.Functions.Like(t.Notes, $"%{text}%")));
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.Distinct().ToList();
            query = query.Where(t => wanted.Contains(t.Status));
        }

        if (filter.Uncategorized)
        {
            query = query.Where(t => t.CategoryId == null);
        }
        else if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (filter.DueWithinDays is { } days)
        {
            var cutoff = today.AddDays(days);
            query = query.Where(t => t.DueDate != null && t.DueDate <= cutoff);
        }

        if (filter.Overdue)
        {
            query = query.Where(t => t.DueDate != null && t.DueDate < today && t.Status != TaskItemStatus.Done);
        }

        return query;
    }

    private async Task EnsureCategoryExistsAsync(int? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return;
        }

        var exists = await context.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken);
        if (!exists)
        {
            throw new ArgumentException($"No category has id {categoryId}.", nameof(categoryId));
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static readonly Expression<Func<TaskItem, TaskView>> Projection = t => new TaskView(
        t.Id,
        t.Title,
        t.Notes,
        t.Status,
        t.CategoryId,
        t.Category == null ? null : t.Category.Name,
        t.DueDate,
        t.CompletedAt,
        t.JiraKey,
        t.CreatedAt,
        t.UpdatedAt);
}
