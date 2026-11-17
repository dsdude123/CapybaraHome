namespace CapybaraHome.Domain.Users;

/// <summary>What an issued OAuth token is for.</summary>
public enum OAuthTokenKind
{
    /// <summary>A short-lived token that reaches the API and the MCP endpoint.</summary>
    Access = 0,

    /// <summary>A long-lived token that buys a new access token without the person present.</summary>
    Refresh = 1,
}
