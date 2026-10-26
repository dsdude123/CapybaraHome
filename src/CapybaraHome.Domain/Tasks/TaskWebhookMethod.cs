namespace CapybaraHome.Domain.Tasks;

/// <summary>The HTTP methods a webhook operation can be sent with.</summary>
public enum TaskWebhookMethod
{
    /// <summary>Posts the completed task as JSON.</summary>
    Post,

    /// <summary>Fetches the URL with no body.</summary>
    Get,
}
