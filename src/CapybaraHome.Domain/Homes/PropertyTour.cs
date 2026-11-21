namespace CapybaraHome.Domain.Homes;

/// <summary>
/// One tour of a property as it was submitted: what was written, which files came with it, and
/// how far the model has got with reading them.
/// </summary>
public class PropertyTour
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property that was toured.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>Where the tour stands with the model.</summary>
    public PropertyTourStatus Status { get; set; }

    /// <summary>What was typed on the tour, or null when nothing was.</summary>
    public string? Notes { get; set; }

    /// <summary>The pictures uploaded with the tour.</summary>
    public List<int> PhotoIds { get; set; } = [];

    /// <summary>The attachment holding the tour's video, or null when none was uploaded.</summary>
    public int? VideoAttachmentId { get; set; }

    /// <summary>
    /// What was said in the video, kept once it has been worked out so that trying again does not
    /// transcribe it a second time.
    /// </summary>
    public string? Transcript { get; set; }

    /// <summary>Why the last attempt failed, or null when it has not.</summary>
    public string? Error { get; set; }

    /// <summary>When the tour was submitted, in UTC.</summary>
    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>When the model finished with it, in UTC.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}
