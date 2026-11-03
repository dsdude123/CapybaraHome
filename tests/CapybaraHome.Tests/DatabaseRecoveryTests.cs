using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the startup pass that repairs what a hard shutdown leaves behind: the abandoned
/// migration lock that otherwise keeps the app waiting forever, and the integrity check that
/// stops it migrating over a damaged file.
/// </summary>
public class DatabaseRecoveryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 16, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"capybarahome-recovery-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(directory, "capybarahome.db");

    private string BackupPath => Path.Combine(directory, "backups");

    /// <summary>Creates the directory this test's database and exports live in.</summary>
    public DatabaseRecoveryTests() => Directory.CreateDirectory(directory);

    /// <summary>Closes the pooled connections and deletes the directory.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_healthy_database_passes_and_is_left_alone()
    {
        await MigrateAsync();

        var result = await RunRecoveryAsync();

        Assert.Equal("ok", result.IntegrityResult);
        Assert.False(result.ClearedStaleLock);
    }

    [Fact]
    public async Task A_database_that_has_never_been_migrated_has_no_lock_to_clear()
    {
        var result = await RunRecoveryAsync();

        Assert.Equal("ok", result.IntegrityResult);
        Assert.False(result.ClearedStaleLock);
    }

    [Fact]
    public async Task An_abandoned_lock_is_cleared_so_the_migrations_can_run()
    {
        await MigrateAsync();
        await HoldLockAsync(Now - DatabaseRecovery.StaleLockAge - TimeSpan.FromMinutes(1));

        var result = await RunRecoveryAsync();

        Assert.True(result.ClearedStaleLock);
        Assert.Null(await ReadLockAsync());

        // The point of clearing it: a start that was stuck waiting now gets through.
        await MigrateAsync();
    }

    [Fact]
    public async Task A_lock_taken_moments_ago_is_left_for_its_holder()
    {
        await MigrateAsync();
        var taken = Now - TimeSpan.FromSeconds(30);
        await HoldLockAsync(taken);

        var result = await RunRecoveryAsync();

        Assert.False(result.ClearedStaleLock);
        Assert.NotNull(await ReadLockAsync());
    }

    [Fact]
    public async Task A_lock_holding_something_that_is_not_a_time_is_treated_as_abandoned()
    {
        await MigrateAsync();
        await HoldLockAsync("not a time at all");

        var result = await RunRecoveryAsync();

        Assert.True(result.ClearedStaleLock);
        Assert.Null(await ReadLockAsync());
    }

    [Fact]
    public async Task A_lock_dated_in_the_future_is_cleared_rather_than_waited_on()
    {
        await MigrateAsync();
        await HoldLockAsync(Now + TimeSpan.FromHours(2));

        var result = await RunRecoveryAsync();

        Assert.True(result.ClearedStaleLock);
    }

    [Fact]
    public async Task A_damaged_database_is_refused_instead_of_migrated_over()
    {
        await MigrateAsync();
        await FillPagesAsync();
        Damage();

        var failure = await Assert.ThrowsAsync<CorruptDatabaseException>(() => RunRecoveryAsync());

        Assert.NotEqual("ok", failure.IntegrityResult);
        Assert.Contains(BackupPath, failure.Message);
    }

    [Fact]
    public async Task A_lock_holding_a_number_instead_of_a_time_is_treated_as_abandoned()
    {
        await MigrateAsync();
        await HoldRawLockAsync(20261103);

        var result = await RunRecoveryAsync();

        Assert.True(result.ClearedStaleLock);
        Assert.Null(await ReadLockAsync());
    }

    [Fact]
    public async Task The_refusal_names_the_export_to_restore_from()
    {
        Directory.CreateDirectory(BackupPath);
        await File.WriteAllTextAsync(Path.Combine(BackupPath, "capybarahome-20261101-030000.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(BackupPath, "capybarahome-20261102-030000.json"), "{}");

        await MigrateAsync();
        await FillPagesAsync();
        Damage();

        var log = new RecordingLogger();
        await Assert.ThrowsAsync<CorruptDatabaseException>(() => RunRecoveryAsync(log));

        Assert.Contains("capybarahome-20261102-030000.json", Assert.Single(log.Messages));
    }

    [Fact]
    public async Task The_refusal_stands_on_its_own_when_there_is_no_export_yet()
    {
        Directory.CreateDirectory(BackupPath);

        await MigrateAsync();
        await FillPagesAsync();
        Damage();

        var log = new RecordingLogger();
        await Assert.ThrowsAsync<CorruptDatabaseException>(() => RunRecoveryAsync(log));

        var message = Assert.Single(log.Messages);
        Assert.Contains(BackupPath, message);
        Assert.DoesNotContain("the newest of which", message);
    }

    [Fact]
    public async Task A_damaged_database_leaves_the_lock_alone()
    {
        await MigrateAsync();
        await FillPagesAsync();
        await HoldLockAsync(Now - TimeSpan.FromDays(1));
        Damage();

        await Assert.ThrowsAsync<CorruptDatabaseException>(() => RunRecoveryAsync());

        Assert.NotNull(await ReadLockAsync());
    }

    [Fact]
    public async Task The_app_starts_over_a_database_a_crash_left_locked()
    {
        await MigrateAsync();
        await HoldLockAsync(DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
        SqliteConnection.ClearAllPools();

        await using var app = await AppFactory.CreateAsync(databasePath: DatabasePath);
        var client = app.CreateClient();

        var health = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        Assert.Null(await ReadLockAsync());
    }

    private async Task<DatabaseRecoveryResult> RunRecoveryAsync(ILogger<DatabaseRecovery>? log = null)
    {
        await using var context = NewContext();
        var recovery = new DatabaseRecovery(
            context,
            Options.Create(new BackupOptions { Destination = BackupPath }),
            log ?? NullLogger<DatabaseRecovery>.Instance,
            new FakeTimeProvider(Now));

        return await recovery.RunAsync();
    }

    private CapybaraHomeDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CapybaraHomeDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;

        return new CapybaraHomeDbContext(options);
    }

    private async Task MigrateAsync()
    {
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    private Task HoldLockAsync(DateTimeOffset taken) =>
        HoldLockAsync(taken.ToString("yyyy-MM-dd HH:mm:ss.fffffffK", System.Globalization.CultureInfo.InvariantCulture));

    private async Task HoldLockAsync(string stored)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" ("Id" INTEGER NOT NULL PRIMARY KEY, "Timestamp" TEXT NOT NULL);
            DELETE FROM "__EFMigrationsLock";
            INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp") VALUES (1, $taken);
            """;
        command.Parameters.AddWithValue("$taken", stored);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Stores the lock timestamp as the given value, whatever type SQLite keeps it as.</summary>
    private async Task HoldRawLockAsync(object stored)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" ("Id" INTEGER NOT NULL PRIMARY KEY, "Timestamp" TEXT NOT NULL);
            DELETE FROM "__EFMigrationsLock";
            INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp") VALUES (1, $stored);
            """;
        command.Parameters.AddWithValue("$stored", stored);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ReadLockAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Timestamp" FROM "__EFMigrationsLock" ORDER BY "Id" LIMIT 1;""";
        return await command.ExecuteScalarAsync() as string;
    }

    /// <summary>Writes enough rows that the database holds pages the damage below can reach.</summary>
    private async Task FillPagesAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();

        await using var prepare = connection.CreateCommand();
        prepare.CommandText =
            """
            PRAGMA journal_mode = delete;
            CREATE TABLE "Filler" ("Id" INTEGER PRIMARY KEY, "Text" TEXT NOT NULL);
            """;
        await prepare.ExecuteNonQueryAsync();

        await using var transaction = await connection.BeginTransactionAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = """INSERT INTO "Filler" ("Text") VALUES (hex(randomblob(400)));""";
        for (var row = 0; row < 400; row++)
        {
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Keeps what was logged, so a test can read the line somebody recovering would.</summary>
    private sealed class RecordingLogger : ILogger<DatabaseRecovery>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Critical;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    /// <summary>Overwrites the content of a page, which is what a half-written file looks like.</summary>
    private void Damage()
    {
        using var file = File.Open(DatabasePath, FileMode.Open, FileAccess.ReadWrite);
        file.Seek(file.Length / 2, SeekOrigin.Begin);
        file.Write(new byte[2048]);
    }
}
