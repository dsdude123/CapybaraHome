namespace CapybaraHome.Domain.Homes;

/// <summary>
/// A file kept with a property, such as the notes or the video from a tour. The bytes are on disk
/// beside the database; this row is the name the file is downloaded under.
/// </summary>
public class PropertyAttachment
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property the file is kept with.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>Name of the file on disk, which the app chose rather than the browser.</summary>
    public required string StoredName { get; set; }

    /// <summary>Name the file is downloaded under.</summary>
    public required string FileName { get; set; }

    /// <summary>Media type the file is served as.</summary>
    public required string ContentType { get; set; }

    /// <summary>How large the file is, in bytes.</summary>
    public long ByteCount { get; set; }

    /// <summary>When the file was attached, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
