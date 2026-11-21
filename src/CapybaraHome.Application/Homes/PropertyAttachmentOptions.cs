namespace CapybaraHome.Application.Homes;

/// <summary>Where the property attachments are kept, bound from the <c>Attachments</c> configuration section.</summary>
public class PropertyAttachmentOptions
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "Attachments";

    /// <summary>Directory the files are written to. A volume mount in a container.</summary>
    public string Path { get; set; } = "attachments";

    /// <summary>Largest file that will be accepted, in bytes. A tour video is the reason it is this large.</summary>
    public long MaxBytes { get; set; } = 8L * 1024 * 1024 * 1024;
}
