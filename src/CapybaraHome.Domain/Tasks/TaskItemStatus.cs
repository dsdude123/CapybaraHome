namespace CapybaraHome.Domain.Tasks;

/// <summary>The state a task is in. Persisted by name so the database stays readable.</summary>
public enum TaskItemStatus
{
    ToDo,
    InProgress,
    OnHold,
    Done,
}
