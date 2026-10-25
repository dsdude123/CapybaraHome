using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Web;

/// <summary>Turns the automation frequencies into the words the UI shows.</summary>
public static class FrequencyNames
{
    /// <summary>Every frequency, in the order the picker offers them.</summary>
    public static readonly TaskAutomationFrequency[] All =
    [
        TaskAutomationFrequency.Daily,
        TaskAutomationFrequency.Weekly,
        TaskAutomationFrequency.BiWeekly,
        TaskAutomationFrequency.Monthly,
        TaskAutomationFrequency.Quarterly,
        TaskAutomationFrequency.Yearly,
    ];

    /// <summary>Returns the label for <paramref name="frequency"/>.</summary>
    public static string Label(TaskAutomationFrequency frequency) => frequency switch
    {
        TaskAutomationFrequency.Daily => "Every day",
        TaskAutomationFrequency.Weekly => "Every week",
        TaskAutomationFrequency.BiWeekly => "Every two weeks",
        TaskAutomationFrequency.Monthly => "Every month",
        TaskAutomationFrequency.Quarterly => "Every three months",
        TaskAutomationFrequency.Yearly => "Every year",
        _ => frequency.ToString(),
    };
}
