using System.Text;
using System.Text.RegularExpressions;

namespace CapybaraHome.Application.Meals;

/// <summary>A guess at how an imported description divides into ingredients and directions.</summary>
/// <param name="Ingredients">The lines that look like ingredients, split into a quantity and an item.</param>
/// <param name="Directions">What is left, as markdown.</param>
public record SplitSuggestion(IReadOnlyList<MealIngredientInput> Ingredients, string Directions);

/// <summary>
/// Guesses which lines of an imported description are ingredients. The guess is only ever offered
/// to be corrected on the splitting screen: nothing calls it at import time, so an imported
/// description is never rewritten behind the person's back and the guess can always be run again.
/// </summary>
public static partial class IngredientSplitter
{
    /// <summary>Units that mark the end of a quantity, so that "2 cups rice" splits where it reads.</summary>
    private static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        "bag", "bags", "bottle", "bottles", "box", "boxes", "bunch", "bunches", "can", "cans",
        "clove", "cloves", "cup", "cups", "dash", "dashes", "g", "gram", "grams", "handful",
        "handfuls", "head", "heads", "jar", "jars", "kg", "l", "lb", "lbs", "litre", "litres",
        "liter", "liters", "ml", "oz", "ounce", "ounces", "pack", "packs", "package", "packages",
        "piece", "pieces", "pinch", "pinches", "pound", "pounds", "punnet", "punnets", "sachet",
        "sachets", "slice", "slices", "sprig", "sprigs", "stick", "sticks", "tbsp", "tablespoon",
        "tablespoons", "tin", "tins", "tsp", "teaspoon", "teaspoons",
    };

    /// <summary>Returns the suggested split of <paramref name="text"/>, which may be null or empty.</summary>
    public static SplitSuggestion Suggest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SplitSuggestion([], string.Empty);
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sections = Sections(lines);

        var ingredients = sections.Ingredients
            .Select(Line)
            .Where(line => line is not null)
            .Select(line => line!)
            .ToList();

        return new SplitSuggestion(ingredients, string.Join("\n", sections.Directions).Trim());
    }

    /// <summary>
    /// Divides the lines. A description with an ingredients heading is taken at its word; one
    /// without is read as a list at the top, which is how most of them are written, and anything
    /// that does not look like an ingredient line ends the list.
    /// </summary>
    private static (List<string> Ingredients, List<string> Directions) Sections(string[] lines)
    {
        var ingredients = new List<string>();
        var directions = new List<string>();
        var heading = Array.FindIndex(lines, IsIngredientHeading);

        if (heading >= 0)
        {
            directions.AddRange(lines[..heading]);

            for (var index = heading + 1; index < lines.Length; index++)
            {
                if (IsHeading(lines[index]))
                {
                    directions.AddRange(lines[index..]);
                    return (ingredients, directions);
                }

                if (lines[index].Trim().Length > 0)
                {
                    ingredients.Add(lines[index]);
                }
            }

            return (ingredients, directions);
        }

        var reading = true;

        foreach (var line in lines)
        {
            if (reading && line.Trim().Length == 0)
            {
                continue;
            }

            if (reading && LooksLikeIngredient(line))
            {
                ingredients.Add(line);
                continue;
            }

            reading = false;
            directions.Add(line);
        }

        // A description whose every line looks like an ingredient is more likely a list of steps
        // than a shopping list, so one line on its own is left alone.
        return ingredients.Count > 1 ? (ingredients, directions) : ([], [.. lines]);
    }

    private static bool IsHeading(string line) => HeadingPattern().IsMatch(line.Trim());

    private static bool IsIngredientHeading(string line) =>
        IsHeading(line) && line.Contains("ingredient", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeIngredient(string line)
    {
        var text = Strip(line);
        if (text.Length == 0 || IsHeading(line))
        {
            return false;
        }

        // A plain bullet, or a line opening with an amount, is the shape an ingredient takes. A
        // numbered list is not: it is nearly always the steps, so it falls through to directions.
        return ListBulletPattern().IsMatch(line.Trim()) || QuantityPattern().IsMatch(text);
    }

    /// <summary>Turns one line into a quantity and an item, or null when nothing is left of it.</summary>
    private static MealIngredientInput? Line(string line)
    {
        var text = Strip(line);
        if (text.Length == 0)
        {
            return null;
        }

        var match = QuantityPattern().Match(text);
        if (!match.Success)
        {
            return new MealIngredientInput(text);
        }

        var quantity = new StringBuilder(match.Groups["amount"].Value.Trim());
        var rest = text[match.Length..].Trim();
        var words = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 0 && Units.Contains(words[0].Trim('.', ',')))
        {
            quantity.Append(' ').Append(words[0].Trim('.', ','));
            rest = words.Length > 1 ? words[1].Trim() : string.Empty;
        }

        return rest.Length > 0
            ? new MealIngredientInput(rest, quantity.ToString())
            : new MealIngredientInput(quantity.ToString());
    }

    /// <summary>Takes the markdown off a line: its bullet, its checkbox and its stray spaces.</summary>
    private static string Strip(string line) => BulletPattern().Replace(line.Trim(), string.Empty).Trim();

    [GeneratedRegex(@"^(#{1,6}\s+|\*\*.+\*\*:?$|.+:$)")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^([-*+•]|\d+\.)\s+(\[[ xX]\]\s*)?")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^[-*+•]\s+")]
    private static partial Regex ListBulletPattern();

    [GeneratedRegex(@"^(?<amount>\d+([.,]\d+)?(\s*[-–/]\s*\d+([.,]\d+)?)?(\s+\d+/\d+)?|\d*\s*\d+/\d+)\s*")]
    private static partial Regex QuantityPattern();
}
