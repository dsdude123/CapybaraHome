namespace CapybaraHome.Domain.Homes;

/// <summary>Where a drive time came from.</summary>
public enum DriveTimeSource
{
    /// <summary>Typed in by hand.</summary>
    Manual,

    /// <summary>Measured by the routing service.</summary>
    Measured,
}
