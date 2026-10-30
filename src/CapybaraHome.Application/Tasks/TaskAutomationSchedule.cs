using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Works out when an automation runs. Every occurrence is counted from the start date rather than
/// from the previous run, so a monthly automation that starts on the 31st lands on the 28th in
/// February and back on the 31st in March instead of walking backwards through the months.
/// </summary>
public static class TaskAutomationSchedule
{
    /// <summary>The smallest interval an automation can repeat on.</summary>
    public const int MinimumIntervalCount = 1;

    /// <summary>Returns the moment of the <paramref name="index"/>th occurrence, counting the start date as zero.</summary>
    /// <param name="unit">Unit the interval is counted in.</param>
    /// <param name="count">How many of <paramref name="unit"/> there are between runs.</param>
    /// <param name="startsOn">Day the interval is counted from.</param>
    /// <param name="timeOfDay">Time of day the run happens at, read in <paramref name="zone"/>.</param>
    /// <param name="index">Which occurrence to return, from zero.</param>
    /// <param name="zone">Time zone the start date and time of day are written in.</param>
    /// <exception cref="ArgumentOutOfRangeException">The occurrence falls outside the calendar.</exception>
    public static DateTimeOffset Occurrence(
        TaskIntervalUnit unit,
        int count,
        DateOnly startsOn,
        TimeOnly timeOfDay,
        int index,
        TimeZoneInfo zone)
    {
        var steps = Steps(unit, count, index);

        var date = unit switch
        {
            TaskIntervalUnit.Weeks => startsOn.AddDays(steps * 7),
            TaskIntervalUnit.Months => startsOn.AddMonths(steps),
            TaskIntervalUnit.Years => startsOn.AddYears(steps),
            _ => startsOn.AddDays(steps),
        };

        return ToInstant(date, timeOfDay, zone);
    }

    /// <summary>
    /// Returns the first occurrence strictly after <paramref name="after"/>, or null when the
    /// schedule runs off the end of the calendar before reaching one.
    /// </summary>
    /// <param name="unit">Unit the interval is counted in.</param>
    /// <param name="count">How many of <paramref name="unit"/> there are between runs.</param>
    /// <param name="startsOn">Day the interval is counted from.</param>
    /// <param name="timeOfDay">Time of day the run happens at, read in <paramref name="zone"/>.</param>
    /// <param name="after">Moment the occurrence has to fall after.</param>
    /// <param name="zone">Time zone the start date and time of day are written in.</param>
    public static DateTimeOffset? NextAfter(
        TaskIntervalUnit unit,
        int count,
        DateOnly startsOn,
        TimeOnly timeOfDay,
        DateTimeOffset after,
        TimeZoneInfo zone)
    {
        var index = Estimate(unit, count, startsOn, after, zone);

        try
        {
            while (true)
            {
                var moment = Occurrence(unit, count, startsOn, timeOfDay, index, zone);
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
    }

    /// <summary>
    /// Returns the next <paramref name="take"/> occurrences after <paramref name="after"/>, which
    /// is what the editor shows so the start date can be picked against real dates.
    /// </summary>
    /// <param name="unit">Unit the interval is counted in.</param>
    /// <param name="count">How many of <paramref name="unit"/> there are between runs.</param>
    /// <param name="startsOn">Day the interval is counted from.</param>
    /// <param name="timeOfDay">Time of day the run happens at, read in <paramref name="zone"/>.</param>
    /// <param name="after">Moment the first occurrence has to fall after.</param>
    /// <param name="take">How many occurrences to return.</param>
    /// <param name="zone">Time zone the start date and time of day are written in.</param>
    public static IReadOnlyList<DateTimeOffset> Upcoming(
        TaskIntervalUnit unit,
        int count,
        DateOnly startsOn,
        TimeOnly timeOfDay,
        DateTimeOffset after,
        int take,
        TimeZoneInfo zone)
    {
        var moments = new List<DateTimeOffset>();
        var from = after;

        for (var taken = 0; taken < take; taken++)
        {
            if (NextAfter(unit, count, startsOn, timeOfDay, from, zone) is not { } moment)
            {
                break;
            }

            moments.Add(moment);
            from = moment;
        }

        return moments;
    }

    /// <summary>
    /// How far past the start date the <paramref name="index"/>th occurrence is, in whole units.
    /// A long interval multiplied by a far-off index leaves the range a date can hold long before
    /// it leaves the range an int can, so the product is taken in wider arithmetic and reported as
    /// off the calendar rather than quietly wrapping into a date in the past.
    /// </summary>
    private static int Steps(TaskIntervalUnit unit, int count, int index)
    {
        var steps = (long)Math.Max(MinimumIntervalCount, count) * index;
        var widest = unit == TaskIntervalUnit.Weeks ? steps * 7 : steps;

        if (widest is > int.MaxValue or < int.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "The occurrence falls outside the calendar.");
        }

        return (int)steps;
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

    /// <summary>
    /// Returns the index of the last occurrence that cannot be after <paramref name="after"/>, so
    /// that the walk which follows only ever goes forward, and only by an occurrence or two: the
    /// elapsed days, months or years are divided by the interval and rounded down, which lands on
    /// an occurrence in an earlier day, month or year than <paramref name="after"/> itself.
    /// </summary>
    private static int Estimate(TaskIntervalUnit unit, int count, DateOnly startsOn, DateTimeOffset after, TimeZoneInfo zone)
    {
        var target = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        var days = target.DayNumber - startsOn.DayNumber;
        var months = ((target.Year - startsOn.Year) * 12) + target.Month - startsOn.Month;
        // In long arithmetic because a count near int.MaxValue overflows a week's multiplier.
        long step = Math.Max(MinimumIntervalCount, count);

        var guess = unit switch
        {
            TaskIntervalUnit.Weeks => days / (7 * step),
            TaskIntervalUnit.Months => months / step,
            TaskIntervalUnit.Years => (target.Year - startsOn.Year) / step,
            _ => days / step,
        };

        return (int)Math.Clamp(guess, 0, int.MaxValue);
    }
}
