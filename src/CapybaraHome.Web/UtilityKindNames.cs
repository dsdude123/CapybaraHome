using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Web;

/// <summary>Turns the utility kinds into the words the UI shows.</summary>
public static class UtilityKindNames
{
    /// <summary>Every kind, in the order the UI offers them.</summary>
    public static readonly UtilityKind[] All =
    [
        UtilityKind.Electricity,
        UtilityKind.Gas,
        UtilityKind.Water,
        UtilityKind.Sewer,
        UtilityKind.Trash,
        UtilityKind.Internet,
        UtilityKind.Other,
    ];

    /// <summary>Returns the label for <paramref name="kind"/>.</summary>
    public static string Label(UtilityKind kind) => kind switch
    {
        UtilityKind.Electricity => "Electricity",
        UtilityKind.Gas => "Gas",
        UtilityKind.Water => "Water",
        UtilityKind.Sewer => "Sewer",
        UtilityKind.Trash => "Trash",
        UtilityKind.Internet => "Internet",
        UtilityKind.Other => "Other",
        _ => kind.ToString(),
    };

    /// <summary>Returns what to call a line, which is its own label when the kind is "other".</summary>
    public static string Label(UtilityKind kind, string? given) =>
        kind == UtilityKind.Other && !string.IsNullOrWhiteSpace(given) ? given : Label(kind);
}
