using CapybaraHome.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
