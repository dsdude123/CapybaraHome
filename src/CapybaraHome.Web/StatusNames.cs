using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Web;

/// <summary>Turns the task statuses into the words the UI shows.</summary>
public static class StatusNames
{
    /// <summary>Every status, in the order the board shows them.</summary>
    public static readonly TaskItemStatus[] All =
    [
        TaskItemStatus.ToDo,
        TaskItemStatus.InProgress,
        TaskItemStatus.OnHold,
        TaskItemStatus.Done,
    ];

    /// <summary>Returns the label for <paramref name="status"/>.</summary>
    public static string Label(TaskItemStatus status) => status switch
    {
        TaskItemStatus.ToDo => "To do",
        TaskItemStatus.InProgress => "In progress",
        TaskItemStatus.OnHold => "On hold",
        TaskItemStatus.Done => "Done",
        _ => status.ToString(),
    };
}
