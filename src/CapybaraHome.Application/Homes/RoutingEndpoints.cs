namespace CapybaraHome.Application.Homes;

/// <summary>
/// Where the routing calls go. HeiGIT retired <c>api.openrouteservice.org</c> in favour of
/// <c>api.heigit.org</c>, which serves routing under <c>/openrouteservice</c> and the Pelias
/// geocoder under <c>/pelias/v1</c>; a self-hosted instance still answers the plain
/// OpenRouteService paths, so the base address decides which pair of paths is used.
/// </summary>
public static class RoutingEndpoints
{
    /// <summary>The hosted service's address, which is what the settings screen suggests.</summary>
    public const string HostedBaseUrl = "https://api.heigit.org";

    /// <summary>The hosts that used to serve the hosted API and no longer answer.</summary>
    private static readonly string[] RetiredHosts =
    [
        "api.openrouteservice.org",
        "openrouteservice.org",
        "api.heigit.com",
    ];

    /// <summary>
    /// Tidies a base address and sends a retired host to the one that answers now, so an address
    /// saved before the move keeps working.
    /// </summary>
    /// <param name="baseUrl">The address as it was typed or saved.</param>
    public static string? Normalize(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');

        // A bare host would be sent as a relative address and never reach the service.
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        return Host(trimmed) is { } host && RetiredHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
            ? HostedBaseUrl
            : trimmed;
    }

    /// <summary>Whether the address names HeiGIT's hosted API rather than a self-hosted instance.</summary>
    /// <param name="baseUrl">A normalized base address.</param>
    public static bool IsHosted(string baseUrl) =>
        Host(baseUrl) is { } host
        && (host.Equals("heigit.org", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".heigit.org", StringComparison.OrdinalIgnoreCase));

    /// <summary>The path that looks an address up, with the address already escaped.</summary>
    /// <param name="baseUrl">A normalized base address.</param>
    /// <param name="address">The address to look up.</param>
    public static string Geocode(string baseUrl, string address) =>
        (IsHosted(baseUrl) ? "pelias/v1/search" : "geocode/search")
        + $"?text={Uri.EscapeDataString(address)}&size=1";

    /// <summary>The path that measures a drive by car.</summary>
    /// <param name="baseUrl">A normalized base address.</param>
    public static string Directions(string baseUrl) =>
        IsHosted(baseUrl) ? "openrouteservice/v2/directions/driving-car" : "v2/directions/driving-car";

    private static string? Host(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
}
