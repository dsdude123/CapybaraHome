using System.Security.Claims;
using CapybaraHome.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CapybaraHome.Web.Auth;

/// <summary>Turns a checked account into the sign-in cookie, and signs it out again.</summary>
public static class SignInManager
{
    /// <summary>Writes the sign-in cookie for <paramref name="user"/>.</summary>
    /// <param name="http">The request the cookie is written on.</param>
    /// <param name="user">The account that was checked.</param>
    /// <param name="stayedSignedIn">True to keep the cookie after the browser closes.</param>
    public static Task SignInAsync(HttpContext http, UserView user, bool stayedSignedIn)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(CapybaraClaims.Username, user.Username),
                new Claim(CapybaraClaims.UserId, user.Id.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (user.IsFirstUser)
        {
            identity.AddClaim(new Claim(CapybaraClaims.FirstUser, "true"));
        }

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = stayedSignedIn });
    }

    /// <summary>Clears the sign-in cookie.</summary>
    public static Task SignOutAsync(HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
