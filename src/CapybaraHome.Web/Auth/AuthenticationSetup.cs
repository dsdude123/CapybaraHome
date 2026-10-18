using System.Security.Claims;
using CapybaraHome.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CapybaraHome.Web.Auth;

/// <summary>Wires up cookie sign-in, the OpenID Connect provider and the connector's bearer tokens.</summary>
public static class AuthenticationSetup
{
    /// <summary>Policy the API and the MCP endpoint run under, which also accepts a bearer token.</summary>
    public const string ApiPolicyName = "ApiAccess";

    /// <summary>Scheme that routes an API request to the bearer token or to the sign-in cookie.</summary>
    public const string ApiSchemeName = "ApiAuth";

    private const string CookieName = "capybarahome.auth";

    /// <summary>Adds authentication and the two policies the app runs under.</summary>
    public static IServiceCollection AddCapybaraAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        out OidcOptions oidc)
    {
        oidc = new OidcOptions();
        configuration.GetSection(OidcOptions.SectionName).Bind(oidc);
        services.Configure<OidcOptions>(configuration.GetSection(OidcOptions.SectionName));

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

        if (oidc.IsConfigured)
        {
            var settings = oidc;
            builder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                foreach (var scope in settings.AdditionalScopes)
                {
                    options.Scope.Add(scope);
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                };

                options.Events = new OpenIdConnectEvents
                {
                    OnTicketReceived = OnTicketReceivedAsync,
                };
            });
        }

        services.AddAuthorization(options =>
        {
            // The UI is reached by a browser, so an unauthenticated visit has to end at the sign-in
            // page. The API and the MCP endpoint also take the connector's bearer token, and an
            // unauthenticated call there has to end in a 401 rather than a redirect.
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

    // The provider has vouched for the identity; whether it may use this installation is ours to
    // decide. The first identity to arrive claims it, and every one after needs an open invite.
    private static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var principal = context.Principal;
        var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal?.FindFirst("sub")?.Value;

        if (principal is null || string.IsNullOrEmpty(subject))
        {
            context.HandleResponse();
            context.Response.Redirect("/access-denied?reason=no-subject");
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<UserService>();
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value;
        var name = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        var issuer = principal.FindFirst("iss")?.Value
            ?? (context.Options as Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions)?.Authority
            ?? "unknown";

        var result = await users.SignInAsync(issuer, subject, email, name, context.HttpContext.RequestAborted);

        if (!result.IsAllowed)
        {
            context.HandleResponse();
            var reason = result.Outcome == SignInOutcome.NoEmail ? "no-email" : "not-invited";
            context.Response.Redirect($"/access-denied?reason={reason}");
            return;
        }

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new System.Security.Claims.Claim(CapybaraClaims.UserId, result.User!.Id.ToString()));

        if (identity.FindFirst(ClaimTypes.Name) is null)
        {
            identity.AddClaim(new System.Security.Claims.Claim(ClaimTypes.Name, result.User.DisplayName));
        }
    }
}
