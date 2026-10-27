using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CapybaraHome.Application.Reports;
using CapybaraHome.Application.Users;
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
/// Hosts the real app over a throwaway SQLite file. By default it seeds the first account and
/// signs every request in as it, so the pages and the API can be exercised directly; a test that
/// is about signing in asks for the real cookie scheme instead.
/// </summary>
public class AppFactory : WebApplicationFactory<Program>
{
    /// <summary>The password the seeded account is created with.</summary>
    public const string SeededPassword = "a-long-enough-password";

    /// <summary>The username the seeded account is created with.</summary>
    public const string SeededUsername = "first";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"capybarahome-tests-{Guid.NewGuid():N}.db");
    private readonly bool stubSignIn;
    private readonly Action<IServiceCollection>? configureServices;

    private AppFactory(bool stubSignIn, Action<IServiceCollection>? configureServices)
    {
        this.stubSignIn = stubSignIn;
        this.configureServices = configureServices;
    }

    /// <summary>
    /// Starts the app. With <paramref name="stubSignIn"/> every request arrives signed in; with
    /// <paramref name="seedFirstUser"/> the installation already has its first account.
    /// </summary>
    /// <param name="stubSignIn">Whether every request arrives signed in as the first account.</param>
    /// <param name="seedFirstUser">Whether the installation already has its first account.</param>
    /// <param name="configureServices">Replaces services for the test, such as the JIRA client's handler.</param>
    public static async Task<AppFactory> CreateAsync(
        bool stubSignIn = true,
        bool seedFirstUser = true,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new AppFactory(stubSignIn, configureServices);

        if (seedFirstUser)
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserService>();
            await users.CreateFirstUserAsync(SeededUsername, "First", SeededPassword);
        }
        else
        {
            _ = factory.Services;
        }

        return factory;
    }

    /// <summary>Points the app at a temporary database and, unless a test opts out, stubs the sign-in.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:CapybaraHome", $"Data Source={databasePath}");
        builder.UseSetting("Backups:Enabled", "false");

        // The report reads the open web. Here every one of its sources answers from inside the
        // process, so a test is neither slow nor at the mercy of a news site, and a test that
        // wants real answers registers its own handler afterwards.
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<WeatherFeed>().ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
            services.AddHttpClient<CalendarFeed>().ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
            services.AddHttpClient<NewsFeed>().ConfigurePrimaryHttpMessageHandler(() => new OfflineHandler());
        });

        if (configureServices is not null)
        {
            builder.ConfigureServices(configureServices);
        }

        if (!stubSignIn)
        {
            return;
        }

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

    /// <summary>Refuses every request, so nothing a test runs reaches the network.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    /// <summary>Signs every request in as the first account, so the pages under test can render.</summary>
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
                    new Claim(ClaimTypes.Name, "First"),
                    new Claim(CapybaraClaims.UserId, "1"),
                    new Claim(CapybaraClaims.Username, SeededUsername),
                    new Claim(CapybaraClaims.FirstUser, "true"),
                ],
                SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
