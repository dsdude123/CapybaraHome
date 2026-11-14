namespace CapybaraHome.Domain.Tasks;

/// <summary>Where one attempt at an operation stands.</summary>
public enum TaskCloseRunStatus
{
    /// <summary>Queued, and not sent yet.</summary>
    Pending,

    /// <summary>Done.</summary>
    Succeeded,

    /// <summary>Given up on.</summary>
    Failed,
}
