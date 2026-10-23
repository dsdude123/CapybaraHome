using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Tasks;

/// <summary>A task as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Title">Short description of the work.</param>
/// <param name="Notes">Free-form detail, held as markdown.</param>
/// <param name="Status">Current state.</param>
/// <param name="CategoryId">Category the task belongs to, or null.</param>
/// <param name="CategoryName">Name of that category, or null.</param>
/// <param name="DueDate">Day the task is due, or null.</param>
/// <param name="CompletedAt">When the task was completed, or null.</param>
/// <param name="JiraKey">Key of the JIRA issue the task came from, or null.</param>
/// <param name="CreatedAt">When the task was created.</param>
/// <param name="UpdatedAt">When the task last changed.</param>
public record TaskView(
    int Id,
    string Title,
    string? Notes,
    TaskItemStatus Status,
    int? CategoryId,
    string? CategoryName,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    string? JiraKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Filters for a task listing. Every member is optional and narrows the result.</summary>
/// <param name="Text">Case-insensitive substring matched against title and notes.</param>
/// <param name="Status">Keep only tasks in this state.</param>
/// <param name="CategoryId">Keep only tasks in this category.</param>
/// <param name="Uncategorized">When true, keep only tasks with no category.</param>
/// <param name="DueWithinDays">Keep only tasks due no more than this many days from today.</param>
/// <param name="Overdue">When true, keep only tasks due before today and not done.</param>
/// <param name="Limit">Maximum number of tasks to return.</param>
/// <param name="Skip">Number of matching tasks to pass over before the first one returned.</param>
public record TaskFilter(
    string? Text = null,
    TaskItemStatus? Status = null,
    int? CategoryId = null,
    bool Uncategorized = false,
    int? DueWithinDays = null,
    bool Overdue = false,
    int? Limit = null,
    int Skip = 0);

/// <summary>The orders a task listing can be read in. A task with no due date always sorts last.</summary>
public enum TaskSort
{
    /// <summary>Earliest due date first.</summary>
    DueDate,

    /// <summary>Latest due date first.</summary>
    DueDateDescending,

    /// <summary>Title from A to Z.</summary>
    Title,

    /// <summary>Title from Z to A.</summary>
    TitleDescending,

    /// <summary>Category name from A to Z, with the uncategorized last.</summary>
    Category,

    /// <summary>Most recently changed first.</summary>
    RecentlyUpdated,
}

/// <summary>Fields accepted when creating a task.</summary>
/// <param name="Title">Short description of the work; required.</param>
/// <param name="Notes">Free-form detail, held as markdown.</param>
/// <param name="Status">State to start in; defaults to <see cref="TaskItemStatus.ToDo"/>.</param>
/// <param name="CategoryId">Category to file it under, or null.</param>
/// <param name="DueDate">Day it is due, or null.</param>
public record CreateTaskRequest(
    string Title,
    string? Notes = null,
    TaskItemStatus Status = TaskItemStatus.ToDo,
    int? CategoryId = null,
    DateOnly? DueDate = null);

/// <summary>Fields accepted when updating a task. A null member leaves that field alone.</summary>
/// <param name="Title">New title.</param>
/// <param name="Notes">New notes; pass an empty string to clear them.</param>
/// <param name="Status">New state.</param>
/// <param name="CategoryId">New category.</param>
/// <param name="ClearCategory">When true, removes the category.</param>
/// <param name="DueDate">New due date.</param>
/// <param name="ClearDueDate">When true, removes the due date.</param>
public record UpdateTaskRequest(
    string? Title = null,
    string? Notes = null,
    TaskItemStatus? Status = null,
    int? CategoryId = null,
    bool ClearCategory = false,
    DateOnly? DueDate = null,
    bool ClearDueDate = false);
