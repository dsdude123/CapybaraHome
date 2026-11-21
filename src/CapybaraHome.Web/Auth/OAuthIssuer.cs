using CapybaraHome.Application.Users;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Web.Auth;

/// <summary>
/// The address this installation is reached at, which every OAuth document has to agree on.
/// A connector compares the issuer in the metadata against the URL it fetched it from, so this
/// follows the request — the forwarded headers have already been applied by the time it is read
/// — unless <c>OAuth:Issuer</c> names it outright, which is what a proxy that rewrites the path
/// or serves a different host needs.
/// </summary>
public class OAuthIssuer(IOptions<OAuthOptions> options)
{
    /// <summary>Where the protected resource metadata is served from.</summary>
    public const string ResourceMetadataPath = "/.well-known/oauth-protected-resource";

    /// <summary>Where the authorization server metadata is served from.</summary>
    public const string AuthorizationServerMetadataPath = "/.well-known/oauth-authorization-server";

    private readonly string? configured = options.Value.Issuer?.TrimEnd('/');

    private readonly string? authorizationEndpoint = options.Value.AuthorizationEndpoint?.Trim();

    /// <summary>The issuer identifier, with no trailing slash.</summary>
    /// <param name="request">The request the address is read off.</param>
    public string For(HttpRequest request) =>
        string.IsNullOrWhiteSpace(configured)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/')
            : configured;

    /// <summary>The MCP endpoint as a resource identifier.</summary>
    /// <param name="request">The request the address is read off.</param>
    public string ResourceFor(HttpRequest request) => $"{For(request)}/mcp";

    /// <summary>Where a client is told to read the protected resource metadata.</summary>
    /// <param name="request">The request the address is read off.</param>
    public string ResourceMetadataFor(HttpRequest request) =>
        $"{For(request)}{ResourceMetadataPath}/mcp";

    /// <summary>
    /// Whether a resource indicator names this installation's MCP endpoint. A request that
    /// carries none is accepted, since not every client sends one, but one that names somewhere
    /// else is refused rather than silently granted against this installation.
    /// </summary>
    /// <param name="request">The request the address is read off.</param>
    /// <param name="resource">The resource indicator the client asked for, or null.</param>
    public bool IsOwnResource(HttpRequest request, string? resource) =>
        string.IsNullOrWhiteSpace(resource)
        || string.Equals(resource.TrimEnd('/'), ResourceFor(request).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the browser is sent to approve a client: <c>OAuth:AuthorizationEndpoint</c> when it
    /// is set, so the consent page can live on an address that does not face the internet, and
    /// the issuer's own <c>/oauth/authorize</c> otherwise.
    /// </summary>
    /// <param name="request">The request the address is read off.</param>
    public string AuthorizationEndpointFor(HttpRequest request) =>
        string.IsNullOrWhiteSpace(authorizationEndpoint)
            ? $"{For(request)}/oauth/authorize"
            : authorizationEndpoint;

    /// <summary>What <c>/.well-known/oauth-protected-resource</c> answers.</summary>
    /// <param name="request">The request the address is read off.</param>
    public object ResourceMetadata(HttpRequest request) => new
    {
        resource = ResourceFor(request),
        authorization_servers = new[] { For(request) },
        scopes_supported = new[] { OAuthService.FullScope },
        bearer_methods_supported = new[] { "header" },
        resource_name = "CapybaraHome",
    };

    /// <summary>What <c>/.well-known/oauth-authorization-server</c> answers.</summary>
    /// <param name="request">The request the address is read off.</param>
    public object AuthorizationServerMetadata(HttpRequest request)
    {
        var issuer = For(request);
        return new
        {
            issuer,
            authorization_endpoint = AuthorizationEndpointFor(request),
            token_endpoint = $"{issuer}/oauth/token",
            registration_endpoint = $"{issuer}/oauth/register",
            revocation_endpoint = $"{issuer}/oauth/revoke",
            scopes_supported = new[] { OAuthService.FullScope },
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { OAuthService.ChallengeMethod },
            token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post", "client_secret_basic" },
            revocation_endpoint_auth_methods_supported = new[] { "none", "client_secret_post", "client_secret_basic" },
        };
    }
}
