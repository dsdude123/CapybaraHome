using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Dashboards;

/// <summary>A dashboard card together with the numbers it reports.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Title">Heading shown on the card.</param>
/// <param name="Kind">What the card shows.</param>
/// <param name="CategoryId">Category the card reports on, or null.</param>
/// <param name="Status">Status the card filters on, or null.</param>
/// <param name="DueWithinDays">Days-away bound the card filters on, or null.</param>
/// <param name="SortOrder">Position on the dashboard.</param>
/// <param name="Total">Tasks matching the card, or the category's total for a progress card.</param>
/// <param name="Done">Tasks of <paramref name="Total"/> that are done. Only meaningful for a progress card.</param>
/// <param name="Tasks">The matching tasks, for a list card.</param>
public record DashboardCardView(
    int Id,
    string Title,
    DashboardCardKind Kind,
    int? CategoryId,
    TaskItemStatus? Status,
    int? DueWithinDays,
    int SortOrder,
    int Total,
    int Done,
    IReadOnlyList<TaskView> Tasks)
{
    /// <summary>Share of the card's tasks that are done, from 0 to 1.</summary>
    public double Progress => Total == 0 ? 0 : (double)Done / Total;
}

/// <summary>Fields accepted when creating or updating a dashboard card.</summary>
/// <param name="Title">Heading to show; required.</param>
/// <param name="Kind">What the card should show.</param>
/// <param name="CategoryId">Category to report on. Required for a progress card.</param>
/// <param name="Status">Status to filter on, or null for any.</param>
/// <param name="DueWithinDays">Days-away bound to filter on, or null for any.</param>
/// <param name="SortOrder">Position on the dashboard.</param>
public record DashboardCardRequest(
    string Title,
    DashboardCardKind Kind,
    int? CategoryId = null,
    TaskItemStatus? Status = null,
    int? DueWithinDays = null,
    int SortOrder = 0);
