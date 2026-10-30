using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Tasks;

/// <summary>An operation as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="TaskId">Task it belongs to.</param>
/// <param name="Kind">What it does.</param>
/// <param name="SortOrder">Position in the task's list.</param>
/// <param name="IsEnabled">Whether it runs at all.</param>
/// <param name="WebhookUrl">URL a webhook operation calls.</param>
/// <param name="WebhookMethod">Method a webhook operation is sent with.</param>
/// <param name="FollowUpTitle">Title the created task is given, or null to reuse the completed task's.</param>
/// <param name="DueDateMode">How the created task's due date is worked out.</param>
/// <param name="DueDateValue">How many days, weeks, months or years later it is due.</param>
/// <param name="DueDateOn">Day it is due when that is picked by hand.</param>
/// <param name="CarryOperationsForward">Whether the created task gets a copy of these operations.</param>
/// <param name="LastRun">The most recent run of this operation, or null while it has never run.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record TaskCloseOperationView(
    int Id,
    int TaskId,
    TaskCloseOperationKind Kind,
    int SortOrder,
    bool IsEnabled,
    string? WebhookUrl,
    TaskWebhookMethod WebhookMethod,
    string? FollowUpTitle,
    TaskDueDateMode DueDateMode,
    int? DueDateValue,
    DateOnly? DueDateOn,
    bool CarryOperationsForward,
    TaskCloseRunView? LastRun,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One completion's attempt at one operation, as the UI shows it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="OperationId">Operation that ran, or null once it has been deleted.</param>
/// <param name="TaskId">Task that was completed, or null once it has been deleted.</param>
/// <param name="TaskTitle">Title that task had.</param>
/// <param name="Kind">What the operation does.</param>
/// <param name="Status">Where the run stands.</param>
/// <param name="Attempts">How many times it has been attempted.</param>
/// <param name="LastAttemptAt">When it was last attempted, or null.</param>
/// <param name="Detail">What happened.</param>
/// <param name="CreatedTaskId">Task it created, or null.</param>
/// <param name="CompletedAt">When the task was completed.</param>
/// <param name="CreatedAt">When the run was queued.</param>
public record TaskCloseRunView(
    int Id,
    int? OperationId,
    int? TaskId,
    string TaskTitle,
    TaskCloseOperationKind Kind,
    TaskCloseRunStatus Status,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    string? Detail,
    int? CreatedTaskId,
    DateTimeOffset CompletedAt,
    DateTimeOffset CreatedAt);

/// <summary>Fields accepted when adding an operation to a task.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="WebhookUrl">URL to call, for a webhook.</param>
/// <param name="WebhookMethod">Method to send it with.</param>
/// <param name="FollowUpTitle">Title to give the created task, or null to reuse the completed task's.</param>
/// <param name="DueDateMode">How to work out the created task's due date.</param>
/// <param name="DueDateValue">How many days, weeks, months or years later it is due.</param>
/// <param name="DueDateOn">Day it is due when that is picked by hand.</param>
/// <param name="CarryOperationsForward">Whether the created task gets a copy of these operations.</param>
/// <param name="IsEnabled">Whether it runs at all; true when left out.</param>
public record CreateTaskCloseOperationRequest(
    TaskCloseOperationKind Kind,
    string? WebhookUrl = null,
    TaskWebhookMethod WebhookMethod = TaskWebhookMethod.Post,
    string? FollowUpTitle = null,
    TaskDueDateMode DueDateMode = TaskDueDateMode.None,
    int? DueDateValue = null,
    DateOnly? DueDateOn = null,
    bool CarryOperationsForward = true,
    bool IsEnabled = true);

/// <summary>Fields accepted when changing an operation. A null member leaves that field alone.</summary>
/// <param name="Kind">New kind.</param>
/// <param name="WebhookUrl">New URL.</param>
/// <param name="WebhookMethod">New method.</param>
/// <param name="FollowUpTitle">New title for the created task; pass an empty string to reuse the completed task's.</param>
/// <param name="DueDateMode">New due date mode.</param>
/// <param name="DueDateValue">New number of days, weeks, months or years.</param>
/// <param name="DueDateOn">New day picked by hand.</param>
/// <param name="CarryOperationsForward">New setting for copying the operations forward.</param>
/// <param name="IsEnabled">New setting for whether it runs.</param>
public record UpdateTaskCloseOperationRequest(
    TaskCloseOperationKind? Kind = null,
    string? WebhookUrl = null,
    TaskWebhookMethod? WebhookMethod = null,
    string? FollowUpTitle = null,
    TaskDueDateMode? DueDateMode = null,
    int? DueDateValue = null,
    DateOnly? DueDateOn = null,
    bool? CarryOperationsForward = null,
    bool? IsEnabled = null);

/// <summary>What queueing one completion's operations did.</summary>
/// <param name="Queued">Operations that were queued.</param>
/// <param name="TasksCreated">Tasks created straight away.</param>
/// <param name="WebhooksPending">Webhook calls left for the sweep to send.</param>
public record TaskCloseRunSummary(int Queued, int TasksCreated, int WebhooksPending);

/// <summary>What one sweep of the pending webhook calls did.</summary>
/// <param name="Attempted">Calls that were tried.</param>
/// <param name="Succeeded">Calls the target accepted.</param>
/// <param name="Failed">Calls that have now been given up on.</param>
public record TaskCloseWebhookSweepSummary(int Attempted, int Succeeded, int Failed);
