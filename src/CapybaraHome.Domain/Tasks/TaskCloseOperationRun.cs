namespace CapybaraHome.Domain.Tasks;

/// <summary>
/// What one completion asked one operation to do, and how it went. The title, the kind and a
/// produced document are copied onto the run so the record still reads after the task or the
/// operation behind it is deleted.
/// </summary>
public class TaskCloseOperationRun
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Operation that was run, or null once it has been deleted.</summary>
    public int? OperationId { get; set; }

    /// <summary>Navigation to <see cref="OperationId"/>.</summary>
    public TaskCloseOperation? Operation { get; set; }

    /// <summary>Task that was completed, or null once it has been deleted.</summary>
    public int? TaskId { get; set; }

    /// <summary>Navigation to <see cref="TaskId"/>.</summary>
    public TaskItem? Task { get; set; }

    /// <summary>Title the completed task had.</summary>
    public required string TaskTitle { get; set; }

    /// <summary>What the operation does.</summary>
    public TaskCloseOperationKind Kind { get; set; }

    /// <summary>Where the run stands.</summary>
    public TaskCloseRunStatus Status { get; set; } = TaskCloseRunStatus.Pending;

    /// <summary>How many times it has been attempted.</summary>
    public int Attempts { get; set; }

    /// <summary>When it was last attempted, or null while it never has been.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>What happened, in the words the run list shows.</summary>
    public string? Detail { get; set; }

    /// <summary>The JSON body a webhook run posts, built when the task was completed.</summary>
    public string? WebhookPayload { get; set; }

    /// <summary>Task this run created, or null when it created none.</summary>
    public int? CreatedTaskId { get; set; }

    /// <summary>Heading of the document this run produced, or null when it produced none.</summary>
    public string? DocumentTitle { get; set; }

    /// <summary>Body of the document this run produced, or null when it produced none.</summary>
    public string? DocumentBody { get; set; }

    /// <summary>When the task was completed, in UTC.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
