namespace CapybaraHome.Domain.Homes;

/// <summary>
/// The one row holding the routing service and the tour model the search uses, which are the
/// same whichever scenario is active; the scoring weights belong to a <see cref="SearchProfile"/>.
/// </summary>
public class HomeSearchSettings
{
    /// <summary>Primary key of the single row.</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Base address of the OpenRouteService instance, which both places an address and measures
    /// the drive. Empty until it is set, and then every drive time is typed in by hand.
    /// </summary>
    public string? RoutingBaseUrl { get; set; }

    /// <summary>Key the service is called with.</summary>
    public string? RoutingApiKey { get; set; }

    /// <summary>
    /// Base address of the OpenAI-compatible service that reads a tour, such as
    /// <c>http://host.docker.internal:8080/v1</c>. Empty until it is set.
    /// </summary>
    public string? TourAiBaseUrl { get; set; }

    /// <summary>Model that captions the pictures and analyses the tour.</summary>
    public string? TourAiModel { get; set; }

    /// <summary>Model that transcribes the video.</summary>
    public string? TourTranscriptionModel { get; set; }

    /// <summary>How long one call to the service may take, in seconds.</summary>
    public int TourAiTimeoutSeconds { get; set; } = 3600;

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
