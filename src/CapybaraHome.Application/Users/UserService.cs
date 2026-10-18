using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Users;

/// <summary>
/// Decides who may sign in. The first identity to arrive claims the installation; every
/// identity after that needs an open invite from a user who is already in.
/// </summary>
public class UserService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>Matches an external identity to a user, creating one if it is entitled to exist.</summary>
    /// <param name="issuer">Issuer of the external identity.</param>
    /// <param name="subject">Subject claim from the provider.</param>
    /// <param name="email">Email claim from the provider, if it sent one.</param>
    /// <param name="displayName">Name claim from the provider, if it sent one.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<SignInResult> SignInAsync(
        string issuer,
        string subject,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var existing = await context.Users.SingleOrDefaultAsync(
            u => u.Issuer == issuer && u.Subject == subject,
            cancellationToken);

        if (existing is not null)
        {
            existing.LastSignInAt = now;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                existing.DisplayName = displayName;
            }

            await context.SaveChangesAsync(cancellationToken);
            return new SignInResult(SignInOutcome.ExistingUser, View(existing));
        }

        var normalizedEmail = email?.Trim().ToLowerInvariant();
        var isFirstUser = !await context.Users.AnyAsync(cancellationToken);

        if (!isFirstUser)
        {
            if (string.IsNullOrEmpty(normalizedEmail))
            {
                return new SignInResult(SignInOutcome.NoEmail, null);
            }

            var invite = await context.UserInvites
                .Where(i => i.Email == normalizedEmail && i.AcceptedAt == null && i.RevokedAt == null)
                .OrderBy(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (invite is null)
            {
                return new SignInResult(SignInOutcome.NotInvited, null);
            }

            invite.AcceptedAt = now;
            var invited = Add(issuer, subject, normalizedEmail, displayName, isFirstUser: false, invite.CreatedByUserId, now);
            await context.SaveChangesAsync(cancellationToken);
            return new SignInResult(SignInOutcome.Invited, View(invited));
        }

        var first = Add(issuer, subject, normalizedEmail ?? string.Empty, displayName, isFirstUser: true, null, now);
        await context.SaveChangesAsync(cancellationToken);
        return new SignInResult(SignInOutcome.FirstUser, View(first));
    }

    /// <summary>Returns every user, oldest first.</summary>
    public async Task<IReadOnlyList<UserView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await context.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return [.. users.Select(View)];
    }

    /// <summary>Returns every invite, newest first.</summary>
    public async Task<IReadOnlyList<InviteView>> ListInvitesAsync(CancellationToken cancellationToken = default)
    {
        return await context.UserInvites.AsNoTracking()
            .OrderByDescending(i => i.Id)
            .Select(i => new InviteView(i.Id, i.Email, i.CreatedAt, i.AcceptedAt, i.RevokedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Lets <paramref name="email"/> sign in, on the authority of <paramref name="invitedByUserId"/>.</summary>
    /// <exception cref="ArgumentException">The address is blank or already has an open invite or an account.</exception>
    public async Task<InviteView> InviteAsync(string email, int invitedByUserId, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || !normalized.Contains('@'))
        {
            throw new ArgumentException("An invite needs an email address.", nameof(email));
        }

        if (await context.Users.AnyAsync(u => u.Email == normalized, cancellationToken))
        {
            throw new ArgumentException("That address already has an account.", nameof(email));
        }

        if (await context.UserInvites.AnyAsync(i => i.Email == normalized && i.AcceptedAt == null && i.RevokedAt == null, cancellationToken))
        {
            throw new ArgumentException("That address already has an open invite.", nameof(email));
        }

        var invite = new UserInvite
        {
            Email = normalized,
            CreatedByUserId = invitedByUserId,
            CreatedAt = clock.GetUtcNow(),
        };

        context.UserInvites.Add(invite);
        await context.SaveChangesAsync(cancellationToken);

        return new InviteView(invite.Id, invite.Email, invite.CreatedAt, invite.AcceptedAt, invite.RevokedAt);
    }

    /// <summary>Withdraws an invite that has not been used. Returns false when there is nothing to withdraw.</summary>
    public async Task<bool> RevokeInviteAsync(int id, CancellationToken cancellationToken = default)
    {
        var invite = await context.UserInvites.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invite is null || !invite.IsOpen)
        {
            return false;
        }

        invite.RevokedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private AppUser Add(string issuer, string subject, string email, string? displayName, bool isFirstUser, int? invitedByUserId, DateTimeOffset now)
    {
        var user = new AppUser
        {
            Issuer = issuer,
            Subject = subject,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? FallbackName(email) : displayName,
            IsFirstUser = isFirstUser,
            InvitedByUserId = invitedByUserId,
            CreatedAt = now,
            LastSignInAt = now,
        };

        context.Users.Add(user);
        return user;
    }

    private static string FallbackName(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : (email.Length == 0 ? "Someone" : email);
    }

    private static UserView View(AppUser u) => new(u.Id, u.Email, u.DisplayName, u.IsFirstUser, u.CreatedAt, u.LastSignInAt);
}
