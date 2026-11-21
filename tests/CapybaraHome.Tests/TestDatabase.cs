using System.Net;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CapybaraHome.Application.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CapybaraHome.Tests;

/// <summary>A migrated SQLite database in memory, with a clock the tests control.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private TestDatabase(SqliteConnection connection, CapybaraHomeDbContext context, FakeTimeProvider clock)
    {
        this.connection = connection;
        Context = context;
        Clock = clock;
    }

    /// <summary>The context under test.</summary>
    public CapybaraHomeDbContext Context { get; }

    /// <summary>The clock the services read, fixed unless a test moves it.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>
    /// A task service over this database. The close operations hang off a completion, so they are
    /// wired in here as well; <paramref name="webhooks"/> replaces the sender when a test is about
    /// the calls themselves.
    /// </summary>
    /// <param name="webhooks">Sender the webhook operations use, or null for one that answers 200.</param>
    public TaskService Tasks(TaskWebhookSender? webhooks = null) => new(Context, CloseOperations(webhooks), Clock);

    /// <summary>A close operation service over this database.</summary>
    /// <param name="webhooks">Sender the webhook operations use, or null for one that answers 200.</param>
    public TaskCloseOperationService CloseOperations(TaskWebhookSender? webhooks = null) =>
        new(Context, webhooks ?? Sender(new FakeHttp().Reply("ok")), Clock);

    /// <summary>
    /// The callbacks the tests are allowed to register. Registration is held to an allowlist, so
    /// a test that registers a client has to name a host on it.
    /// </summary>
    private static readonly string[] TestRedirects =
    [
        "https://example.test/callback",
        "https://other.test/callback",
        "https://idle.test/cb",
        "https://new.test/cb",
        "https://last.test/cb",
        "https://claude.ai/api/mcp/auth_callback",
        "http://localhost:3000/callback",
        "http://127.0.0.1:3000/callback",
    ];

    /// <summary>An authorization server over this database.</summary>
    /// <param name="allowedRedirectUris">Callbacks a client may register, or none for the test list.</param>
    public OAuthService OAuth(params string[] allowedRedirectUris) =>
        new(
            Context,
            Options.Create(new OAuthOptions
            {
                AllowedRedirectUris = allowedRedirectUris.Length > 0 ? allowedRedirectUris : TestRedirects,
            }),
            Clock);

    /// <summary>Returns a sender that answers from <paramref name="http"/>.</summary>
    /// <param name="http">The canned replies to answer with.</param>
    public static TaskWebhookSender Sender(FakeHttp http) => new(http.Client());

    /// <summary>Opens a connection, runs the migrations and returns the ready database.</summary>
    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CapybaraHomeDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new CapybaraHomeDbContext(options);
        await context.Database.MigrateAsync();

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
        return new TestDatabase(connection, context, clock);
    }

    /// <summary>Disposes the context and closes the connection, dropping the database with it.</summary>
    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
