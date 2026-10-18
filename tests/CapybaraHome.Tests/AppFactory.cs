using System.Security.Claims;
using System.Text.Encodings.Web;
using CapybaraHome.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Tests;

/// <summary>
/// Hosts the real app over a throwaway SQLite file, with a stand-in sign-in so the pages and the
/// API can be exercised without an OpenID Connect provider.
/// </summary>
public class AppFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"capybarahome-tests-{Guid.NewGuid():N}.db");

    /// <summary>Points the app at a temporary database and replaces the browser scheme with a stub.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:CapybaraHome", $"Data Source={databasePath}");
        builder.UseSetting("Backups:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(StubAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, StubAuthenticationHandler>(
                    StubAuthenticationHandler.SchemeName,
                    _ => { });

            var stub = new AuthorizationPolicyBuilder(StubAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build();

            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(stub)
                .SetFallbackPolicy(stub)
                .AddPolicy(AuthenticationSetup.ApiPolicyName, stub);
        });
    }

    /// <summary>Shuts the host down and deletes the temporary database.</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Signs every request in as the same user, so the pages under test can render.</summary>
    private class StubAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Stub";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, "Test user"),
                    new Claim(CapybaraClaims.UserId, "1"),
                ],
                SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
