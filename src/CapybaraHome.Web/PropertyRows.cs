using System.Globalization;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Web;

/// <summary>One line of a property's utility breakdown as the editor holds it while it is typed.</summary>
/// <param name="Kind">Which utility it is.</param>
/// <param name="Label">What the line is called, as typed.</param>
/// <param name="MonthlyCost">The monthly cost, as typed.</param>
public record UtilityRow(UtilityKind Kind, string? Label, string? MonthlyCost);

/// <summary>One line of a list of plain text, such as an amenity or a pro, as the editor holds it.</summary>
/// <param name="Text">What was typed.</param>
public record TextRow(string? Text);

/// <summary>
/// Reads the repeated rows out of a posted property editor. The editor holds four lists in one
/// form, so each list owns a field name and the remove buttons carry the list they belong to.
/// </summary>
public static class PropertyRows
{
    /// <summary>Field name the utility kinds are posted under.</summary>
    public const string UtilityKindField = "utility-kind";

    /// <summary>Field name the utility labels are posted under.</summary>
    public const string UtilityLabelField = "utility-label";

    /// <summary>Field name the utility costs are posted under.</summary>
    public const string UtilityCostField = "utility-cost";

    /// <summary>The value an add button posts for the list called <paramref name="list"/>.</summary>
    public static string AddAction(string list) => $"add-{list}";

    /// <summary>The value a remove button posts for row <paramref name="index"/> of <paramref name="list"/>.</summary>
    public static string RemoveAction(string list, int index) => $"remove-{list}-{index}";

    /// <summary>Returns the list and row a remove button asked to drop, or null for another action.</summary>
    public static (string List, int Index)? Removed(string? action)
    {
        if (action is null || !action.StartsWith("remove-", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = action["remove-".Length..];
        var split = rest.LastIndexOf('-');

        return split > 0 && int.TryParse(rest[(split + 1)..], out var index)
            ? (rest[..split], index)
            : null;
    }

    /// <summary>Returns the list an add button asked to grow, or null for another action.</summary>
    public static string? Added(string? action) =>
        action is not null && action.StartsWith("add-", StringComparison.Ordinal) ? action["add-".Length..] : null;

    /// <summary>Reads the utility lines out of a posted form, in the order the page rendered them.</summary>
    public static List<UtilityRow> ReadUtilities(IFormCollection form)
    {
        var labels = form[UtilityLabelField];
        var costs = form[UtilityCostField];

        return form[UtilityKindField]
            .Select((kind, index) => new UtilityRow(
                Enum.TryParse<UtilityKind>(kind, out var parsed) ? parsed : UtilityKind.Other,
                index < labels.Count ? labels[index] : null,
                index < costs.Count ? costs[index] : null))
            .ToList();
    }

    /// <summary>Reads the lines of one text list out of a posted form.</summary>
    public static List<TextRow> ReadText(IFormCollection form, string field) =>
        form[field].Select(value => new TextRow(value)).ToList();

    /// <summary>Reads a money box, treating anything unparseable as nothing typed.</summary>
    public static decimal? Money(string? typed) =>
        decimal.TryParse(typed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
