namespace CapybaraHome.Web.Auth;

/// <summary>
/// OpenID Connect settings, bound from the <c>Authentication:Oidc</c> configuration section.
/// The repository is public, so the client secret only ever comes from the environment.
/// </summary>
public class OidcOptions
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "Authentication:Oidc";

    /// <summary>Provider's issuer URL, from which the discovery document is read.</summary>
    public string? Authority { get; set; }

    /// <summary>Client id this installation is registered under.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret. Supply it as an environment variable, never in a file in the repository.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Scopes to request on top of <c>openid</c>, <c>profile</c> and <c>email</c>.</summary>
    public IList<string> AdditionalScopes { get; set; } = [];

    /// <summary>True once enough is configured to talk to a provider.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
