using System.Security.Claims;

namespace CapybaraHome.Web.Auth;

/// <summary>Claims the app adds to a signed-in principal, and the helpers that read them back.</summary>
public static class CapybaraClaims
{
    /// <summary>Claim holding the <c>AppUser.Id</c> of the signed-in user.</summary>
    public const string UserId = "capybarahome:user-id";

    /// <summary>Returns the signed-in user's id, or null when the principal is not one of ours.</summary>
    public static int? GetUserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(UserId)?.Value;
        return int.TryParse(value, out var id) ? id : null;
    }
}
