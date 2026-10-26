namespace CapybaraHome.Domain.Tasks;

/// <summary>What an operation does when its task is completed.</summary>
public enum TaskCloseOperationKind
{
    /// <summary>Calls a URL.</summary>
    Webhook,

    /// <summary>Creates another task.</summary>
    CreateTask,

    /// <summary>Produces a document to print.</summary>
    PrintDocument,
}
