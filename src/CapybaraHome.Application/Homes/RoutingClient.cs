using System.Net.Http.Json;
using System.Text.Json;

namespace CapybaraHome.Application.Homes;

/// <summary>A call to the routing service that did not come back with what was asked for.</summary>
/// <param name="message">What to tell whoever pressed the button.</param>
public class RoutingException(string message) : Exception(message);

/// <summary>Where an address is.</summary>
/// <param name="Label">The address as the service resolved it.</param>
/// <param name="Latitude">Its latitude.</param>
/// <param name="Longitude">Its longitude.</param>
public record GeocodedPlace(string Label, double Latitude, double Longitude);

/// <summary>How long a drive takes and how far it is.</summary>
/// <param name="Minutes">The drive in minutes.</param>
/// <param name="Miles">The drive in miles.</param>
public record MeasuredDrive(double Minutes, double Miles);

/// <summary>
/// Places addresses and measures drives through OpenRouteService. The hosted service at
/// api.openrouteservice.org and a self-hosted instance speak the same API, so only the base
/// address changes; both are read from the settings on every call, so no key is held in memory.
/// </summary>
public class RoutingClient(HttpClient http, HomeSearchService search)
{
    /// <summary>Metres in a mile, which the service's distances are turned into.</summary>
    private const double MetresPerMile = 1609.344d;

    /// <summary>Turns an address into coordinates, or returns null when the service found nothing.</summary>
    /// <param name="address">The address to look up.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="RoutingException">No service is configured, or it did not answer.</exception>
    public async Task<GeocodedPlace?> PlaceAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new RoutingException("There is no address to look up.");
        }

        var (baseUrl, key) = await ConnectionAsync(cancellationToken);
        var path = $"geocode/search?text={Uri.EscapeDataString(address)}&size=1";

        using var document = await ReadAsync(HttpMethod.Get, baseUrl, path, key, null, cancellationToken);

        if (!document.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array
            || features.GetArrayLength() == 0)
        {
            return null;
        }

        var feature = features[0];
        if (!feature.TryGetProperty("geometry", out var geometry)
            || !geometry.TryGetProperty("coordinates", out var coordinates)
            || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2)
        {
            throw new RoutingException("The geocoder answered without any coordinates.");
        }

        var label = feature.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty("label", out var node)
                ? node.GetString()
                : null;

        // GeoJSON orders a position longitude first, which is the opposite of how they are written.
        return new GeocodedPlace(label ?? address, coordinates[1].GetDouble(), coordinates[0].GetDouble());
    }

    /// <summary>Measures the drive between two points, or returns null when the service found no route.</summary>
    /// <param name="fromLatitude">Latitude driven from.</param>
    /// <param name="fromLongitude">Longitude driven from.</param>
    /// <param name="toLatitude">Latitude driven to.</param>
    /// <param name="toLongitude">Longitude driven to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="RoutingException">No service is configured, or it did not answer.</exception>
    public async Task<MeasuredDrive?> MeasureAsync(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude,
        CancellationToken cancellationToken = default)
    {
        var (baseUrl, key) = await ConnectionAsync(cancellationToken);

        var body = new
        {
            coordinates = new[]
            {
                new[] { fromLongitude, fromLatitude },
                new[] { toLongitude, toLatitude },
            },
        };

        using var document = await ReadAsync(
            HttpMethod.Post, baseUrl, "v2/directions/driving-car", key, body, cancellationToken);

        if (!document.RootElement.TryGetProperty("routes", out var routes)
            || routes.ValueKind != JsonValueKind.Array
            || routes.GetArrayLength() == 0)
        {
            return null;
        }

        if (!routes[0].TryGetProperty("summary", out var summary)
            || !summary.TryGetProperty("duration", out var duration)
            || !summary.TryGetProperty("distance", out var distance))
        {
            // A pair of points with no road between them comes back as a route with an empty
            // summary rather than as an error.
            return null;
        }

        return new MeasuredDrive(duration.GetDouble() / 60d, distance.GetDouble() / MetresPerMile);
    }

    private async Task<(string BaseUrl, string? Key)> ConnectionAsync(CancellationToken cancellationToken)
    {
        var settings = await search.GetSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.RoutingBaseUrl))
        {
            throw new RoutingException("No routing service is set up yet, so drive times have to be typed in.");
        }

        return (settings.RoutingBaseUrl.TrimEnd('/'), await search.GetRoutingKeyAsync(cancellationToken));
    }

    private async Task<JsonDocument> ReadAsync(
        HttpMethod method,
        string baseUrl,
        string path,
        string? key,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{baseUrl}/{path}");

        // OpenRouteService takes the key as a bare Authorization value rather than as a scheme,
        // and a self-hosted instance takes no key at all.
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.TryAddWithoutValidation("Authorization", key);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RoutingException($"The routing service at {baseUrl} did not answer: {ex.Message}");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new RoutingException(
                    $"The routing service answered {(int)response.StatusCode}: {Shorten(payload)}");
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException ex)
            {
                throw new RoutingException($"The routing service answered something that is not JSON: {ex.Message}");
            }
        }
    }

    // The message goes on a page, so a service that answers with a wall of HTML is cut short.
    private static string Shorten(string payload)
    {
        var text = payload.Trim().ReplaceLineEndings(" ");
        return text.Length <= 300 ? text : text[..300] + "…";
    }
}
