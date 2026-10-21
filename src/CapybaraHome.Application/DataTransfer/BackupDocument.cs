using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.DataTransfer;

/// <summary>Everything an export carries, and everything an import reads.</summary>
/// <param name="FormatVersion">Schema version of the document, so a later import can tell what it is reading.</param>
/// <param name="ExportedAt">When the export was taken, in UTC.</param>
/// <param name="Categories">Every category.</param>
/// <param name="Tasks">Every task.</param>
/// <param name="DashboardCards">Every dashboard card.</param>
public record BackupDocument(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<CategoryRecord> Categories,
    IReadOnlyList<TaskRecord> Tasks,
    IReadOnlyList<DashboardCardRecord> DashboardCards)
{
    /// <summary>The version this build writes and can read.</summary>
    public const int CurrentFormatVersion = 1;
}

/// <summary>A category in an export. Ids are carried so tasks and cards can point at them.</summary>
/// <param name="Id">Primary key as exported.</param>
/// <param name="Name">Display name.</param>
/// <param name="ColorHex">Hex colour, or null.</param>
/// <param name="SortOrder">Position in hand-ordered lists.</param>
public record CategoryRecord(int Id, string Name, string? ColorHex, int SortOrder);

/// <summary>A task in an export.</summary>
/// <param name="Id">Primary key as exported.</param>
/// <param name="Title">Short description of the work.</param>
/// <param name="Notes">Free-form detail, held as markdown.</param>
/// <param name="CategoryId">Category it was filed under, or null.</param>
/// <param name="Status">State it was in.</param>
/// <param name="DueDate">Day it is due, or null.</param>
/// <param name="CompletedAt">When it was completed, or null.</param>
/// <param name="JiraKey">Key of the JIRA issue it came from, or null.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record TaskRecord(
    int Id,
    string Title,
    string? Notes,
    int? CategoryId,
    TaskItemStatus Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    string? JiraKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A dashboard card in an export.</summary>
/// <param name="Title">Heading shown on the card.</param>
/// <param name="Kind">What the card shows.</param>
/// <param name="CategoryId">Category it reports on, or null.</param>
/// <param name="Status">Status it filters on, or null.</param>
/// <param name="DueWithinDays">Days-away bound it filters on, or null.</param>
/// <param name="SortOrder">Position on the dashboard.</param>
public record DashboardCardRecord(
    string Title,
    DashboardCardKind Kind,
    int? CategoryId,
    TaskItemStatus? Status,
    int? DueWithinDays,
    int SortOrder);
