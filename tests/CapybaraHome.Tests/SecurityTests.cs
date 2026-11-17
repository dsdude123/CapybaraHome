using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CapybaraHome.Application.Users;
using CapybaraHome.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the headers every response carries, the windows a caller is held to, and the audit
/// line an MCP call leaves behind when it arrives over the real transport.
/// </summary>
public class SecurityTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("1; mode=block", Assert.Single(response.Headers.GetValues("X-XSS-Protection")));
        Assert.Equal(
            "strict-origin-when-cross-origin",
            Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "geolocation=(), microphone=(), camera=()",
            Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public async Task A_caller_past_the_api_window_is_refused_with_the_headers_still_on_the_answer()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        HttpResponseMessage? refused = null;
        for (var attempt = 0; attempt < 220 && refused is null; attempt++)
        {
            var response = await client.GetAsync("/api/tasks");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused = response;
            }
        }

        Assert.NotNull(refused);
        Assert.Equal("60", Assert.Single(refused.Headers.GetValues("Retry-After")));
        Assert.Equal("nosniff", Assert.Single(refused.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("Rate limit exceeded", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_page_load_is_not_refused_by_the_window_the_api_sits_under()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var attempt = 0; attempt < 205; attempt++)
        {
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_mcp_call_is_logged_against_the_user_the_token_resolved_to()
    {
        var log = new RecordingLoggerProvider();
        await using var app = await AppFactory.CreateAsync(
            stubSignIn: false,
            configureServices: services => services.AddSingleton<ILoggerProvider>(log));

        string secret;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserService>();
            var user = Assert.Single(await users.ListAsync());
            var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
            secret = (await tokens.CreateAsync("Tests", user.Id)).Secret;
        }

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        var session = await InitializeAsync(client);
        var answer = await CallToolAsync(client, session, "list_categories");

        Assert.Contains("\"result\"", answer);
        Assert.Contains(
            log.Lines,
            line => line.Contains($"[MCP] list_categories user={AppFactory.SeededUsername}"));
    }

    private static async Task<string?> InitializeAsync(HttpClient client)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "tests", version = "1.0" },
            },
        };

        var response = await client.PostAsJsonAsync("/mcp", request);
        response.EnsureSuccessStatusCode();

        var session = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        if (session is not null)
        {
            var notification = new { jsonrpc = "2.0", method = "notifications/initialized" };
            using var message = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent.Create(notification),
            };
            message.Headers.Add("Mcp-Session-Id", session);
            (await client.SendAsync(message)).EnsureSuccessStatusCode();
        }

        return session;
    }

    private static async Task<string> CallToolAsync(HttpClient client, string? session, string tool)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = tool, arguments = new { } },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(request),
        };

        if (session is not null)
        {
            message.Headers.Add("Mcp-Session-Id", session);
        }

        var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Keeps every line the host logs, so a test can look for one.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (lines)
                {
                    return lines.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> lines) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (lines)
                {
                    lines.Add(formatter(state, exception));
                }
            }

            private sealed class Scope : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
