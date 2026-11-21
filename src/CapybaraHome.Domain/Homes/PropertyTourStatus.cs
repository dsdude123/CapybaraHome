namespace CapybaraHome.Domain.Homes;

/// <summary>Where a submitted tour stands with the model that reads it.</summary>
public enum PropertyTourStatus
{
    /// <summary>Waiting for, or with, the model.</summary>
    Processing,

    /// <summary>The last attempt stopped on an error and can be tried again.</summary>
    Failed,

    /// <summary>Read, and the property updated from it.</summary>
    Done,

    /// <summary>Failed and set aside without being tried again.</summary>
    Dismissed,
}
