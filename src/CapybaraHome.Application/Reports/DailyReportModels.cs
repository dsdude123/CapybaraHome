using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Application.Reports;

/// <summary>One period of the forecast, as the service words it.</summary>
/// <param name="Name">What the period is called, such as a day or a night.</param>
/// <param name="Condition">The sky in a few words.</param>
/// <param name="Temperature">The temperature, in degrees Celsius, as written.</param>
/// <param name="Wind">The wind speed and direction, already joined.</param>
public record WeatherPeriod(string Name, string Condition, string Temperature, string Wind);

/// <summary>A task the report lists.</summary>
/// <param name="Id">The task's own id, which the report shows in place of the old issue key.</param>
/// <param name="Title">Short description of the work.</param>
/// <param name="DueDate">Day it is due.</param>
/// <param name="IsOverdue">Whether the due date has passed.</param>
/// <param name="IsDueToday">Whether it is due on the day of the report.</param>
public record ReportTask(int Id, string Title, DateOnly? DueDate, bool IsOverdue, bool IsDueToday);

/// <summary>A planned meal the report lists.</summary>
/// <param name="Id">The meal's own id.</param>
/// <param name="Title">The meal's name, with the planned variant named alongside it when there is one.</param>
/// <param name="Status">Whether the meal is planned or planned to share.</param>
public record ReportMeal(int Id, string Title, MealStatus Status);

/// <summary>An entry from a calendar feed.</summary>
/// <param name="Summary">What the entry is called.</param>
/// <param name="Date">Day it falls on, read in the server's time zone.</param>
/// <param name="Time">Clock time it starts at, or null when it takes the whole day.</param>
/// <param name="Location">Where it is, or an empty string.</param>
public record ReportEvent(string Summary, DateOnly Date, TimeOnly? Time, string Location);

/// <summary>A headline from a news source.</summary>
/// <param name="Title">The headline itself.</param>
/// <param name="Link">Where it was published.</param>
public record NewsHeadline(string Title, string Link);

/// <summary>Everything one morning's report says.</summary>
/// <param name="Date">The day the report is for, in the server's time zone.</param>
/// <param name="Weather">The forecast periods, in the order the service gives them.</param>
/// <param name="Tasks">Tasks due within a week, earliest first.</param>
/// <param name="Meals">Planned meals, by name.</param>
/// <param name="Events">Calendar entries for the next 30 days, earliest first.</param>
/// <param name="Holidays">Public holidays in the next 30 days, earliest first.</param>
/// <param name="Today">The calendars that show only today, each under its own heading.</param>
/// <param name="LocalNews">Local headlines.</param>
/// <param name="NationalNews">National headlines.</param>
public record DailyReport(
    DateOnly Date,
    IReadOnlyList<WeatherPeriod> Weather,
    IReadOnlyList<ReportTask> Tasks,
    IReadOnlyList<ReportMeal> Meals,
    IReadOnlyList<ReportEvent> Events,
    IReadOnlyList<ReportHoliday> Holidays,
    IReadOnlyList<ReportTodaySection> Today,
    IReadOnlyList<NewsHeadline> LocalNews,
    IReadOnlyList<NewsHeadline> NationalNews);

/// <summary>One calendar's entries for today, as its own short section.</summary>
/// <param name="Name">What the calendar is called, which the heading is built from.</param>
/// <param name="Entries">What it has on today.</param>
public record ReportTodaySection(string Name, IReadOnlyList<ReportEvent> Entries);

/// <summary>A public holiday the report lists.</summary>
/// <param name="Name">What the holiday is called.</param>
/// <param name="Date">Day it falls on.</param>
public record ReportHoliday(string Name, DateOnly Date);
