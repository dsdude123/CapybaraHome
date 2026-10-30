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
    [InlineData(TaskIntervalUnit.Days, 1, "2026-10-02", "2026-10-03")]
    [InlineData(TaskIntervalUnit.Days, 13, "2026-10-14", "2026-10-27")]
    [InlineData(TaskIntervalUnit.Weeks, 1, "2026-10-08", "2026-10-15")]
    [InlineData(TaskIntervalUnit.Weeks, 2, "2026-10-15", "2026-10-29")]
    [InlineData(TaskIntervalUnit.Weeks, 6, "2026-11-12", "2026-12-24")]
    [InlineData(TaskIntervalUnit.Months, 1, "2026-11-01", "2026-12-01")]
    [InlineData(TaskIntervalUnit.Months, 3, "2027-01-01", "2027-04-01")]
    [InlineData(TaskIntervalUnit.Months, 9, "2027-07-01", "2028-04-01")]
    [InlineData(TaskIntervalUnit.Years, 1, "2027-10-01", "2028-10-01")]
    [InlineData(TaskIntervalUnit.Years, 3, "2029-10-01", "2032-10-01")]
    public void An_interval_of_any_length_steps_by_itself(TaskIntervalUnit unit, int count, string second, string third)
    {
        var startsOn = new DateOnly(2026, 10, 1);

        var upcoming = TaskAutomationSchedule.Upcoming(
            unit,
            count,
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
    [InlineData(TaskIntervalUnit.Days, 1)]
    [InlineData(TaskIntervalUnit.Days, 13)]
    [InlineData(TaskIntervalUnit.Weeks, 1)]
    [InlineData(TaskIntervalUnit.Weeks, 2)]
    [InlineData(TaskIntervalUnit.Months, 1)]
    [InlineData(TaskIntervalUnit.Months, 9)]
    [InlineData(TaskIntervalUnit.Years, 1)]
    [InlineData(TaskIntervalUnit.Years, 3)]
    public void Every_interval_previews_in_ascending_order_from_a_start_long_past(TaskIntervalUnit unit, int count)
    {
        var now = Instant(2026, 10, 25, 6, 0);

        var upcoming = TaskAutomationSchedule.Upcoming(unit, count, new DateOnly(2019, 1, 7), new TimeOnly(7, 30), now, 5, Pacific);

        Assert.Equal(5, upcoming.Count);
        Assert.All(upcoming, moment => Assert.True(moment > now));
        Assert.Equal(upcoming.OrderBy(m => m), upcoming);
    }

    [Fact]
    public void A_monthly_run_counted_from_the_31st_clamps_in_February_and_comes_back_in_March()
    {
        var upcoming = TaskAutomationSchedule.Upcoming(
            TaskIntervalUnit.Months,
            1,
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
            TaskIntervalUnit.Days,
            1,
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
            TaskIntervalUnit.Days,
            1,
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
            TaskIntervalUnit.Days,
            1,
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
        var first = TaskAutomationSchedule.Occurrence(TaskIntervalUnit.Weeks, 1, startsOn, new TimeOnly(6, 0), 0, Pacific);

        var next = TaskAutomationSchedule.NextAfter(TaskIntervalUnit.Weeks, 1, startsOn, new TimeOnly(6, 0), first, Pacific);

        Assert.Equal(startsOn.AddDays(7), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next!.Value, Pacific).DateTime));
    }

    [Fact]
    public void A_schedule_that_runs_off_the_end_of_the_calendar_has_no_next_execution()
    {
        var next = TaskAutomationSchedule.NextAfter(
            TaskIntervalUnit.Years,
            1,
            new DateOnly(2026, 10, 1),
            TimeOnly.MinValue,
            new DateTimeOffset(DateTime.MaxValue.AddDays(-1), TimeSpan.Zero),
            Pacific);

        Assert.Null(next);
    }

    [Fact]
    public void A_preview_that_runs_out_of_calendar_returns_the_executions_it_has()
    {
        var upcoming = TaskAutomationSchedule.Upcoming(
            TaskIntervalUnit.Years,
            1,
            new DateOnly(9999, 1, 1),
            TimeOnly.MinValue,
            new DateTimeOffset(new DateTime(9998, 12, 31), TimeSpan.Zero),
            TaskAutomationService.PreviewCount,
            Pacific);

        Assert.Equal([new DateOnly(9999, 1, 1)], upcoming.Select(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m, Pacific).DateTime)));
    }

    [Theory]
    [InlineData(TaskIntervalUnit.Days, 3)]
    [InlineData(TaskIntervalUnit.Weeks, 1)]
    public void An_interval_too_wide_for_the_calendar_is_refused_rather_than_wrapped(TaskIntervalUnit unit, int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskAutomationSchedule.Occurrence(
            unit, int.MaxValue, new DateOnly(2026, 10, 1), TimeOnly.MinValue, index, Pacific));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "2026-11-02")]
    [InlineData(DayOfWeek.Tuesday, "2026-11-03")]
    [InlineData(DayOfWeek.Wednesday, "2026-11-04")]
    [InlineData(DayOfWeek.Thursday, "2026-11-05")]
    [InlineData(DayOfWeek.Friday, "2026-10-30")]
    [InlineData(DayOfWeek.Saturday, "2026-10-31")]
    [InlineData(DayOfWeek.Sunday, "2026-11-01")]
    public void A_weekly_schedule_starts_on_the_first_of_its_day_on_or_after_the_start_date(DayOfWeek day, string expected)
    {
        // The 30th of October 2026 is a Friday, so Friday itself is the start date and every other
        // day is the one that follows it.
        var anchor = TaskAutomationSchedule.Anchor(TaskIntervalUnit.Weeks, new DateOnly(2026, 10, 30), day);

        Assert.Equal(DateOnly.Parse(expected), anchor);
        Assert.Equal(day, anchor.DayOfWeek);
    }

    [Fact]
    public void A_weekly_schedule_keeps_its_day_every_time_it_repeats()
    {
        var upcoming = TaskAutomationSchedule.Upcoming(
            TaskIntervalUnit.Weeks,
            2,
            TaskAutomationSchedule.Anchor(TaskIntervalUnit.Weeks, new DateOnly(2026, 10, 30), DayOfWeek.Sunday),
            new TimeOnly(8, 0),
            Instant(2026, 10, 30, 0, 0),
            4,
            Pacific);

        Assert.All(upcoming, moment => Assert.Equal(
            DayOfWeek.Sunday,
            TimeZoneInfo.ConvertTime(moment, Pacific).DayOfWeek));
        Assert.Equal(
            [new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 29), new DateOnly(2026, 12, 13)],
            upcoming.Select(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m, Pacific).DateTime)));
    }

    [Theory]
    [InlineData(TaskIntervalUnit.Days)]
    [InlineData(TaskIntervalUnit.Months)]
    [InlineData(TaskIntervalUnit.Years)]
    public void A_day_of_the_week_does_nothing_to_an_interval_that_is_not_counted_in_weeks(TaskIntervalUnit unit)
    {
        var startsOn = new DateOnly(2026, 10, 30);

        Assert.Equal(startsOn, TaskAutomationSchedule.Anchor(unit, startsOn, DayOfWeek.Sunday));
    }

    [Fact]
    public void A_weekly_schedule_with_no_day_of_the_week_keeps_the_start_date()
    {
        var startsOn = new DateOnly(2026, 10, 30);

        Assert.Equal(startsOn, TaskAutomationSchedule.Anchor(TaskIntervalUnit.Weeks, startsOn, null));
    }

    private static DateTimeOffset Instant(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
