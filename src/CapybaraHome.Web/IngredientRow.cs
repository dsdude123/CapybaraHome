namespace CapybaraHome.Web;

/// <summary>One line of an ingredient list as an editor holds it while it is being typed.</summary>
/// <param name="Quantity">How much is needed, as typed.</param>
/// <param name="Item">What the ingredient is, as typed.</param>
public record IngredientRow(string? Quantity, string? Item)
{
    private const string RemovePrefix = "remove-";

    /// <summary>The value a remove button posts for the row at <paramref name="index"/>.</summary>
    public static string RemoveAction(int index) => RemovePrefix + index;

    /// <summary>Reads the rows out of a posted editor form, in the order the page rendered them.</summary>
    public static List<IngredientRow> ReadFrom(IFormCollection form)
    {
        var quantities = form["quantity"];
        return form["item"]
            .Select((item, index) => new IngredientRow(index < quantities.Count ? quantities[index] : null, item))
            .ToList();
    }

    /// <summary>Returns the row a remove button asked to drop, or null when the action is something else.</summary>
    public static int? RemovedIndex(string? action)
    {
        if (action is null || !action.StartsWith(RemovePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(action[RemovePrefix.Length..], out var index) ? index : null;
    }
}
