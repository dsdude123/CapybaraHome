namespace CapybaraHome.Domain.Homes;

/// <summary>The kinds of utility a property's monthly costs are broken down by.</summary>
public enum UtilityKind
{
    /// <summary>Electricity.</summary>
    Electricity,

    /// <summary>Gas.</summary>
    Gas,

    /// <summary>Water.</summary>
    Water,

    /// <summary>Sewer.</summary>
    Sewer,

    /// <summary>Rubbish collection.</summary>
    Trash,

    /// <summary>Internet, where it is billed on top of the rent.</summary>
    Internet,

    /// <summary>Anything else, named by the line itself.</summary>
    Other,
}
