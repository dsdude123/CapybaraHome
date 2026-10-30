namespace CapybaraHome.Application.Homes;

/// <summary>Where the property pictures are kept, bound from the <c>Photos</c> configuration section.</summary>
public class PropertyPhotoOptions
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "Photos";

    /// <summary>Directory the picture files are written to. A volume mount in a container.</summary>
    public string Path { get; set; } = "photos";

    /// <summary>Largest picture that will be accepted, in bytes.</summary>
    public long MaxBytes { get; set; } = 16L * 1024 * 1024;
}
