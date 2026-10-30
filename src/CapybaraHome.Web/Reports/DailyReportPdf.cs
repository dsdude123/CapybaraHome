using CapybaraHome.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CapybaraHome.Web.Reports;

/// <summary>Lays the report out as the sheet of paper that comes off the printer.</summary>
public static class DailyReportPdf
{
    /// <summary>The font the library carries with it, so the image needs none installed.</summary>
    private const string Font = "Lato";

    /// <summary>What an overdue task is written in.</summary>
    private const string OverdueColour = "#c0392b";

    /// <summary>What a task due today is written in.</summary>
    private const string TodayColour = "#e67e22";

    static DailyReportPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // A meal or a task written in a script the font has no glyphs for is worth a gap on the
        // page; it is not worth losing the morning's report over.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    /// <summary>Returns <paramref name="report"/> as a PDF.</summary>
    public static byte[] Render(DailyReport report) => Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(1, Unit.Inch);
            page.DefaultTextStyle(text => text.FontFamily(Font).FontSize(11).FontColor("#111111"));

            page.Content().Column(column =>
            {
                column.Spacing(14);

                column.Item().Column(title =>
                {
                    title.Item().Text(DailyReportText.Heading(report.Date)).FontSize(16).Bold();
                    title.Item().PaddingTop(4).LineHorizontal(2).LineColor("#111111");
                });

                Section(column, "Weather Forecast", body => Weather(body, report));
                Section(column, "Tasks (due within 7 days)", body => Tasks(body, report));
                Section(column, "Meal Plan", body => Meals(body, report));
                Section(column, "Upcoming Events (30 days)", body => Events(body, report));
                Section(column, "Upcoming Holidays (Next 30 Days)", body => Holidays(body, report));

                foreach (var today in report.Today)
                {
                    Section(column, DailyReportText.TodayHeading(today), body => body
                        .Text(string.Join("\n", today.Entries.Select(DailyReportText.Game))));
                }

                Section(column, "Local News (KING 5)", body => Headlines(body, report.LocalNews));
                Section(column, "National News (AP)", body => Headlines(body, report.NationalNews));
            });
        });
    }).GeneratePdf();

    /// <summary>Writes one heading and the block that belongs under it.</summary>
    private static void Section(ColumnDescriptor column, string heading, Action<IContainer> body)
    {
        column.Item().Column(section =>
        {
            section.Item().Text(heading).FontSize(13).Bold();
            section.Item().PaddingTop(2).PaddingBottom(6).LineHorizontal(1).LineColor("#888888");
            body(section.Item());
        });
    }

    private static void Weather(IContainer body, DailyReport report)
    {
        if (report.Weather.Count == 0)
        {
            body.Text("Weather data unavailable.");
            return;
        }

        body.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
            });

            Head(table, "Period", "Condition", "Temp", "Wind");

            foreach (var period in report.Weather)
            {
                table.Cell().Element(Body).Text(period.Name);
                table.Cell().Element(Body).Text(period.Condition);
                table.Cell().Element(Body).Text(DailyReportText.Temperature(period.Temperature));
                table.Cell().Element(Body).Text(period.Wind);
            }
        });
    }

    private static void Tasks(IContainer body, DailyReport report)
    {
        if (report.Tasks.Count == 0)
        {
            body.Text("No tasks due within 7 days.");
            return;
        }

        body.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1);
                columns.RelativeColumn(5);
                columns.RelativeColumn(2);
            });

            Head(table, "ID", "Title", "Due Date");

            foreach (var task in report.Tasks)
            {
                var style = (TextSpanDescriptor span) =>
                {
                    if (task.IsOverdue)
                    {
                        span.FontColor(OverdueColour).Bold();
                    }
                    else if (task.IsDueToday)
                    {
                        span.FontColor(TodayColour).Bold();
                    }
                };

                style(table.Cell().Element(Body).Text($"#{task.Id}"));
                style(table.Cell().Element(Body).Text(task.Title));
                style(table.Cell().Element(Body).Text(DailyReportText.DueDate(task.DueDate)));
            }
        });
    }

    private static void Meals(IContainer body, DailyReport report)
    {
        if (report.Meals.Count == 0)
        {
            body.Text("No meals planned.");
            return;
        }

        body.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1);
                columns.RelativeColumn(5);
                columns.RelativeColumn(2);
            });

            Head(table, "ID", "Title", "Status");

            foreach (var meal in report.Meals)
            {
                table.Cell().Element(Body).Text($"#{meal.Id}");
                table.Cell().Element(Body).Text(meal.Title);
                table.Cell().Element(Body).Text(MealStatusNames.Label(meal.Status));
            }
        });
    }

    private static void Events(IContainer body, DailyReport report)
    {
        if (report.Events.Count == 0)
        {
            body.Text("No upcoming events.");
            return;
        }

        body.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn(5);
                columns.RelativeColumn(3);
            });

            Head(table, "Date", "Event", "Location");

            foreach (var entry in report.Events)
            {
                table.Cell().Element(Body).Text(DailyReportText.When(entry));
                table.Cell().Element(Body).Text(entry.Summary);
                table.Cell().Element(Body).Text(entry.Location);
            }
        });
    }

    private static void Holidays(IContainer body, DailyReport report)
    {
        if (report.Holidays.Count == 0)
        {
            body.Text("No holidays in the next 30 days.");
            return;
        }

        body.Column(list =>
        {
            foreach (var holiday in report.Holidays)
            {
                list.Item().Text($"•  {DailyReportText.Holiday(holiday)}");
            }
        });
    }

    private static void Headlines(IContainer body, IReadOnlyList<NewsHeadline> headlines)
    {
        if (headlines.Count == 0)
        {
            body.Text("No headlines available.");
            return;
        }

        body.Column(list =>
        {
            for (var n = 0; n < headlines.Count; n++)
            {
                list.Item().Text($"{n + 1}.  {headlines[n].Title}");
            }
        });
    }

    /// <summary>Writes a table's header row.</summary>
    private static void Head(TableDescriptor table, params string[] headings)
    {
        table.Header(header =>
        {
            foreach (var heading in headings)
            {
                header.Cell().Element(HeaderCell).Text(heading).Bold();
            }
        });
    }

    private static IContainer HeaderCell(IContainer cell) => cell
        .Background("#f0f0f0")
        .BorderBottom(1)
        .BorderColor("#555555")
        .PaddingVertical(3)
        .PaddingHorizontal(6);

    private static IContainer Body(IContainer cell) => cell
        .BorderBottom(1)
        .BorderColor("#dddddd")
        .PaddingVertical(3)
        .PaddingHorizontal(6);
}
