namespace CapybaraHome.Domain.Homes;

/// <summary>
/// A picture of a property. The bytes are on disk beside the database rather than in it, so an
/// export stays small enough to read; this row is the name the file is served under.
/// </summary>
public class PropertyPhoto
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property the picture is of.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>Name of the file on disk, which the app chose rather than the browser.</summary>
    public required string StoredName { get; set; }

    /// <summary>Name the file was uploaded under, shown when it is downloaded.</summary>
    public required string FileName { get; set; }

    /// <summary>Media type the file is served as.</summary>
    public required string ContentType { get; set; }

    /// <summary>How large the file is, in bytes.</summary>
    public long ByteCount { get; set; }

    /// <summary>What the picture shows.</summary>
    public string? Caption { get; set; }

    /// <summary>Position in the property's pictures; lower sorts first.</summary>
    public int SortOrder { get; set; }

    /// <summary>When the picture was uploaded, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
