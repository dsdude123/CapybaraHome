namespace CapybaraHome.Application.Users;

/// <summary>A user as the settings screen shows them.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Username">Name typed at sign-in.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="IsFirstUser">True for the account that claimed the installation.</param>
/// <param name="TotpEnabled">True when the user signs in with an authenticator code.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="LastSignInAt">When the user last signed in, or null.</param>
public record UserView(
    int Id,
    string Username,
    string DisplayName,
    bool IsFirstUser,
    bool TotpEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt);

/// <summary>Why a sign-in was turned away, or that it was accepted.</summary>
public enum SignInOutcome
{
    /// <summary>The username and password matched, and any code required was right.</summary>
    Succeeded,

    /// <summary>No user has that name, or the password was wrong. The two are not told apart.</summary>
    WrongCredentials,

    /// <summary>The password was right but the account needs an authenticator code, which was missing.</summary>
    CodeRequired,

    /// <summary>The password was right but the authenticator code was wrong.</summary>
    WrongCode,
}

/// <summary>What happened when someone tried to sign in.</summary>
/// <param name="Outcome">Whether the attempt was accepted, and why not when it was refused.</param>
/// <param name="User">The user signed in, or null when the attempt was refused.</param>
public record SignInResult(SignInOutcome Outcome, UserView? User)
{
    /// <summary>True when the attempt was accepted.</summary>
    public bool IsAllowed => User is not null;
}

/// <summary>A started authenticator setup, which is not in force until a code confirms it.</summary>
/// <param name="Secret">Base32 secret to type into the app.</param>
/// <param name="Uri">The <c>otpauth://</c> URI holding the same secret.</param>
public record TotpEnrolment(string Secret, string Uri);
