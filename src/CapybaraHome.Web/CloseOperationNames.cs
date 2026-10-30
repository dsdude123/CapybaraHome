using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Web;

/// <summary>Turns the close operations and their settings into the words the UI shows.</summary>
public static class CloseOperationNames
{
    /// <summary>Every kind of operation, in the order the chooser offers them.</summary>
    public static readonly TaskCloseOperationKind[] AllKinds =
    [
        TaskCloseOperationKind.CreateTask,
        TaskCloseOperationKind.Webhook,
    ];

    /// <summary>Every way a due date can be worked out, in the order the picker offers them.</summary>
    public static readonly TaskDueDateMode[] AllDueDateModes =
    [
        TaskDueDateMode.None,
        TaskDueDateMode.OnDate,
        TaskDueDateMode.InDays,
        TaskDueDateMode.InWeeks,
        TaskDueDateMode.InMonths,
        TaskDueDateMode.InYears,
        TaskDueDateMode.NextQuarter,
        TaskDueDateMode.EndOfQuarter,
    ];

    /// <summary>Every method a webhook can be sent with.</summary>
    public static readonly TaskWebhookMethod[] AllMethods = [TaskWebhookMethod.Post, TaskWebhookMethod.Get];

    /// <summary>Returns the label for <paramref name="kind"/>.</summary>
    /// <param name="kind">Kind to label.</param>
    public static string Label(TaskCloseOperationKind kind) => kind switch
    {
        TaskCloseOperationKind.Webhook => "Call a webhook",
        TaskCloseOperationKind.CreateTask => "Create another task",
        _ => kind.ToString(),
    };

    /// <summary>Returns the one-line description of <paramref name="kind"/> the chooser shows.</summary>
    /// <param name="kind">Kind to describe.</param>
    public static string Description(TaskCloseOperationKind kind) => kind switch
    {
        TaskCloseOperationKind.Webhook => "Sends the completed task to a URL on your network or on the web.",
        TaskCloseOperationKind.CreateTask => "Adds the task again with a due date worked out from the day you finished it.",
        _ => string.Empty,
    };

    /// <summary>Returns the label for <paramref name="mode"/>.</summary>
    /// <param name="mode">Mode to label.</param>
    public static string Label(TaskDueDateMode mode) => mode switch
    {
        TaskDueDateMode.None => "No due date",
        TaskDueDateMode.OnDate => "On a date I pick",
        TaskDueDateMode.InDays => "A number of days after it is completed",
        TaskDueDateMode.InWeeks => "A number of weeks after it is completed",
        TaskDueDateMode.InMonths => "A number of months after it is completed",
        TaskDueDateMode.InYears => "A number of years after it is completed",
        TaskDueDateMode.NextQuarter => "The start of the quarter after it is completed",
        TaskDueDateMode.EndOfQuarter => "The end of the quarter it is completed in",
        _ => mode.ToString(),
    };

    /// <summary>Returns the label for <paramref name="method"/>.</summary>
    /// <param name="method">Method to label.</param>
    public static string Label(TaskWebhookMethod method) => method switch
    {
        TaskWebhookMethod.Post => "POST the completed task as JSON",
        TaskWebhookMethod.Get => "GET the URL, with no body",
        _ => method.ToString(),
    };

    /// <summary>Returns how a due date rule reads in a line of prose.</summary>
    /// <param name="mode">How the due date is worked out.</param>
    /// <param name="value">Number of days, weeks, months or years, when the mode counts them.</param>
    /// <param name="onDate">Day picked by hand, when the mode uses one.</param>
    public static string DueDateSummary(TaskDueDateMode mode, int? value, DateOnly? onDate) => mode switch
    {
        TaskDueDateMode.None => "no due date",
        TaskDueDateMode.OnDate => onDate is { } date ? $"due {DateNames.Long(date)}" : "no date picked yet",
        TaskDueDateMode.NextQuarter => "due at the start of the next quarter",
        TaskDueDateMode.EndOfQuarter => "due at the end of the quarter it is completed in",
        _ => $"due {value} {Application.Tasks.TaskDueDate.UnitName(mode)} after it is completed",
    };

    /// <summary>Returns the label for <paramref name="status"/>.</summary>
    /// <param name="status">Status to label.</param>
    public static string Label(TaskCloseRunStatus status) => status switch
    {
        TaskCloseRunStatus.Pending => "Waiting to be sent",
        TaskCloseRunStatus.Succeeded => "Done",
        TaskCloseRunStatus.Failed => "Failed",
        _ => status.ToString(),
    };
}
