using System.Globalization;

namespace CapybaraHome.Web;

/// <summary>Turns dates into the words the UI shows.</summary>
public static class DateNames
{
    /// <summary>Returns <paramref name="date"/> as its full month name, day and year.</summary>
    public static string Long(DateOnly date) => date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>Returns when a meal was last cooked, in the words the meal lists use.</summary>
    public static string LastCooked(DateOnly? date) =>
        date is { } cooked ? $"Last cooked {Long(cooked)}" : "Never cooked";
}
