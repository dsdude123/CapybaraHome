using System.Security.Claims;
using CapybaraHome.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CapybaraHome.Tests;

/// <summary>Holds the audit lines the MCP tools write, so a test can read them back.</summary>
/// <typeparam name="T">The tool class the lines were written for.</typeparam>
public sealed class McpAuditLog<T> : ILogger<T>
{
    /// <summary>Every line written so far, in order.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>The one line written, which fails the test when there is not exactly one.</summary>
    public string Single() => Assert.Single(Lines);

    /// <summary>Forgets the lines written so far.</summary>
    public void Clear() => Lines.Clear();

    /// <summary>Scopes are not recorded.</summary>
    /// <param name="state">The scope's state.</param>
    /// <typeparam name="TState">Type of the scope's state.</typeparam>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Scope();

    /// <summary>Every level is recorded.</summary>
    /// <param name="logLevel">The level being asked about.</param>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>Records the formatted message.</summary>
    /// <param name="logLevel">Level the line was written at.</param>
    /// <param name="eventId">Event the line was written for.</param>
    /// <param name="state">The line's state.</param>
    /// <param name="exception">Exception the line carries, if any.</param>
    /// <param name="formatter">Renders the line.</param>
    /// <typeparam name="TState">Type of the line's state.</typeparam>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));

    /// <summary>A request signed in as <paramref name="username"/>.</summary>
    /// <param name="username">Username the request's token resolved to.</param>
    public static IHttpContextAccessor RequestFrom(string username)
    {
        var identity = new ClaimsIdentity([new Claim(CapybaraClaims.Username, username)], "Test");
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
