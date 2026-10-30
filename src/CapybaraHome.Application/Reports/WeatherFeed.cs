using System.Globalization;
using System.Text.Json;

namespace CapybaraHome.Application.Reports;

/// <summary>Reads the forecast the report opens with.</summary>
public class WeatherFeed(HttpClient client)
{
    /// <summary>How many periods the forecast service answers with.</summary>
    private const int PeriodCount = 4;

    /// <summary>Returns the forecast periods from <paramref name="url"/>.</summary>
    public async Task<IReadOnlyList<WeatherPeriod>> ReadAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var periods = new List<WeatherPeriod>(PeriodCount);
        for (var n = 1; n <= PeriodCount; n++)
        {
            var prefix = $"period{n}";
            var wind = $"{Field(root, prefix + "WindSpeed")} {Field(root, prefix + "WindDirection")}".Trim();
            periods.Add(new WeatherPeriod(
                Field(root, prefix + "Name"),
                Field(root, prefix + "Condition"),
                Field(root, prefix + "Temp"),
                wind));
        }

        return periods;
    }

    /// <summary>Returns a field as text, whether the service wrote it as a string or as a number.</summary>
    private static string Field(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetDouble().ToString("0.##", CultureInfo.InvariantCulture),
            JsonValueKind.True or JsonValueKind.False => value.GetBoolean().ToString(),
            _ => string.Empty,
        };
    }
}
