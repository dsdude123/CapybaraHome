using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Domain.Dashboards;

/// <summary>One card on the shared dashboard, with the filter it reports on.</summary>
public class DashboardCard
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Heading shown on the card; the owner can rename it freely.</summary>
    public required string Title { get; set; }

    /// <summary>What the card shows.</summary>
    public DashboardCardKind Kind { get; set; }

    /// <summary>Category the card reports on. Required for <see cref="DashboardCardKind.CategoryProgress"/>.</summary>
    public int? CategoryId { get; set; }

    /// <summary>Navigation to <see cref="CategoryId"/>.</summary>
    public Category? Category { get; set; }

    /// <summary>Status the card filters on, or null for any status.</summary>
    public TaskItemStatus? Status { get; set; }

    /// <summary>Upper bound in days on how far away a task's due date may be, or null for any.</summary>
    public int? DueWithinDays { get; set; }

    /// <summary>Position on the dashboard; lower sorts first.</summary>
    public int SortOrder { get; set; }
}
