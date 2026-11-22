using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Application.Homes;

/// <summary>A tour that cannot be taken or read, with the reason to show.</summary>
/// <param name="message">What to tell whoever submitted it.</param>
public class PropertyTourException(string message) : Exception(message);

/// <summary>A file that arrived with a tour.</summary>
/// <param name="FileName">Name the file was uploaded under.</param>
/// <param name="ContentType">Media type the browser gave.</param>
/// <param name="Open">Opens the bytes.</param>
public record TourUpload(string FileName, string? ContentType, Func<Stream> Open);

/// <summary>A submitted tour, as the property page shows it.</summary>
/// <param name="Id">The tour's id.</param>
/// <param name="PropertyId">The property that was toured.</param>
/// <param name="Status">Where it stands with the model.</param>
/// <param name="Error">Why the last attempt failed.</param>
/// <param name="SubmittedAt">When it was submitted.</param>
public record PropertyTourView(int Id, int PropertyId, PropertyTourStatus Status, string? Error, DateTimeOffset SubmittedAt);

/// <summary>The service that reads a tour, as the settings page shows it.</summary>
/// <param name="BaseUrl">Base address of the OpenAI-compatible service.</param>
/// <param name="Model">Model that captions the pictures and analyses the tour.</param>
/// <param name="TranscriptionModel">Model that transcribes the video.</param>
/// <param name="TimeoutSeconds">How long one call may take, in seconds.</param>
public record TourSettingsView(string? BaseUrl, string? Model, string? TranscriptionModel, int TimeoutSeconds)
{
    /// <summary>Whether there is a service and a model to send a tour to.</summary>
    public bool CanProcess => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>What the model made of a tour.</summary>
/// <param name="Summary">The tour in a few paragraphs.</param>
/// <param name="Priorities">The priorities it could decide, by id, and whether each is met.</param>
/// <param name="Pros">Things in the property's favour.</param>
/// <param name="Cons">Things against it.</param>
/// <param name="Discrepancies">Where the tour disagrees with what was already recorded.</param>
/// <param name="Suggestions">Things that go beyond a priority, offered as pros or cons to add by hand.</param>
public record TourFindings(
    string Summary,
    IReadOnlyDictionary<int, bool> Priorities,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    IReadOnlyList<string> Discrepancies,
    IReadOnlyList<string>? Suggestions = null);
