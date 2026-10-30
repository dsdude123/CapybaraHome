using CapybaraHome.Web.Auth;
using CapybaraHome.Web.Reports;

namespace CapybaraHome.Web.Endpoints;

/// <summary>Hands the report over as a file, which a statically rendered page cannot do itself.</summary>
public static class ReportEndpoints
{
    /// <summary>Maps the report's PDF download.</summary>
    public static void MapReport(this IEndpointRouteBuilder app)
    {
        app.MapGet("/report/pdf", async (DailyReportPrinting printing, CancellationToken cancellationToken) =>
        {
            var report = await printing.BuildAsync(cancellationToken);
            var name = $"morning-report-{report.Date:yyyy-MM-dd}.pdf";
            return Results.File(DailyReportPdf.Render(report), "application/pdf", name);
        }).RequireAuthorization(AuthenticationSetup.ApiPolicyName);
    }
}
