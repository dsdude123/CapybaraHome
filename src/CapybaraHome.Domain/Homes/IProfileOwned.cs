namespace CapybaraHome.Domain.Homes;

/// <summary>Something that belongs to one scenario of the search rather than to the search itself.</summary>
public interface IProfileOwned
{
    /// <summary>The scenario it belongs to.</summary>
    int ProfileId { get; set; }
}
