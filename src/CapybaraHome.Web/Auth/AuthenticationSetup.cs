using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace CapybaraHome.Web.Auth;

/// <summary>Wires up the sign-in cookie and the connector's bearer tokens.</summary>
public static class AuthenticationSetup
{
    /// <summary>Policy the API and the MCP endpoint run under, which also accepts a bearer token.</summary>
    public const string ApiPolicyName = "ApiAccess";

    /// <summary>Scheme that routes an API request to the bearer token or to the sign-in cookie.</summary>
    public const string ApiSchemeName = "ApiAuth";

    /// <summary>Name of the sign-in cookie.</summary>
    public const string CookieName = "capybarahome.auth";

    /// <summary>Adds authentication and the two policies the app runs under.</summary>
    public static IServiceCollection AddCapybaraAuthentication(this IServiceCollection services)
    {
        var builder = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/sign-in";
                options.AccessDeniedPath = "/access-denied";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Cookie.Name = CookieName;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenAuthenticationHandler.SchemeName,
                _ => { });

        // The API and the MCP endpoint are reached two ways: by the connector with a bearer token,
        // and by a signed-in browser following a link such as the export. A policy scheme picks
        // between them per request, so an unauthenticated API call answers 401 rather than
        // redirecting a machine to a sign-in page it cannot complete.
        builder.AddPolicyScheme(ApiSchemeName, ApiSchemeName, options =>
        {
            options.ForwardDefaultSelector = context =>
                !context.Request.Headers.ContainsKey("Authorization")
                && context.Request.Cookies.ContainsKey(CookieName)
                    ? CookieAuthenticationDefaults.AuthenticationScheme
                    : ApiTokenAuthenticationHandler.SchemeName;
        });

        services.AddAuthorization(options =>
        {
            var browser = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();

            options.DefaultPolicy = browser;
            options.FallbackPolicy = browser;

            options.AddPolicy(ApiPolicyName, policy => policy
                .AddAuthenticationSchemes(ApiSchemeName)
                .RequireAuthenticatedUser());
        });

        return services;
    }
}
