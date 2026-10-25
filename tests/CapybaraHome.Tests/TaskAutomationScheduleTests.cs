using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the dates an automation's schedule lands on. The time zone is a real one with daylight
/// saving in it, because the execution time is a wall clock time the server reads in its own zone.
/// </summary>
public class TaskAutomationScheduleTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    [Theory]
    [InlineData(TaskAutomationFrequency.Daily, "2026-10-02", "2026-10-03")]
    [InlineData(TaskAutomationFrequency.Weekly, "2026-10-08", "2026-10-15")]
    [InlineData(TaskAutomationFrequency.BiWeekly, "2026-10-15", "2026-10-29")]
    [InlineData(TaskAutomationFrequency.Monthly, "2026-11-01", "2026-12-01")]
    [InlineData(TaskAutomationFrequency.Quarterly, "2027-01-01", "2027-04-01")]
    [InlineData(TaskAutomationFrequency.Yearly, "2027-10-01", "2028-10-01")]
    public void Every_frequency_steps_by_its_own_interval(TaskAutomationFrequency frequency, string second, string third)
    {
        var startsOn = new DateOnly(2026, 10, 1);

        var upcoming = TaskAutomationSchedule.Upcoming(
            frequency,
            startsOn,
            new TimeOnly(9, 0),
            Instant(2026, 9, 30, 0, 0),
            3,
            Pacific);

        Assert.Equal(
            [startsOn, DateOnly.Parse(second), DateOnly.Parse(third)],
            upcoming.Select(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m, Pacific).DateTime)));
    }

    [Theory]
    [InlineData(TaskAutomationFrequency.Daily)]
    [InlineData(TaskAutomationFrequency.Weekly)]
    [InlineData(TaskAutomationFrequency.BiWeekly)]
    [InlineData(TaskAutomationFrequency.Monthly)]
    [InlineData(TaskAutomationFrequency.Quarterly)]
    [InlineData(TaskAutomationFrequency.Yearly)]
    public void Every_frequency_previews_in_ascending_order_from_a_start_long_past(TaskAutomationFrequency frequency)
    {
        var now = Instant(2026, 10, 25, 6, 0);

        var upcoming = TaskAutomationSchedule.Upcoming(frequency, new DateOnly(2019, 1, 7), new TimeOnly(7, 30), now, 5, Pacific);

        Assert.Equal(5, upcoming.Count);
        Assert.All(upcoming, moment => Assert.True(moment > now));
        Assert.Equal(upcoming.OrderBy(m => m), upcoming);
    }

    [Fact]
    public void A_monthly_run_counted_from_the_31st_clamps_in_February_and_comes_back_in_March()
    {
        var upcoming = TaskAutomationSchedule.Upcoming(
            TaskAutomationFrequency.Monthly,
            new DateOnly(2027, 1, 31),
            TimeOnly.MinValue,
            Instant(2027, 1, 1, 0, 0),
            3,
            Pacific);

        Assert.Equal(
            [new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31)],
            upcoming.Select(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m, Pacific).DateTime)));
    }

    [Fact]
    public void Midnight_means_midnight_where_the_server_is()
    {
        var next = TaskAutomationSchedule.NextAfter(
            TaskAutomationFrequency.Daily,
            new DateOnly(2026, 10, 20),
            TimeOnly.MinValue,
            Instant(2026, 10, 19, 12, 0),
            Pacific);

        // Pacific is seven hours behind UTC in October, so local midnight is 07:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.Zero), next!.Value.ToUniversalTime());
    }

    [Fact]
    public void An_execution_time_the_clocks_skip_runs_at_the_first_minute_that_exists()
    {
        var next = TaskAutomationSchedule.NextAfter(
            TaskAutomationFrequency.Daily,
            new DateOnly(2026, 3, 8),
            new TimeOnly(2, 30),
            Instant(2026, 3, 7, 12, 0),
            Pacific);

        // 02:30 does not happen on the morning the clocks go forward, so the run is at 03:00.
        var local = TimeZoneInfo.ConvertTime(next!.Value, Pacific);
        Assert.Equal(new DateTime(2026, 3, 8, 3, 0, 0), local.DateTime);
    }

    [Fact]
    public void An_execution_time_the_clocks_repeat_resolves_to_one_of_the_two_passes()
    {
        var next = TaskAutomationSchedule.NextAfter(
            TaskAutomationFrequency.Daily,
            new DateOnly(2026, 11, 1),
            new TimeOnly(1, 30),
            Instant(2026, 10, 31, 12, 0),
            Pacific);

        // 01:30 happens twice that morning; the zone's own offset lookup picks the later pass.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), next!.Value.ToUniversalTime());
    }

    [Fact]
    public void An_occurrence_exactly_on_the_moment_asked_about_is_not_the_next_one()
    {
        var startsOn = new DateOnly(2026, 10, 20);
        var first = TaskAutomationSchedule.Occurrence(TaskAutomationFrequency.Weekly, startsOn, new TimeOnly(6, 0), 0, Pacific);

        var next = TaskAutomationSchedule.NextAfter(TaskAutomationFrequency.Weekly, startsOn, new TimeOnly(6, 0), first, Pacific);

        Assert.Equal(startsOn.AddDays(7), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next!.Value, Pacific).DateTime));
    }

    [Fact]
    public void A_schedule_that_runs_off_the_end_of_the_calendar_has_no_next_execution()
    {
        var next = TaskAutomationSchedule.NextAfter(
            TaskAutomationFrequency.Yearly,
            new DateOnly(2026, 10, 1),
            TimeOnly.MinValue,
            new DateTimeOffset(DateTime.MaxValue.AddDays(-1), TimeSpan.Zero),
            Pacific);

        Assert.Null(next);
    }

    private static DateTimeOffset Instant(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
