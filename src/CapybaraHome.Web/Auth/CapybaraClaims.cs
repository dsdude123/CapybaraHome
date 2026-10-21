using System.Security.Claims;

namespace CapybaraHome.Web.Auth;

/// <summary>Claims the app adds to a signed-in principal, and the helpers that read them back.</summary>
public static class CapybaraClaims
{
    /// <summary>Claim holding the <c>AppUser.Id</c> of the signed-in user.</summary>
    public const string UserId = "capybarahome:user-id";

    /// <summary>Claim holding the signed-in user's username.</summary>
    public const string Username = "capybarahome:username";

    /// <summary>Claim present only on the first account, which may add and remove accounts.</summary>
    public const string FirstUser = "capybarahome:first-user";

    /// <summary>Returns the signed-in user's id, or null when the principal is not one of ours.</summary>
    public static int? GetUserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(UserId)?.Value;
        return int.TryParse(value, out var id) ? id : null;
    }

    /// <summary>True when the signed-in user is the first account.</summary>
    public static bool IsFirstUser(this ClaimsPrincipal? principal) =>
        principal?.FindFirst(FirstUser)?.Value == "true";
}
