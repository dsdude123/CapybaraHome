using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Users;

/// <summary>
/// The accounts the app signs people in with. Passwords are checked here and stored only as
/// PBKDF2 hashes. The first account claims an empty installation and is the one that may add
/// and remove other accounts; everyone shares the same task data.
/// </summary>
public class UserService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>True when the installation has at least one account, so setup is done.</summary>
    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(cancellationToken);

    /// <summary>Creates the account that claims an empty installation.</summary>
    /// <exception cref="InvalidOperationException">The installation already has an account.</exception>
    /// <exception cref="ArgumentException">The username or the password is not acceptable.</exception>
    public async Task<UserView> CreateFirstUserAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (await AnyUsersAsync(cancellationToken))
        {
            throw new InvalidOperationException("This installation already has an account. Sign in instead.");
        }

        return await AddAsync(username, displayName, password, isFirstUser: true, createdByUserId: null, cancellationToken);
    }

    /// <summary>Creates another account, on the authority of the first user.</summary>
    /// <exception cref="InvalidOperationException">The acting user may not add accounts.</exception>
    /// <exception cref="ArgumentException">The username is taken, or the password is too short.</exception>
    public async Task<UserView> CreateUserAsync(
        string username,
        string displayName,
        string password,
        int actingUserId,
        CancellationToken cancellationToken = default)
    {
        await RequireFirstUserAsync(actingUserId, cancellationToken);
        return await AddAsync(username, displayName, password, isFirstUser: false, actingUserId, cancellationToken);
    }

    /// <summary>Checks a username, a password and, where the account uses one, an authenticator code.</summary>
    /// <param name="username">Name typed at sign-in.</param>
    /// <param name="password">Password typed at sign-in.</param>
    /// <param name="code">Authenticator code, if one was given.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<SignInResult> SignInAsync(
        string? username,
        string? password,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var name = Normalize(username);
        var user = name.Length == 0
            ? null
            : await context.Users.SingleOrDefaultAsync(u => u.Username == name, cancellationToken);

        // The password is verified even when no such user exists, so that a wrong username does
        // not answer faster than a wrong password and reveal which accounts are real.
        var hash = user?.PasswordHash ?? Decoy;
        var passwordMatches = PasswordHasher.Verify(password, hash);

        if (user is null || !passwordMatches)
        {
            return new SignInResult(SignInOutcome.WrongCredentials, null);
        }

        if (user.TotpEnabled)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new SignInResult(SignInOutcome.CodeRequired, null);
            }

            var step = TotpService.MatchingStep(user.TotpSecret, code, clock.GetUtcNow());

            // A code stays computable for the whole drift window, so without this an onlooker who
            // also had the password could sign in again with the code they saw.
            if (step is null || step <= user.TotpLastUsedStep)
            {
                return new SignInResult(SignInOutcome.WrongCode, null);
            }

            user.TotpLastUsedStep = step;
        }

        user.LastSignInAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return new SignInResult(SignInOutcome.Succeeded, View(user));
    }

    /// <summary>Returns every account, oldest first.</summary>
    public async Task<IReadOnlyList<UserView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await context.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return [.. users.Select(View)];
    }

    /// <summary>Returns one account, or null when no account has that id.</summary>
    public async Task<UserView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : View(user);
    }

    /// <summary>Changes a user's own password, after checking the current one.</summary>
    /// <exception cref="ArgumentException">The current password is wrong, or the new one is too short.</exception>
    public async Task ChangePasswordAsync(
        int userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await Require(userId, cancellationToken);

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            throw new ArgumentException("That is not the current password.", nameof(currentPassword));
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sets another user's password, on the authority of the first user.</summary>
    /// <exception cref="InvalidOperationException">The acting user may not reset passwords.</exception>
    public async Task ResetPasswordAsync(
        int userId,
        string newPassword,
        int actingUserId,
        CancellationToken cancellationToken = default)
    {
        await RequireFirstUserAsync(actingUserId, cancellationToken);

        var user = await Require(userId, cancellationToken);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Starts authenticator setup and returns the secret to type in. It is stored but not in
    /// force, so a half-finished setup cannot lock anyone out; <see cref="ConfirmTotpAsync"/>
    /// turns it on.
    /// </summary>
    public async Task<TotpEnrolment> BeginTotpEnrolmentAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId, cancellationToken);

        var secret = TotpService.GenerateSecret();
        user.TotpSecret = secret;
        user.TotpConfirmedAt = null;
        user.TotpLastUsedStep = null;
        await context.SaveChangesAsync(cancellationToken);

        return new TotpEnrolment(secret, TotpService.BuildUri(secret, user.Username));
    }

    /// <summary>
    /// Returns the enrolment a user has started but not confirmed, so a statically rendered page
    /// can show the same secret again after a post, or null when there is none.
    /// </summary>
    public async Task<TotpEnrolment?> GetPendingTotpEnrolmentAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId, cancellationToken);

        return user is { TotpSecret: { Length: > 0 } secret, TotpConfirmedAt: null }
            ? new TotpEnrolment(secret, TotpService.BuildUri(secret, user.Username))
            : null;
    }

    /// <summary>Abandons an enrolment that was never confirmed. A confirmed one is left alone.</summary>
    public async Task CancelTotpEnrolmentAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId, cancellationToken);

        if (user.TotpConfirmedAt is not null)
        {
            return;
        }

        user.TotpSecret = null;
        user.TotpLastUsedStep = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Turns on authenticator codes once the user proves the app is set up. Returns false when the code is wrong.</summary>
    public async Task<bool> ConfirmTotpAsync(int userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId, cancellationToken);

        var step = TotpService.MatchingStep(user.TotpSecret, code, clock.GetUtcNow());
        if (step is null || step <= user.TotpLastUsedStep)
        {
            return false;
        }

        user.TotpLastUsedStep = step;
        user.TotpConfirmedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Turns authenticator codes off again, after checking the user's password.</summary>
    /// <exception cref="ArgumentException">The password is wrong.</exception>
    public async Task DisableTotpAsync(int userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId, cancellationToken);

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            throw new ArgumentException("That is not the current password.", nameof(password));
        }

        user.TotpSecret = null;
        user.TotpConfirmedAt = null;
        user.TotpLastUsedStep = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes an account, on the authority of the first user. Returns false when no account has that id.</summary>
    /// <exception cref="InvalidOperationException">The acting user may not remove accounts, or the account is the first one.</exception>
    public async Task<bool> DeleteUserAsync(int userId, int actingUserId, CancellationToken cancellationToken = default)
    {
        await RequireFirstUserAsync(actingUserId, cancellationToken);

        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        if (user.IsFirstUser)
        {
            throw new InvalidOperationException("The first account cannot be removed.");
        }

        context.Users.Remove(user);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<UserView> AddAsync(
        string username,
        string displayName,
        string password,
        bool isFirstUser,
        int? createdByUserId,
        CancellationToken cancellationToken)
    {
        var name = Normalize(username);
        if (name.Length < 2)
        {
            throw new ArgumentException("A username needs at least two characters.", nameof(username));
        }

        if (name.Any(c => !char.IsLetterOrDigit(c) && c is not ('.' or '-' or '_')))
        {
            throw new ArgumentException("A username can hold letters, digits, dots, dashes and underscores.", nameof(username));
        }

        if (await context.Users.AnyAsync(u => u.Username == name, cancellationToken))
        {
            throw new ArgumentException("That username is taken.", nameof(username));
        }

        var now = clock.GetUtcNow();
        var user = new AppUser
        {
            Username = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            IsFirstUser = isFirstUser,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        return View(user);
    }

    private async Task<AppUser> Require(int userId, CancellationToken cancellationToken) =>
        await context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
        ?? throw new InvalidOperationException($"No account has id {userId}.");

    private async Task RequireFirstUserAsync(int actingUserId, CancellationToken cancellationToken)
    {
        var acting = await Require(actingUserId, cancellationToken);
        if (!acting.IsFirstUser)
        {
            throw new InvalidOperationException("Only the first account can add or remove accounts.");
        }
    }

    private static string Normalize(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    private static UserView View(AppUser u) =>
        new(u.Id, u.Username, u.DisplayName, u.IsFirstUser, u.TotpEnabled, u.CreatedAt, u.LastSignInAt);

    // A valid hash of a value nobody knows, so a sign-in for a username that does not exist still
    // spends the time a real PBKDF2 check costs.
    private static readonly string Decoy = PasswordHasher.Hash("a-password-that-is-nobody-s");
}
