using CapybaraHome.Domain.Tasks;
using CapybaraHome.Web;

namespace CapybaraHome.Tests;

/// <summary>Covers the words the automation lists and the editor put on a repeat interval.</summary>
public class IntervalNamesTests
{
    [Theory]
    [InlineData(TaskIntervalUnit.Days, 1, "Every day")]
    [InlineData(TaskIntervalUnit.Weeks, 1, "Every week")]
    [InlineData(TaskIntervalUnit.Months, 1, "Every month")]
    [InlineData(TaskIntervalUnit.Years, 1, "Every year")]
    [InlineData(TaskIntervalUnit.Days, 13, "Every 13 days")]
    [InlineData(TaskIntervalUnit.Weeks, 2, "Every 2 weeks")]
    [InlineData(TaskIntervalUnit.Months, 9, "Every 9 months")]
    [InlineData(TaskIntervalUnit.Years, 3, "Every 3 years")]
    public void Every_interval_reads_as_itself(TaskIntervalUnit unit, int count, string expected)
    {
        Assert.Equal(expected, IntervalNames.Label(unit, count));
    }

    [Fact]
    public void The_picker_offers_every_unit()
    {
        Assert.Equal(Enum.GetValues<TaskIntervalUnit>(), IntervalNames.All);
    }
}
