using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Web;

namespace CapybaraHome.Tests;

/// <summary>Covers every way a due date can be worked out from the day a task was completed.</summary>
public class TaskDueDateTests
{
    private static readonly DateOnly Completed = new(2026, 2, 10);

    [Theory]
    [InlineData(TaskDueDateMode.None, null, null)]
    [InlineData(TaskDueDateMode.OnDate, null, "2026-12-01")]
    [InlineData(TaskDueDateMode.InDays, 5, "2026-02-15")]
    [InlineData(TaskDueDateMode.InWeeks, 3, "2026-03-03")]
    [InlineData(TaskDueDateMode.InMonths, 4, "2026-06-10")]
    [InlineData(TaskDueDateMode.InYears, 2, "2028-02-10")]
    [InlineData(TaskDueDateMode.NextQuarter, null, "2026-04-01")]
    [InlineData(TaskDueDateMode.EndOfQuarter, null, "2026-03-31")]
    public void Every_mode_gives_the_date_it_names(TaskDueDateMode mode, int? value, string? expected)
    {
        var picked = mode == TaskDueDateMode.OnDate ? new DateOnly(2026, 12, 1) : (DateOnly?)null;

        var due = TaskDueDate.Resolve(mode, value, picked, Completed);

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), due);
    }

    [Fact]
    public void Every_mode_the_picker_offers_resolves_and_reads_back_in_words()
    {
        Assert.Equal(Enum.GetValues<TaskDueDateMode>(), CloseOperationNames.AllDueDateModes);

        foreach (var mode in CloseOperationNames.AllDueDateModes)
        {
            var value = TaskDueDate.NeedsValue(mode) ? 2 : (int?)null;
            var date = TaskDueDate.NeedsDate(mode) ? Completed.AddDays(30) : (DateOnly?)null;

            Assert.Null(TaskDueDate.Validate(mode, value, date));
            Assert.NotEmpty(CloseOperationNames.Label(mode));
            Assert.NotEmpty(CloseOperationNames.DueDateSummary(mode, value, date));

            var due = TaskDueDate.Resolve(mode, value, date, Completed);
            Assert.True(mode == TaskDueDateMode.None || due is not null, $"{mode} resolved to no date.");
        }
    }

    [Theory]
    [InlineData(2026, 1, 15, "2026-04-01", "2026-03-31")]
    [InlineData(2026, 12, 5, "2027-01-01", "2026-12-31")]
    [InlineData(2026, 3, 31, "2026-04-01", "2026-03-31")]
    [InlineData(2026, 7, 1, "2026-10-01", "2026-09-30")]
    public void The_quarters_are_counted_from_the_completion_date(int year, int month, int day, string next, string end)
    {
        var completed = new DateOnly(year, month, day);

        Assert.Equal(DateOnly.Parse(next), TaskDueDate.Resolve(TaskDueDateMode.NextQuarter, null, null, completed));
        Assert.Equal(DateOnly.Parse(end), TaskDueDate.Resolve(TaskDueDateMode.EndOfQuarter, null, null, completed));
    }

    [Fact]
    public void A_month_later_than_the_thirty_first_lands_on_the_last_day_of_the_month()
    {
        var due = TaskDueDate.Resolve(TaskDueDateMode.InMonths, 1, null, new DateOnly(2026, 1, 31));

        Assert.Equal(new DateOnly(2026, 2, 28), due);
    }

    [Theory]
    [InlineData(TaskDueDateMode.InDays, null)]
    [InlineData(TaskDueDateMode.InDays, 0)]
    [InlineData(TaskDueDateMode.InDays, 4000)]
    [InlineData(TaskDueDateMode.InWeeks, 600)]
    [InlineData(TaskDueDateMode.InMonths, 500)]
    [InlineData(TaskDueDateMode.InYears, 200)]
    public void A_count_that_is_missing_or_out_of_range_is_refused(TaskDueDateMode mode, int? value)
    {
        Assert.NotNull(TaskDueDate.Validate(mode, value, null));
        Assert.Null(TaskDueDate.Resolve(mode, value, null, Completed));
    }

    [Fact]
    public void A_picked_date_that_was_never_picked_is_refused()
    {
        Assert.NotNull(TaskDueDate.Validate(TaskDueDateMode.OnDate, null, null));
    }
}
