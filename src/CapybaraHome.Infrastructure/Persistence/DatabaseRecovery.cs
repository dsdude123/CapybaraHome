using System.Globalization;
using CapybaraHome.Application.DataTransfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>
/// Checks the database and clears what a hard shutdown leaves behind, before the migrations run.
/// A host that dies mid-migration leaves the row Entity Framework uses as its migration lock in
/// place, and every later start then waits on a holder that no longer exists, with no timeout: the
/// port never opens and the app is down until somebody deletes the row by hand.
/// </summary>
public class DatabaseRecovery(
    CapybaraHomeDbContext context,
    IOptions<BackupOptions> backups,
    ILogger<DatabaseRecovery> logger,
    TimeProvider clock)
{
    /// <summary>
    /// How old the migration lock has to be before it is treated as abandoned. The migrations are
    /// schema changes against one small SQLite file and finish in seconds, and nothing but this
    /// process writes the database, so a lock older than this has no live holder to wait for.
    /// </summary>
    public static readonly TimeSpan StaleLockAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs the integrity check and clears an abandoned migration lock. Returns what it found.
    /// </summary>
    /// <exception cref="CorruptDatabaseException">The integrity check failed, so nothing was changed.</exception>
    public async Task<DatabaseRecoveryResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var integrity = await CheckIntegrityAsync(cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogCritical(
                "The database failed its integrity check: {Result}. Restore it from an export in {Destination}{Newest}.",
                integrity,
                backups.Value.Destination,
                DescribeNewestBackup());

            throw new CorruptDatabaseException(integrity, backups.Value.Destination);
        }

        var cleared = await ClearStaleMigrationLockAsync(cancellationToken);
        return new DatabaseRecoveryResult(integrity, cleared);
    }

    private async Task<string> CheckIntegrityAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result as string ?? "no result";
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> ClearStaleMigrationLockAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = context.Database.GetDbConnection();

            // A database that has never been migrated has no lock table, and SQLite rejects a
            // statement naming a missing table outright rather than returning no rows.
            await using var exists = connection.CreateCommand();
            exists.CommandText =
                """SELECT COUNT(*) FROM "sqlite_master" WHERE "name" = '__EFMigrationsLock' AND "type" = 'table';""";

            if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
            {
                return false;
            }

            await using var read = connection.CreateCommand();
            read.CommandText = """SELECT "Timestamp" FROM "__EFMigrationsLock" ORDER BY "Id" LIMIT 1;""";

            var held = await read.ExecuteScalarAsync(cancellationToken);
            if (held is null or DBNull)
            {
                return false;
            }

            var age = AgeOf(held);
            if (age is not null && age < StaleLockAge)
            {
                logger.LogInformation(
                    "A migration lock taken {Age} ago is held, which is recent enough to belong to a live migration.",
                    age);
                return false;
            }

            await using var delete = connection.CreateCommand();
            delete.CommandText = """DELETE FROM "__EFMigrationsLock";""";
            await delete.ExecuteNonQueryAsync(cancellationToken);

            logger.LogWarning(
                "Cleared an abandoned migration lock taken {Taken}, left behind by a host that stopped mid-migration.",
                held);

            return true;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Reads the age of the stored lock timestamp. Entity Framework writes it as text, so a value
    /// this cannot read is one no running migration wrote and is reported as an unknown age.
    /// </summary>
    private TimeSpan? AgeOf(object stored)
    {
        var text = stored as string ?? Convert.ToString(stored, CultureInfo.InvariantCulture);
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var taken))
        {
            logger.LogWarning("The migration lock holds {Stored}, which is not a time, so it is treated as abandoned.", text);
            return null;
        }

        var age = clock.GetUtcNow() - taken;
        if (age < TimeSpan.Zero)
        {
            // Entity Framework stamps the lock with the current time, so one dated ahead of this
            // clock came from a host whose time moved. Waiting on it would never end.
            logger.LogWarning("The migration lock is dated {Taken}, which is ahead of now, so it is treated as abandoned.", taken);
            return null;
        }

        return age;
    }

    private string DescribeNewestBackup()
    {
        try
        {
            var newest = new DirectoryInfo(backups.Value.Destination)
                .GetFiles("capybarahome-*.json")
                .MaxBy(file => file.Name);

            return newest is null ? string.Empty : $", the newest of which is {newest.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return string.Empty;
        }
    }
}

/// <summary>What the recovery pass found before the migrations ran.</summary>
/// <param name="IntegrityResult">What SQLite's integrity check reported.</param>
/// <param name="ClearedStaleLock">Whether an abandoned migration lock was deleted.</param>
public record DatabaseRecoveryResult(string IntegrityResult, bool ClearedStaleLock);

/// <summary>Thrown when the database fails SQLite's integrity check, so no migration is attempted.</summary>
public class CorruptDatabaseException : Exception
{
    /// <summary>Creates the exception from what the integrity check reported.</summary>
    /// <param name="integrityResult">What SQLite's integrity check reported.</param>
    /// <param name="backupDestination">Directory the exports to restore from are written to.</param>
    public CorruptDatabaseException(string integrityResult, string backupDestination)
        : base($"The database failed its integrity check ({integrityResult}). "
            + $"Restore it from an export in {backupDestination} rather than migrating over it.")
    {
        IntegrityResult = integrityResult;
    }

    /// <summary>What SQLite's integrity check reported.</summary>
    public string IntegrityResult { get; }
}
