namespace CapybaraHome.Domain.Tasks;

/// <summary>
/// Something that happens when a task is moved to done. A task carries as many of these as it
/// needs, and the fields each kind reads are the ones the editor shows for that kind.
/// </summary>
public class TaskCloseOperation
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Task this operation belongs to.</summary>
    public int TaskId { get; set; }

    /// <summary>Navigation to <see cref="TaskId"/>.</summary>
    public TaskItem? Task { get; set; }

    /// <summary>What the operation does.</summary>
    public TaskCloseOperationKind Kind { get; set; }

    /// <summary>Position in the task's list, which is also the order the operations run in.</summary>
    public int SortOrder { get; set; }

    /// <summary>Whether the operation runs at all.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>URL a webhook operation calls.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Method a webhook operation is sent with.</summary>
    public TaskWebhookMethod WebhookMethod { get; set; } = TaskWebhookMethod.Post;

    /// <summary>Title the created task is given, or null to reuse the completed task's title.</summary>
    public string? FollowUpTitle { get; set; }

    /// <summary>How the created task's due date is worked out.</summary>
    public TaskDueDateMode DueDateMode { get; set; } = TaskDueDateMode.None;

    /// <summary>How many days, weeks, months or years later the created task is due.</summary>
    public int? DueDateValue { get; set; }

    /// <summary>Day the created task is due when the mode is a date picked by hand.</summary>
    public DateOnly? DueDateOn { get; set; }

    /// <summary>Whether the created task gets a copy of the completed task's operations.</summary>
    public bool CarryOperationsForward { get; set; } = true;

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
