using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CapybaraHome.Application.Users;
using CapybaraHome.Web.Auth;
using CapybaraHome.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace CapybaraHome.Web.Endpoints;

/// <summary>
/// The machine half of the OAuth flow: the two discovery documents a connector reads before it
/// can start, the registration endpoint it takes a client id from, and the token endpoint it
/// trades an authorization code at. The human half is the consent page at <c>/oauth/authorize</c>.
/// </summary>
public static class OAuthEndpoints
{
    /// <summary>Maps the discovery documents, registration, token and revocation endpoints.</summary>
    /// <param name="app">The routes the endpoints are added to.</param>
    public static void MapOAuth(this IEndpointRouteBuilder app)
    {
        foreach (var path in new[] { OAuthIssuer.ResourceMetadataPath, $"{OAuthIssuer.ResourceMetadataPath}/mcp" })
        {
            app.MapGet(path, (HttpContext http, OAuthIssuer issuer) =>
            {
                // The documents are public and carry nothing about the caller, so a browser-based
                // client may read them cross-origin; without this its discovery step fails.
                http.Response.Headers["Access-Control-Allow-Origin"] = "*";
                return Results.Json(issuer.ResourceMetadata(http.Request));
            }).AllowAnonymous();
        }

        app.MapGet(OAuthIssuer.AuthorizationServerMetadataPath, (HttpContext http, OAuthIssuer issuer) =>
        {
            http.Response.Headers["Access-Control-Allow-Origin"] = "*";
            return Results.Json(issuer.AuthorizationServerMetadata(http.Request));
        }).AllowAnonymous();

        app.MapMethods(
            $"{OAuthIssuer.ResourceMetadataPath}/{{**rest}}",
            ["OPTIONS"],
            (HttpContext http) => Preflight(http)).AllowAnonymous();

        app.MapMethods(OAuthIssuer.AuthorizationServerMetadataPath, ["OPTIONS"], (HttpContext http) => Preflight(http))
            .AllowAnonymous();

        app.MapPost("/oauth/register", async (HttpContext http, OAuthService oauth, CancellationToken cancellationToken) =>
        {
            JsonElement body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<JsonElement>(http.Request.Body, cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                return Failure("invalid_client_metadata", "The registration body is not JSON.");
            }

            if (body.ValueKind != JsonValueKind.Object)
            {
                return Failure("invalid_client_metadata", "The registration body is not a JSON object.");
            }

            try
            {
                Reject(body, "grant_types", ["authorization_code", "refresh_token"]);
                Reject(body, "response_types", ["code"]);

                var registered = await oauth.RegisterAsync(
                    new OAuthClientRegistration(
                        Text(body, "client_name"),
                        Strings(body, "redirect_uris"),
                        Text(body, "token_endpoint_auth_method")),
                    cancellationToken);

                return Results.Json(
                    new Dictionary<string, object?>
                    {
                        ["client_id"] = registered.ClientId,
                        ["client_secret"] = registered.ClientSecret,
                        ["client_secret_expires_at"] = registered.ClientSecret is null ? null : 0,
                        ["client_id_issued_at"] = registered.CreatedAt.ToUnixTimeSeconds(),
                        ["client_name"] = registered.ClientName,
                        ["redirect_uris"] = registered.RedirectUris,
                        ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                        ["response_types"] = new[] { "code" },
                        ["token_endpoint_auth_method"] = registered.TokenEndpointAuthMethod,
                        ["scope"] = OAuthService.FullScope,
                    },
                    statusCode: StatusCodes.Status201Created);
            }
            catch (OAuthException ex)
            {
                return Failure(ex.Error, ex.Message);
            }
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.ApiPolicy);

        app.MapPost("/oauth/token", async (HttpContext http, OAuthService oauth, CancellationToken cancellationToken) =>
        {
            if (!http.Request.HasFormContentType)
            {
                return Failure("invalid_request", "The token request has to be form encoded.");
            }

            var form = await http.Request.ReadFormAsync(cancellationToken);
            var (basicId, basicSecret) = BasicCredentials(http.Request);
            var clientId = Field(form, "client_id") ?? basicId;
            var clientSecret = Field(form, "client_secret") ?? basicSecret;
            var grant = Field(form, "grant_type");

            try
            {
                var tokens = grant switch
                {
                    "authorization_code" => await oauth.RedeemCodeAsync(
                        Field(form, "code"),
                        clientId,
                        Field(form, "redirect_uri"),
                        Field(form, "code_verifier"),
                        clientSecret,
                        cancellationToken),
                    "refresh_token" => await oauth.RefreshAsync(
                        Field(form, "refresh_token"),
                        clientId,
                        clientSecret,
                        cancellationToken),
                    _ => throw new OAuthException(
                        "unsupported_grant_type",
                        $"{grant ?? "(none)"} is not a grant type this server offers."),
                };

                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(new Dictionary<string, object?>
                {
                    ["access_token"] = tokens.AccessToken,
                    ["token_type"] = "Bearer",
                    ["expires_in"] = tokens.ExpiresInSeconds,
                    ["refresh_token"] = tokens.RefreshToken,
                    ["scope"] = tokens.Scope ?? OAuthService.FullScope,
                });
            }
            catch (OAuthException ex)
            {
                return Failure(
                    ex.Error,
                    ex.Message,
                    ex.Error == "invalid_client" ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest);
            }
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.ApiPolicy);

        app.MapPost("/oauth/revoke", async (HttpContext http, OAuthService oauth, CancellationToken cancellationToken) =>
        {
            if (!http.Request.HasFormContentType)
            {
                return Failure("invalid_request", "The revocation request has to be form encoded.");
            }

            var form = await http.Request.ReadFormAsync(cancellationToken);

            // RFC 7009 wants a 200 whether or not the token was known, so a caller cannot use
            // this endpoint to find out which tokens exist.
            await oauth.RevokeTokenAsync(Field(form, "token"), cancellationToken);
            return Results.Ok();
        })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.ApiPolicy);

        // What the consent page's two buttons post. It runs under the sign-in cookie, so an
        // approval can only come from a person who has just been through this installation's own
        // username, password and authenticator code.
        app.MapPost("/oauth/authorize/decide", async (
            HttpContext http,
            [FromForm] string? decision,
            [FromForm] string? clientId,
            [FromForm] string? redirectUri,
            [FromForm] string? codeChallenge,
            [FromForm] string? state,
            [FromForm] string? scope,
            [FromForm] string? resource,
            OAuthService oauth,
            CancellationToken cancellationToken) =>
        {
            // Checked again here rather than trusted from the form, so a posted redirect URI can
            // never send a code anywhere the client did not register.
            if (!await oauth.IsRegisteredRedirectUriAsync(clientId, redirectUri, cancellationToken))
            {
                return Results.BadRequest("That client and redirect URI are not registered here.");
            }

            if (!string.Equals(decision, "approve", StringComparison.Ordinal))
            {
                return Results.Redirect(OAuthRedirect.Error(
                    redirectUri!,
                    "access_denied",
                    "The request was denied here.",
                    state));
            }

            if (!int.TryParse(http.User.FindFirst(CapybaraClaims.UserId)?.Value, out var userId))
            {
                return Results.Redirect(OAuthRedirect.Error(
                    redirectUri!,
                    "access_denied",
                    "Sign in again before authorizing a client.",
                    state));
            }

            try
            {
                var code = await oauth.IssueCodeAsync(
                    clientId,
                    userId,
                    redirectUri!,
                    codeChallenge ?? string.Empty,
                    string.IsNullOrWhiteSpace(scope) ? null : scope,
                    string.IsNullOrWhiteSpace(resource) ? null : resource,
                    cancellationToken);

                return Results.Redirect(OAuthRedirect.Code(redirectUri!, code, state));
            }
            catch (OAuthException ex)
            {
                return Results.Redirect(OAuthRedirect.Error(redirectUri!, ex.Error, ex.Message, state));
            }
        });
    }

    private static IResult Preflight(HttpContext http)
    {
        http.Response.Headers["Access-Control-Allow-Origin"] = "*";
        http.Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
        http.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
        return Results.NoContent();
    }

    private static IResult Failure(string error, string description, int statusCode = StatusCodes.Status400BadRequest) =>
        Results.Json(new { error, error_description = description }, statusCode: statusCode);

    private static void Reject(JsonElement body, string name, string[] offered)
    {
        foreach (var asked in Strings(body, name))
        {
            if (!offered.Contains(asked, StringComparer.Ordinal))
            {
                throw new OAuthException(
                    "invalid_client_metadata",
                    $"{asked} is not a {name.Replace('_', ' ')} value this server offers.");
            }
        }
    }

    private static string? Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> Strings(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!),
        ];
    }

    private static string? Field(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static (string? ClientId, string? Secret) BasicCredentials(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return (null, null);
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return (null, null);
        }

        var separator = decoded.IndexOf(':');
        return separator < 0
            ? (null, null)
            : (Uri.UnescapeDataString(decoded[..separator]), Uri.UnescapeDataString(decoded[(separator + 1)..]));
    }
}
