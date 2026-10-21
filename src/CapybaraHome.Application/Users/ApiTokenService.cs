using System.Security.Cryptography;
using System.Text;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Users;

/// <summary>A token as the settings screen shows it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">Name the owner gave it.</param>
/// <param name="Prefix">First characters of the secret, enough to tell tokens apart.</param>
/// <param name="CreatedAt">When it was minted.</param>
/// <param name="LastUsedAt">When it was last accepted, or null.</param>
/// <param name="RevokedAt">When it was revoked, or null.</param>
public record ApiTokenView(int Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);

/// <summary>A freshly minted token, the one time its secret is readable.</summary>
/// <param name="Token">The token row.</param>
/// <param name="Secret">The secret to paste into the connector. Not stored and not recoverable.</param>
public record MintedApiToken(ApiTokenView Token, string Secret);

/// <summary>
/// Mints and checks the bearer tokens the connector signs in with. Only a SHA-256 hash of
/// each secret is kept, so a leak of the database does not leak usable tokens.
/// </summary>
public class ApiTokenService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    private const int SecretBytes = 32;
    private const int PrefixLength = 8;

    /// <summary>Mints a token for <paramref name="userId"/> and returns its secret once.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<MintedApiToken> CreateAsync(string name, int userId, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A token needs a name.", nameof(name));
        }

        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var token = new ApiToken
        {
            Name = trimmed,
            Prefix = secret[..PrefixLength],
            SecretHash = Hash(secret),
            UserId = userId,
            CreatedAt = clock.GetUtcNow(),
        };

        context.ApiTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);

        return new MintedApiToken(View(token), secret);
    }

    /// <summary>Returns every token, newest first. Secrets are not included.</summary>
    public async Task<IReadOnlyList<ApiTokenView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await context.ApiTokens.AsNoTracking().OrderByDescending(t => t.Id).ToListAsync(cancellationToken);
        return [.. tokens.Select(View)];
    }

    /// <summary>Revokes a token. Returns false when no live token has that id.</summary>
    public async Task<bool> RevokeAsync(int id, CancellationToken cancellationToken = default)
    {
        var token = await context.ApiTokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (token is null || token.RevokedAt is not null)
        {
            return false;
        }

        token.RevokedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns the user a presented secret acts as, or null when it is unknown or revoked.</summary>
    public async Task<UserView?> AuthenticateAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < PrefixLength)
        {
            return null;
        }

        var hash = Hash(secret);
        var token = await context.ApiTokens
            .SingleOrDefaultAsync(t => t.SecretHash == hash && t.RevokedAt == null, cancellationToken);

        if (token is null)
        {
            return null;
        }

        token.LastUsedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        return user is null
            ? null
            : new UserView(
                user.Id,
                user.Username,
                user.DisplayName,
                user.IsFirstUser,
                user.TotpEnabled,
                user.CreatedAt,
                user.LastSignInAt);
    }

    private static string Hash(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static ApiTokenView View(ApiToken t) => new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt, t.RevokedAt);
}
