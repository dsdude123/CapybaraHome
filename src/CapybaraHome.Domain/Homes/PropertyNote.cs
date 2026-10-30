namespace CapybaraHome.Domain.Homes;

/// <summary>A note written about a property, kept with the day it was written.</summary>
public class PropertyNote
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Property the note is about.</summary>
    public int PropertyId { get; set; }

    /// <summary>Navigation to <see cref="PropertyId"/>.</summary>
    public Property? Property { get; set; }

    /// <summary>What was written, held as markdown.</summary>
    public required string Body { get; set; }

    /// <summary>When the note was written, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
