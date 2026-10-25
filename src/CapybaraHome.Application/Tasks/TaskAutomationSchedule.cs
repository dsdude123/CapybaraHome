using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Works out when an automation runs. Every occurrence is counted from the start date rather than
/// from the previous run, so a monthly automation that starts on the 31st lands on the 28th in
/// February and back on the 31st in March instead of walking backwards through the months.
/// </summary>
public static class TaskAutomationSchedule
{
    // An estimate from the elapsed span lands within an occurrence or two of the answer, so the
    // walk that follows it is short. The cap is only there to keep a nonsense date from looping.
    private const int MaxSteps = 1000;

    /// <summary>Returns the moment of the <paramref name="index"/>th occurrence, counting the start date as zero.</summary>
    /// <param name="frequency">How often the automation runs.</param>
    /// <param name="startsOn">Day the interval is counted from.</param>
    /// <param name="timeOfDay">Time of day the run happens at, read in <paramref name="zone"/>.</param>
    /// <param name="index">Which occurrence to return, from zero.</param>
    /// <param name="zone">Time zone the start date and time of day are written in.</param>
    public static DateTimeOffset Occurrence(
        TaskAutomationFrequency frequency,
        DateOnly startsOn,
        TimeOnly timeOfDay,
        int index,
        TimeZoneInfo zone)
    {
        var date = frequency switch
        {
            TaskAutomationFrequency.Daily => startsOn.AddDays(index),
            TaskAutomationFrequency.Weekly => startsOn.AddDays(7 * index),
            TaskAutomationFrequency.BiWeekly => startsOn.AddDays(14 * index),
            TaskAutomationFrequency.Monthly => startsOn.AddMonths(index),
            TaskAutomationFrequency.Quarterly => startsOn.AddMonths(3 * index),
            TaskAutomationFrequency.Yearly => startsOn.AddYears(index),
            _ => startsOn.AddDays(7 * index),
        };

        return ToInstant(date, timeOfDay, zone);
    }

    /// <summary>
    /// Returns the first occurrence strictly after <paramref name="after"/>, or null when the
    /// schedule runs off the end of the calendar before reaching one.
    /// </summary>
    /// <param name="frequency">How often the automation runs.</param>
    /// <param name="startsOn">Day the interval is counted from.</param>
    /// <param name="timeOfDay">Time of day the run happens at, read in <paramref name="zone"/>.</param>
    /// <param name="after">Moment the occurrence has to fall after.</param>
    /// <param name="zone">Time zone the start date and time of day are written in.</param>
    public static DateTimeOffset? NextAfter(
        TaskAutomationFrequency frequency,
        DateOnly startsOn,
        TimeOnly timeOfDay,
        DateTimeOffset after,
        TimeZoneInfo zone)
    {
        var index = Estimate(frequency, startsOn, after, zone);

        try
        {
            while (index > 0 && Occurrence(frequency, startsOn, timeOfDay, index - 1, zone) > after)
            {
                index--;
            }

            for (var step = 0; step < MaxSteps; step++)
            {
                var moment = Occurrence(frequency, startsOn, timeOfDay, index, zone);
                if (moment > after)
                {
                    return moment;
                }

                index++;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Returns the next <paramref name="count"/> occurrences after <paramref name="after"/>, which
    /// is what the editor shows so the start date can be picked against real dates.
    /// </summary>
    /// <param name="frequency">How often the automation runs.</param>
    /// <param name="startsOn">Day the interval is counted from.</param>
    /// <param name="timeOfDay">Time of day the run happens at, read in <paramref name="zone"/>.</param>
    /// <param name="after">Moment the first occurrence has to fall after.</param>
    /// <param name="count">How many occurrences to return.</param>
    /// <param name="zone">Time zone the start date and time of day are written in.</param>
    public static IReadOnlyList<DateTimeOffset> Upcoming(
        TaskAutomationFrequency frequency,
        DateOnly startsOn,
        TimeOnly timeOfDay,
        DateTimeOffset after,
        int count,
        TimeZoneInfo zone)
    {
        var moments = new List<DateTimeOffset>();
        var from = after;

        for (var taken = 0; taken < count; taken++)
        {
            if (NextAfter(frequency, startsOn, timeOfDay, from, zone) is not { } moment)
            {
                break;
            }

            moments.Add(moment);
            from = moment;
        }

        return moments;
    }

    /// <summary>
    /// Turns a day and a time of day written in <paramref name="zone"/> into the instant it names.
    /// The hour the clocks go forward has no such wall time, so a run configured inside it moves to
    /// the first minute that does exist; an hour that happens twice runs on its second pass, which
    /// is what the zone's own offset lookup reports.
    /// </summary>
    private static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time);

        for (var minutes = 0; minutes < 24 * 60 && zone.IsInvalidTime(local); minutes++)
        {
            local = local.AddMinutes(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Returns a starting guess at the index of the occurrence around <paramref name="after"/>.</summary>
    private static int Estimate(TaskAutomationFrequency frequency, DateOnly startsOn, DateTimeOffset after, TimeZoneInfo zone)
    {
        var target = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        var days = target.DayNumber - startsOn.DayNumber;
        var months = ((target.Year - startsOn.Year) * 12) + target.Month - startsOn.Month;

        var guess = frequency switch
        {
            TaskAutomationFrequency.Daily => days,
            TaskAutomationFrequency.Weekly => days / 7,
            TaskAutomationFrequency.BiWeekly => days / 14,
            TaskAutomationFrequency.Monthly => months,
            TaskAutomationFrequency.Quarterly => months / 3,
            TaskAutomationFrequency.Yearly => target.Year - startsOn.Year,
            _ => days / 7,
        };

        return Math.Max(0, guess);
    }
}
