using CapybaraHome.Application.Abstractions;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Dashboards;

/// <summary>Builds the dashboard and keeps its cards. The dashboard is shared by every user.</summary>
public class DashboardService(ICapybaraHomeDbContext context, TaskService tasks)
{
    private const int TaskListCardSize = 10;

    /// <summary>Returns every card in dashboard order, each filled with its current numbers.</summary>
    public async Task<IReadOnlyList<DashboardCardView>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var cards = await context.DashboardCards.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var views = new List<DashboardCardView>(cards.Count);
        foreach (var card in cards)
        {
            views.Add(await FillAsync(card, cancellationToken));
        }

        return views;
    }

    /// <summary>Creates a card and returns it filled.</summary>
    /// <exception cref="ArgumentException">The title is blank, or a progress card names no category.</exception>
    public async Task<DashboardCardView> CreateAsync(DashboardCardRequest request, CancellationToken cancellationToken = default)
    {
        var card = new DashboardCard { Title = Validate(request) };
        Apply(card, request);

        if (request.SortOrder is null)
        {
            var last = await context.DashboardCards.MaxAsync(c => (int?)c.SortOrder, cancellationToken);
            card.SortOrder = (last ?? -1) + 1;
        }

        context.DashboardCards.Add(card);
        await context.SaveChangesAsync(cancellationToken);

        return await FillAsync(card, cancellationToken);
    }

    /// <summary>Replaces a card's fields, or returns null when no card has that id.</summary>
    public async Task<DashboardCardView?> UpdateAsync(int id, DashboardCardRequest request, CancellationToken cancellationToken = default)
    {
        var card = await context.DashboardCards.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null)
        {
            return null;
        }

        card.Title = Validate(request);
        Apply(card, request);

        await context.SaveChangesAsync(cancellationToken);
        return await FillAsync(card, cancellationToken);
    }

    /// <summary>Renames a card, leaving its filter alone. Returns null when no card has that id.</summary>
    public async Task<DashboardCardView?> RenameAsync(int id, string title, CancellationToken cancellationToken = default)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A card needs a title.", nameof(title));
        }

        var card = await context.DashboardCards.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null)
        {
            return null;
        }

        card.Title = trimmed;
        await context.SaveChangesAsync(cancellationToken);
        return await FillAsync(card, cancellationToken);
    }

    /// <summary>Deletes a card. Returns false when no card has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var card = await context.DashboardCards.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null)
        {
            return false;
        }

        context.DashboardCards.Remove(card);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Moves a card <paramref name="offset"/> places along the dashboard, and reports whether it moved.</summary>
    public async Task<bool> MoveAsync(int id, int offset, CancellationToken cancellationToken = default)
    {
        var ids = await context.DashboardCards.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var from = ids.IndexOf(id);
        var to = from + offset;

        if (from < 0 || to < 0 || to >= ids.Count)
        {
            return false;
        }

        (ids[from], ids[to]) = (ids[to], ids[from]);
        await ReorderAsync(ids, cancellationToken);
        return true;
    }

    /// <summary>Puts the cards in <paramref name="orderedIds"/> into that order on the dashboard.</summary>
    public async Task ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken cancellationToken = default)
    {
        var positions = orderedIds
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index);

        var cards = await context.DashboardCards.Where(c => positions.Keys.Contains(c.Id)).ToListAsync(cancellationToken);

        foreach (var card in cards)
        {
            card.SortOrder = positions[card.Id];
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Gives a fresh installation a dashboard worth looking at: one card per standing filter.</summary>
    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (await context.DashboardCards.AnyAsync(cancellationToken))
        {
            return;
        }

        context.DashboardCards.AddRange(
            new DashboardCard { Title = "Due this week", Kind = DashboardCardKind.TaskList, DueWithinDays = 7, SortOrder = 0 },
            new DashboardCard { Title = "In progress", Kind = DashboardCardKind.TaskCount, Status = TaskItemStatus.InProgress, SortOrder = 1 },
            new DashboardCard { Title = "Still to do", Kind = DashboardCardKind.TaskCount, Status = TaskItemStatus.ToDo, SortOrder = 2 },
            new DashboardCard { Title = "On hold", Kind = DashboardCardKind.TaskCount, Status = TaskItemStatus.OnHold, SortOrder = 3 });

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<DashboardCardView> FillAsync(DashboardCard card, CancellationToken cancellationToken)
    {
        var filter = new TaskFilter(
            Statuses: card.Status is { } status ? [status] : null,
            CategoryId: card.CategoryId,
            DueWithinDays: card.DueWithinDays);

        if (card.Kind == DashboardCardKind.CategoryProgress)
        {
            var total = await tasks.CountAsync(new TaskFilter(CategoryId: card.CategoryId), cancellationToken);
            var done = await tasks.CountAsync(new TaskFilter(CategoryId: card.CategoryId, Statuses: [TaskItemStatus.Done]), cancellationToken);
            return View(card, total, done, []);
        }

        if (card.Kind == DashboardCardKind.TaskList)
        {
            var list = await tasks.ListAsync(filter with { Limit = TaskListCardSize }, cancellationToken: cancellationToken);
            var total = await tasks.CountAsync(filter, cancellationToken);
            return View(card, total, 0, list);
        }

        var count = await tasks.CountAsync(filter, cancellationToken);
        return View(card, count, 0, []);
    }

    private static DashboardCardView View(DashboardCard card, int total, int done, IReadOnlyList<TaskView> list) => new(
        card.Id,
        card.Title,
        card.Kind,
        card.CategoryId,
        card.Status,
        card.DueWithinDays,
        card.SortOrder,
        total,
        done,
        list);

    private static string Validate(DashboardCardRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("A card needs a title.", nameof(request));
        }

        if (request.Kind == DashboardCardKind.CategoryProgress && request.CategoryId is null)
        {
            throw new ArgumentException("A progress card has to name a category.", nameof(request));
        }

        return title;
    }

    private static void Apply(DashboardCard card, DashboardCardRequest request)
    {
        card.Kind = request.Kind;
        card.CategoryId = request.CategoryId;
        card.Status = request.Status;
        card.DueWithinDays = request.DueWithinDays;

        if (request.SortOrder is { } order)
        {
            card.SortOrder = order;
        }
    }
}
