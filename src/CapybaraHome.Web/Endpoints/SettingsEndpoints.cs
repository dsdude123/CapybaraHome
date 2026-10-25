using CapybaraHome.Application.DataTransfer;
using Microsoft.AspNetCore.Mvc;

namespace CapybaraHome.Web.Endpoints;

/// <summary>Form posts from the settings screen that a statically rendered page cannot handle itself.</summary>
public static class SettingsEndpoints
{
    /// <summary>Largest export file the import will read, far above anything this app writes.</summary>
    private const long MaxImportBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Maps the export-file upload. Blazor's static form binding does not carry files, so the
    /// form posts here and this sends the browser back to the settings page with the outcome.
    /// </summary>
    public static void MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapPost("/settings/import", async (
            IFormFile file,
            [FromForm] ImportMode? mode,
            DataTransferService service,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Back(error: "That file is empty.");
            }

            if (file.Length > MaxImportBytes)
            {
                return Back(error: $"{file.FileName} is larger than {MaxImportBytes / (1024 * 1024)} MB, so it is not an export from here.");
            }

            string json;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                json = await reader.ReadToEndAsync(cancellationToken);
            }

            try
            {
                var summary = await service.ImportFromJsonAsync(json, mode ?? ImportMode.Merge, cancellationToken);
                return Back(message:
                    $"Added {summary.CategoriesAdded} categories, {summary.TasksAdded} tasks, {summary.MealsAdded} meals, "
                    + $"{summary.AutomationsAdded} automations and {summary.CardsAdded} cards; updated "
                    + $"{summary.TasksUpdated} tasks, {summary.MealsUpdated} meals and {summary.AutomationsUpdated} automations.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
            {
                return Back(error: $"{file.FileName} is not an export this build can read: {ex.Message}");
            }
        });
    }

    private static IResult Back(string? message = null, string? error = null)
    {
        var query = message is not null
            ? $"?imported={Uri.EscapeDataString(message)}"
            : $"?importError={Uri.EscapeDataString(error ?? string.Empty)}";

        return Results.Redirect("/settings" + query);
    }
}
