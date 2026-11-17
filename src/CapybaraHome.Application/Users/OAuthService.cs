using System.Security.Cryptography;
using System.Text;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Users;

/// <summary>
/// The authorization server behind the connector sign-in: it registers clients, writes the
/// authorization codes the consent screen hands out, and trades them for access and refresh
/// tokens. Every secret is kept only as a SHA-256 hash, as the hand-minted tokens are.
/// </summary>
public class OAuthService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>Prefix that tells an access token apart from a hand-minted bearer token.</summary>
    public const string AccessTokenPrefix = "capy_at_";

    /// <summary>Prefix on a refresh token.</summary>
    public const string RefreshTokenPrefix = "capy_rt_";

    /// <summary>The only code challenge method OAuth 2.1 allows.</summary>
    public const string ChallengeMethod = "S256";

    /// <summary>The scope this installation grants, which is everything the account can reach.</summary>
    public const string FullScope = "capybarahome";

    private const int SecretBytes = 32;
    private const int MaxRedirectUris = 5;
    private const int MaxDynamicClients = 50;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(60);
    private static readonly TimeSpan AbandonedRegistrationLifetime = TimeSpan.FromDays(1);

    /// <summary>Registers a client that asked for a client id of its own.</summary>
    /// <exception cref="OAuthException">The registration is not one this server accepts.</exception>
    public async Task<RegisteredOAuthClient> RegisterAsync(
        OAuthClientRegistration registration,
        CancellationToken cancellationToken = default)
    {
        var uris = NormalizeRedirectUris(registration.RedirectUris);
        var method = NormalizeAuthMethod(registration.TokenEndpointAuthMethod);
        var name = (registration.ClientName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = "Unnamed client";
        }
        else if (name.Length > 200)
        {
            name = name[..200];
        }

        await PruneAsync(cancellationToken);

        var live = await context.OAuthClients
            .CountAsync(c => c.RegisteredDynamically && c.RevokedAt == null, cancellationToken);
        if (live >= MaxDynamicClients)
        {
            throw new OAuthException(
                "invalid_client_metadata",
                $"This installation already holds {MaxDynamicClients} registered clients. Revoke one in settings first.");
        }

        var secret = method == "none" ? null : NewSecret(string.Empty);
        var client = new OAuthClient
        {
            ClientId = NewSecret(string.Empty),
            ClientName = name,
            SecretHash = secret is null ? null : Hash(secret),
            RedirectUris = string.Join('\n', uris),
            TokenEndpointAuthMethod = method,
            RegisteredDynamically = true,
            CreatedAt = clock.GetUtcNow(),
        };

        context.OAuthClients.Add(client);
        await context.SaveChangesAsync(cancellationToken);

        return new RegisteredOAuthClient(client.ClientId, secret, client.ClientName, uris, method, client.CreatedAt);
    }

    /// <summary>Returns the client the consent screen is about, or null when it is unknown or revoked.</summary>
    public async Task<OAuthConsentClient?> FindClientAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        var client = await LiveClientAsync(clientId, cancellationToken);
        return client is null ? null : new OAuthConsentClient(client.Id, client.ClientId, client.ClientName);
    }

    /// <summary>Whether <paramref name="redirectUri"/> is one the client registered, compared whole.</summary>
    public async Task<bool> IsRegisteredRedirectUriAsync(
        string? clientId,
        string? redirectUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return false;
        }

        var client = await LiveClientAsync(clientId, cancellationToken);
        return client is not null && RedirectUrisOf(client).Contains(redirectUri, StringComparer.Ordinal);
    }

    /// <summary>Writes an authorization code for an approval the person just gave.</summary>
    /// <exception cref="OAuthException">The request does not name a usable client or redirect URI.</exception>
    public async Task<string> IssueCodeAsync(
        string? clientId,
        int userId,
        string redirectUri,
        string codeChallenge,
        string? scope,
        string? resource,
        CancellationToken cancellationToken = default)
    {
        var client = await LiveClientAsync(clientId, cancellationToken)
            ?? throw new OAuthException("invalid_client", "That client is not registered here.");

        if (!RedirectUrisOf(client).Contains(redirectUri, StringComparer.Ordinal))
        {
            throw new OAuthException("invalid_request", "That redirect URI is not registered for this client.");
        }

        if (string.IsNullOrWhiteSpace(codeChallenge))
        {
            throw new OAuthException("invalid_request", "The authorize request carried no code challenge.");
        }

        var now = clock.GetUtcNow();
        var code = NewSecret(string.Empty);

        context.OAuthAuthorizationCodes.Add(new OAuthAuthorizationCode
        {
            CodeHash = Hash(code),
            ClientId = client.Id,
            UserId = userId,
            RedirectUri = redirectUri,
            CodeChallenge = codeChallenge,
            Scope = scope,
            Resource = resource,
            CreatedAt = now,
            ExpiresAt = now + CodeLifetime,
        });

        await context.SaveChangesAsync(cancellationToken);
        return code;
    }

    /// <summary>Trades an authorization code for tokens, checking the PKCE verifier against it.</summary>
    /// <exception cref="OAuthException">The code, the client or the verifier does not hold up.</exception>
    public async Task<IssuedOAuthTokens> RedeemCodeAsync(
        string? code,
        string? clientId,
        string? redirectUri,
        string? codeVerifier,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        var client = await AuthenticateClientAsync(clientId, clientSecret, cancellationToken);

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new OAuthException("invalid_request", "The request carried no authorization code.");
        }

        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            throw new OAuthException("invalid_request", "The request carried no code verifier.");
        }

        var hash = Hash(code);
        var stored = await context.OAuthAuthorizationCodes.SingleOrDefaultAsync(c => c.CodeHash == hash, cancellationToken);
        var now = clock.GetUtcNow();

        if (stored is null || stored.ClientId != client.Id)
        {
            throw new OAuthException("invalid_grant", "That authorization code is not one this server issued.");
        }

        // A code offered twice is a sign the first use leaked, so everything that use produced
        // goes with it rather than only the replay being refused.
        if (stored.RedeemedAt is not null)
        {
            await RevokeTokensOfAsync(stored.ClientId, stored.UserId, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            throw new OAuthException("invalid_grant", "That authorization code has already been used.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new OAuthException("invalid_grant", "That authorization code has expired.");
        }

        if (!string.Equals(stored.RedirectUri, redirectUri, StringComparison.Ordinal))
        {
            throw new OAuthException("invalid_grant", "That redirect URI is not the one the code was issued for.");
        }

        if (!ChallengeMatches(stored.CodeChallenge, codeVerifier))
        {
            throw new OAuthException("invalid_grant", "The code verifier does not match the challenge.");
        }

        stored.RedeemedAt = now;
        return await IssueTokensAsync(client, stored.UserId, stored.Scope, now, cancellationToken);
    }

    /// <summary>Trades a refresh token for a new pair, retiring the one presented.</summary>
    /// <exception cref="OAuthException">The refresh token or the client does not hold up.</exception>
    public async Task<IssuedOAuthTokens> RefreshAsync(
        string? refreshToken,
        string? clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        var client = await AuthenticateClientAsync(clientId, clientSecret, cancellationToken);

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new OAuthException("invalid_request", "The request carried no refresh token.");
        }

        var hash = Hash(refreshToken);
        var stored = await context.OAuthTokens
            .SingleOrDefaultAsync(t => t.SecretHash == hash && t.Kind == OAuthTokenKind.Refresh, cancellationToken);
        var now = clock.GetUtcNow();

        if (stored is null || stored.ClientId != client.Id)
        {
            throw new OAuthException("invalid_grant", "That refresh token is not one this server issued.");
        }

        if (stored.RevokedAt is not null)
        {
            await RevokeTokensOfAsync(stored.ClientId, stored.UserId, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            throw new OAuthException("invalid_grant", "That refresh token has already been used or revoked.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new OAuthException("invalid_grant", "That refresh token has expired.");
        }

        stored.RevokedAt = now;
        stored.LastUsedAt = now;
        return await IssueTokensAsync(client, stored.UserId, stored.Scope, now, cancellationToken);
    }

    /// <summary>Returns the user an access token acts as, or null when it is unknown or spent.</summary>
    public async Task<UserView?> AuthenticateAccessTokenAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || !secret.StartsWith(AccessTokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Hash(secret);
        var token = await context.OAuthTokens
            .SingleOrDefaultAsync(t => t.SecretHash == hash && t.Kind == OAuthTokenKind.Access, cancellationToken);

        var now = clock.GetUtcNow();
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now)
        {
            return null;
        }

        var client = await context.OAuthClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == token.ClientId, cancellationToken);
        if (client is null || client.RevokedAt is not null)
        {
            return null;
        }

        token.LastUsedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        return user is null ? null : ViewOf(user);
    }

    /// <summary>Revokes a presented access or refresh token. Returns false when it is unknown.</summary>
    public async Task<bool> RevokeTokenAsync(string? secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var hash = Hash(secret);
        var token = await context.OAuthTokens.SingleOrDefaultAsync(t => t.SecretHash == hash, cancellationToken);
        if (token is null)
        {
            return false;
        }

        var now = clock.GetUtcNow();

        // A refresh token names a whole grant, so retiring it takes the access tokens it bought.
        if (token.Kind == OAuthTokenKind.Refresh)
        {
            await RevokeTokensOfAsync(token.ClientId, token.UserId, now, cancellationToken);
        }
        else if (token.RevokedAt is null)
        {
            token.RevokedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns every client, newest first, with how many of its tokens are still live.</summary>
    public async Task<IReadOnlyList<OAuthClientView>> ListClientsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var clients = await context.OAuthClients.AsNoTracking().OrderByDescending(c => c.Id).ToListAsync(cancellationToken);

        // SQLite cannot compare a DateTimeOffset in SQL, so the unrevoked tokens come back and
        // the expiry is settled here.
        var unrevoked = await context.OAuthTokens.AsNoTracking()
            .Where(t => t.RevokedAt == null)
            .Select(t => new { t.ClientId, t.ExpiresAt })
            .ToListAsync(cancellationToken);

        var live = unrevoked
            .Where(t => t.ExpiresAt > now)
            .GroupBy(t => t.ClientId)
            .ToDictionary(g => g.Key, g => g.Count());

        return
        [
            .. clients.Select(c => new OAuthClientView(
                c.Id,
                c.ClientId,
                c.ClientName,
                RedirectUrisOf(c),
                c.RegisteredDynamically,
                c.CreatedAt,
                c.LastUsedAt,
                c.RevokedAt,
                live.GetValueOrDefault(c.Id))),
        ];
    }

    /// <summary>Revokes a client and every token it holds. Returns false when no live client has that id.</summary>
    public async Task<bool> RevokeClientAsync(int id, CancellationToken cancellationToken = default)
    {
        var client = await context.OAuthClients.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (client is null || client.RevokedAt is not null)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        client.RevokedAt = now;

        var tokens = await context.OAuthTokens.Where(t => t.ClientId == id && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Clears spent codes and tokens and the registrations nobody ever approved, so the tables do
    /// not grow without bound and the cap on registered clients cannot be filled up by a caller
    /// that registers and then walks away.
    /// </summary>
    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - RefreshTokenLifetime;

        // SQLite cannot compare a DateTimeOffset in SQL, so the rows come back and the cutoff is
        // settled here. Both tables hold one row per authorization, so there are few of them.
        var codes = await context.OAuthAuthorizationCodes.ToListAsync(cancellationToken);
        var staleCodes = codes.Where(c => c.ExpiresAt <= cutoff).ToList();
        context.OAuthAuthorizationCodes.RemoveRange(staleCodes);

        var tokens = await context.OAuthTokens.ToListAsync(cancellationToken);
        var staleTokens = tokens.Where(t => t.ExpiresAt <= cutoff).ToList();
        context.OAuthTokens.RemoveRange(staleTokens);

        var clients = await context.OAuthClients
            .Where(c => c.RegisteredDynamically && c.LastUsedAt == null)
            .ToListAsync(cancellationToken);
        var abandoned = clients
            .Where(c => c.CreatedAt <= now - AbandonedRegistrationLifetime)
            .ToList();
        context.OAuthClients.RemoveRange(abandoned);

        if (staleCodes.Count > 0 || staleTokens.Count > 0 || abandoned.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IssuedOAuthTokens> IssueTokensAsync(
        OAuthClient client,
        int userId,
        string? scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var access = NewSecret(AccessTokenPrefix);
        var refresh = NewSecret(RefreshTokenPrefix);

        context.OAuthTokens.Add(new OAuthToken
        {
            Kind = OAuthTokenKind.Access,
            SecretHash = Hash(access),
            ClientId = client.Id,
            UserId = userId,
            Scope = scope,
            CreatedAt = now,
            ExpiresAt = now + AccessTokenLifetime,
        });

        context.OAuthTokens.Add(new OAuthToken
        {
            Kind = OAuthTokenKind.Refresh,
            SecretHash = Hash(refresh),
            ClientId = client.Id,
            UserId = userId,
            Scope = scope,
            CreatedAt = now,
            ExpiresAt = now + RefreshTokenLifetime,
        });

        client.LastUsedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        return new IssuedOAuthTokens(access, (int)AccessTokenLifetime.TotalSeconds, refresh, scope);
    }

    private async Task RevokeTokensOfAsync(int clientId, int userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tokens = await context.OAuthTokens
            .Where(t => t.ClientId == clientId && t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    private async Task<OAuthClient> AuthenticateClientAsync(
        string? clientId,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        var client = await LiveClientAsync(clientId, cancellationToken)
            ?? throw new OAuthException("invalid_client", "That client is not registered here.");

        if (client.SecretHash is null)
        {
            return client;
        }

        if (string.IsNullOrWhiteSpace(clientSecret) || !FixedTimeEquals(client.SecretHash, Hash(clientSecret)))
        {
            throw new OAuthException("invalid_client", "That client secret is not right.");
        }

        return client;
    }

    private async Task<OAuthClient?> LiveClientAsync(string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        return await context.OAuthClients
            .SingleOrDefaultAsync(c => c.ClientId == clientId && c.RevokedAt == null, cancellationToken);
    }

    private static IReadOnlyList<string> RedirectUrisOf(OAuthClient client) =>
        [.. client.RedirectUris.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static UserView ViewOf(AppUser user) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.IsFirstUser,
        user.TotpEnabled,
        user.CreatedAt,
        user.LastSignInAt);

    private static IReadOnlyList<string> NormalizeRedirectUris(IReadOnlyList<string>? uris)
    {
        var candidates = (uris ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new OAuthException("invalid_redirect_uri", "A client has to register at least one redirect URI.");
        }

        if (candidates.Count > MaxRedirectUris)
        {
            throw new OAuthException("invalid_redirect_uri", $"A client may register at most {MaxRedirectUris} redirect URIs.");
        }

        foreach (var candidate in candidates)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                throw new OAuthException("invalid_redirect_uri", $"{candidate} is not an absolute URI.");
            }

            if (uri.Fragment.Length > 0)
            {
                throw new OAuthException("invalid_redirect_uri", $"{candidate} carries a fragment, which a redirect URI may not.");
            }

            var loopback = uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && loopback))
            {
                throw new OAuthException(
                    "invalid_redirect_uri",
                    $"{candidate} is not https, and only a loopback address may be plain http.");
            }
        }

        return candidates;
    }

    private static string NormalizeAuthMethod(string? method)
    {
        var requested = string.IsNullOrWhiteSpace(method) ? "none" : method.Trim();
        return requested switch
        {
            "none" or "client_secret_post" or "client_secret_basic" => requested,
            _ => throw new OAuthException(
                "invalid_client_metadata",
                $"{requested} is not a token endpoint authentication method this server offers."),
        };
    }

    private static bool ChallengeMatches(string challenge, string verifier)
    {
        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return FixedTimeEquals(challenge, computed);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static string NewSecret(string prefix) => prefix + Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

    private static string Hash(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
