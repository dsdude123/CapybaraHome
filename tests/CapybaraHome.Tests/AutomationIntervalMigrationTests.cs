using CapybaraHome.Domain.Tasks;
using CapybaraHome.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the migration that turned the six fixed frequencies into an interval of any length. A
/// database written before it holds the old names, and the schedules in it have to come through
/// meaning the same thing rather than falling back to a default.
/// </summary>
public class AutomationIntervalMigrationTests
{
    [Theory]
    [InlineData("Daily", TaskIntervalUnit.Days, 1)]
    [InlineData("Weekly", TaskIntervalUnit.Weeks, 1)]
    [InlineData("BiWeekly", TaskIntervalUnit.Weeks, 2)]
    [InlineData("Monthly", TaskIntervalUnit.Months, 1)]
    [InlineData("Quarterly", TaskIntervalUnit.Months, 3)]
    [InlineData("Yearly", TaskIntervalUnit.Years, 1)]
    public async Task An_old_frequency_becomes_the_interval_it_stood_for(string frequency, TaskIntervalUnit unit, int count)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CapybaraHomeDbContext>().UseSqlite(connection).Options;

        await using (var before = new CapybaraHomeDbContext(options))
        {
            await before.Database.GetService<IMigrator>().MigrateAsync("AddAutomationDueInDays");

            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "TaskAutomations"
                    ("Title", "Frequency", "StartsOn", "TimeOfDay", "SkipWhenOpenTaskExists",
                     "IsEnabled", "LastRunSkipped", "CreatedAt", "UpdatedAt")
                VALUES
                    ({0}, {1}, '2026-10-18', '00:00:00', 0, 1, 0,
                     '2026-10-18 09:00:00+00:00', '2026-10-18 09:00:00+00:00');
                """,
                $"A {frequency} one",
                frequency);
        }

        await using var after = new CapybaraHomeDbContext(options);
        await after.Database.MigrateAsync();

        var automation = await after.TaskAutomations.SingleAsync();
        Assert.Equal(unit, automation.IntervalUnit);
        Assert.Equal(count, automation.IntervalCount);
    }
}
