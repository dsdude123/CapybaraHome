namespace CapybaraHome.Domain.Tasks;

/// <summary>A single thing to do, with an optional category and due date.</summary>
public class TaskItem
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Short description of the work.</summary>
    public required string Title { get; set; }

    /// <summary>Free-form detail, held as markdown.</summary>
    public string? Notes { get; set; }

    /// <summary>Category this task belongs to, or null when it belongs to none.</summary>
    public int? CategoryId { get; set; }

    /// <summary>Navigation to <see cref="CategoryId"/>.</summary>
    public Category? Category { get; set; }

    /// <summary>Current state.</summary>
    public TaskItemStatus Status { get; set; } = TaskItemStatus.ToDo;

    /// <summary>Day the task is due, with no time component.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>When the task was completed, or null while it is not done.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Key of the JIRA issue this task was imported from, used to keep imports re-runnable.</summary>
    public string? JiraKey { get; set; }

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
