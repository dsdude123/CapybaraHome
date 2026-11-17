using System.Security.Claims;
using System.Text.Encodings.Web;
using CapybaraHome.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Web.Auth;

/// <summary>
/// Signs in a request that carries <c>Authorization: Bearer &lt;token&gt;</c>, which is how a
/// client reaches the API and the MCP endpoint without a browser. The token is either one minted
/// by hand on the settings screen or one an OAuth client was issued, told apart by its prefix.
/// </summary>
public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens,
    OAuthService oauth,
    OAuthIssuer issuer) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Name of the scheme this handler is registered under.</summary>
    public const string SchemeName = "ApiToken";

    /// <summary>Reads the bearer token and resolves it to a user.</summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var secret = header["Bearer ".Length..].Trim();
        var user = secret.StartsWith(OAuthService.AccessTokenPrefix, StringComparison.Ordinal)
            ? await oauth.AuthenticateAccessTokenAsync(secret, Context.RequestAborted)
            : await tokens.AuthenticateAsync(secret, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("The bearer token is not valid.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(CapybaraClaims.Username, user.Username),
                new Claim(CapybaraClaims.UserId, user.Id.ToString()),
                .. user.IsFirstUser ? new[] { new Claim(CapybaraClaims.FirstUser, "true") } : [],
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>
    /// Answers an unauthenticated call with the pointer to the protected resource metadata that
    /// RFC 9728 asks for, which is how a connector finds the authorization server to sign in at.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            $"Bearer realm=\"CapybaraHome\", resource_metadata=\"{issuer.ResourceMetadataFor(Request)}\"";
        return Task.CompletedTask;
    }
}
