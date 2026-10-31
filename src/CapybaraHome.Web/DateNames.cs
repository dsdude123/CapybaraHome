using System.Globalization;

namespace CapybaraHome.Web;

/// <summary>Turns dates into the words the UI shows.</summary>
public static class DateNames
{
    /// <summary>Returns <paramref name="date"/> as its full month name, day and year.</summary>
    public static string Long(DateOnly date) => date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns <paramref name="moment"/> as a day and a clock time, read in the time zone the
    /// server runs in. The automations' execution times are picked and shown in that zone, so an
    /// instant stored in UTC has to be converted before it is written out.
    /// </summary>
    public static string LongWithTime(DateTimeOffset moment, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(moment, zone).ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Returns the name of <paramref name="zone"/> as the UI labels times with it.</summary>
    public static string ZoneLabel(TimeZoneInfo zone) => zone.Id;

    /// <summary>Returns the day <paramref name="moment"/> fell on, in the server's own zone.</summary>
    public static string Day(DateTimeOffset moment) =>
        Long(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, TimeZoneInfo.Local).DateTime));

    /// <summary>Returns when a meal was last cooked, in the words the meal lists use.</summary>
    public static string LastCooked(DateOnly? date) =>
        date is { } cooked ? $"Last cooked {Long(cooked)}" : "Never cooked";
}
