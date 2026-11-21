using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// The words sent to the model about a tour and the reading of what it sends back. Nothing here
/// calls anything, so what the model is asked and what counts as an answer can be tested directly.
/// </summary>
public static partial class TourAnalysis
{
    /// <summary>What the model is asked about one picture.</summary>
    public const string CaptionPrompt =
        "This picture was taken while touring a home that might be rented. Describe what it shows in one or "
        + "two plain sentences: the room or area, its condition, and anything a renter would want to remember. "
        + "Answer with the description only.";

    /// <summary>Who the model is and the shape of the answer it has to give.</summary>
    public const string SystemPrompt = """
        You help a household decide which home to rent. They have just toured a property. You are given what
        was already recorded about the property, and what the tour produced: their typed notes, a transcript
        of the video they recorded, and descriptions of the pictures they took.

        Answer with one JSON object and nothing else, in this shape:

        {
          "summary": "what the tour found, in a few short paragraphs of plain text",
          "priorities": [ { "id": 0, "decision": "yes", "evidence": "the words from the tour that settle it" } ],
          "pros": [ "a short point in the property's favour" ],
          "cons": [ "a short point against it" ],
          "discrepancies": [ "where the tour disagrees with what was already recorded, and how" ]
        }

        Rules:
        - "priorities" covers only the unmarked priorities listed, by their id. "decision" is "yes" when the
          tour material clearly shows the property meets it, "no" when it clearly shows it does not, and
          "unclear" otherwise. Do not guess: silence, hints and likelihood are "unclear".
        - "evidence" is required for "yes" and "no": an exact quote, copied word for word from the typed
          notes, the transcript or a picture description, that settles it. Find the quote first, then make
          the decision agree with it. A decision whose quote is not in the tour material is discarded.
        - "pros" and "cons" hold only points the tour itself brought up that are not already recorded.
          Leave a list empty when there is nothing.
        - "discrepancies" holds only real conflicts between the tour and the recorded data, such as a
          different rent or a missing amenity. List every one here even when the summary mentions it.
          Leave it empty when there are none.
        - Use only what you were given. Do not invent facts.
        """;

    /// <summary>Lays out everything known about a property and its tour for the model to read.</summary>
    /// <param name="property">The property as recorded before the tour's findings.</param>
    /// <param name="unmarked">The priorities it has not been marked against.</param>
    /// <param name="notes">What was typed on the tour.</param>
    /// <param name="transcript">What was said in the video.</param>
    /// <param name="captions">What each of the tour's pictures shows.</param>
    /// <param name="priorNotes">Notes written on the property before the tour.</param>
    /// <param name="priorPictures">What the pictures taken before the tour show.</param>
    public static string Material(
        PropertyView property,
        IReadOnlyList<SearchPriorityView> unmarked,
        string? notes,
        string? transcript,
        IReadOnlyList<string> captions,
        IReadOnlyList<string> priorNotes,
        IReadOnlyList<string> priorPictures)
    {
        var text = new StringBuilder();

        text.AppendLine("# Already recorded about the property");
        text.AppendLine($"Name: {property.Name}");
        Line(text, "Address", property.Address);
        Line(text, "Rent a month", Money(property.Rent));
        Line(text, "Parking a month", Money(property.ParkingFee));
        Line(text, "Pet rent a month", Money(property.PetRent));
        Line(text, "Deposit", Money(property.Deposit));
        Line(text, "Square feet", property.SquareFeet?.ToString(CultureInfo.InvariantCulture));
        Line(text, "Internet", property.Internet);

        foreach (var utility in property.Utilities)
        {
            text.AppendLine($"Utility: {utility.Label ?? utility.Kind.ToString()}, {Money(utility.MonthlyCost)} a month");
        }

        List(text, "Amenities", property.Amenities.Select(a => a.Name));
        List(text, "Pros", property.Points.Where(p => p.Kind == PropertyPointKind.Pro).Select(p => p.Text));
        List(text, "Cons", property.Points.Where(p => p.Kind == PropertyPointKind.Con).Select(p => p.Text));
        List(text, "Priorities already marked", property.Priorities
            .Where(p => p.Meets is not null)
            .Select(p => $"{p.Name}: {(p.Meets == true ? "meets it" : "does not meet it")}"));
        List(text, "Earlier notes", priorNotes);
        List(text, "Earlier pictures", priorPictures);

        text.AppendLine();
        text.AppendLine("# Unmarked priorities to decide");
        if (unmarked.Count == 0)
        {
            text.AppendLine("None.");
        }

        foreach (var priority in unmarked)
        {
            text.AppendLine($"- id {priority.Id}: {priority.Name}");
        }

        text.AppendLine();
        text.AppendLine("# The tour");
        Section(text, "Typed notes", notes);
        Section(text, "Video transcript", transcript);

        text.AppendLine("## Pictures");
        if (captions.Count == 0)
        {
            text.AppendLine("None.");
        }

        for (var index = 0; index < captions.Count; index++)
        {
            text.AppendLine($"{index + 1}. {captions[index]}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads the model's answer. A priority is kept only when it was asked about, answered with a
    /// plain yes or no, and backed by a quote that really is in the tour; anything else leaves that
    /// priority as it was.
    /// </summary>
    /// <param name="answer">What the model sent back.</param>
    /// <param name="unmarked">Ids of the priorities it was asked to decide.</param>
    /// <param name="tour">The notes, transcript and picture descriptions the quotes have to come from.</param>
    /// <exception cref="PropertyTourException">The answer is not the JSON object asked for, or has no summary.</exception>
    public static TourFindings Parse(string? answer, IReadOnlyCollection<int> unmarked, string? tour)
    {
        var spoken = Plain(tour);
        var text = Clean(answer);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            throw new PropertyTourException("The model did not answer with the JSON it was asked for.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text[start..(end + 1)], new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException)
        {
            throw new PropertyTourException("The model answered with JSON that could not be read.");
        }

        using (document)
        {
            var root = document.RootElement;

            var summary = root.TryGetProperty("summary", out var node) && node.ValueKind == JsonValueKind.String
                ? node.GetString()!.Trim()
                : string.Empty;

            if (summary.Length == 0)
            {
                throw new PropertyTourException("The model answered without a summary.");
            }

            var decided = new Dictionary<int, bool>();
            if (root.TryGetProperty("priorities", out var priorities) && priorities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in priorities.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && Id(entry) is { } id
                        && unmarked.Contains(id)
                        && Decision(entry) is { } meets
                        && Quoted(entry, spoken))
                    {
                        decided[id] = meets;
                    }
                }
            }

            return new TourFindings(
                summary,
                decided,
                Strings(root, "pros"),
                Strings(root, "cons"),
                Strings(root, "discrepancies"));
        }
    }

    /// <summary>Builds the note left on the property: the summary, and the discrepancies when there are any.</summary>
    public static string Note(TourFindings findings)
    {
        var note = new StringBuilder("Tour Summary\n\n").Append(findings.Summary);

        if (findings.Discrepancies.Count > 0)
        {
            note.Append("\n\nPossible Discrepancies\n");
            foreach (var discrepancy in findings.Discrepancies)
            {
                note.Append("\n- ").Append(discrepancy);
            }
        }

        return note.ToString();
    }

    /// <summary>Strips a reasoning block and a code fence from around a model's answer.</summary>
    public static string Clean(string? answer)
    {
        var text = ThinkBlock().Replace(answer ?? string.Empty, string.Empty).Trim();
        var fenced = Fence().Match(text);
        return fenced.Success ? fenced.Groups[1].Value.Trim() : text;
    }

    /// <summary>Shortest quote that counts as evidence, in letters and digits.</summary>
    private const int ShortestQuote = 6;

    // Compared on letters and digits alone, so a quote that differs from the tour only in its
    // punctuation, spacing or capitals still counts as copied from it.
    private static bool Quoted(JsonElement entry, string spoken)
    {
        if (!entry.TryGetProperty("evidence", out var node) || node.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var quote = Plain(node.GetString());
        return quote.Length >= ShortestQuote && spoken.Contains(quote, StringComparison.Ordinal);
    }

    private static string Plain(string? text) =>
        new((text ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int? Id(JsonElement entry)
    {
        if (!entry.TryGetProperty("id", out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(node.GetString(), CultureInfo.InvariantCulture, out var typed) => typed,
            _ => null,
        };
    }

    private static bool? Decision(JsonElement entry)
    {
        if (!entry.TryGetProperty("decision", out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => node.GetString()!.Trim().ToLowerInvariant() switch
            {
                "yes" => true,
                "no" => false,
                _ => null,
            },
            _ => null,
        };
    }

    private static List<string> Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return node.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Line(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.AppendLine($"{label}: {value}");
        }
    }

    private static void List(StringBuilder text, string label, IEnumerable<string> items)
    {
        var held = items.ToList();
        if (held.Count == 0)
        {
            return;
        }

        text.AppendLine($"{label}:");
        foreach (var item in held)
        {
            text.AppendLine($"- {item}");
        }
    }

    private static void Section(StringBuilder text, string heading, string? body)
    {
        text.AppendLine($"## {heading}");
        text.AppendLine(string.IsNullOrWhiteSpace(body) ? "None." : body.Trim());
        text.AppendLine();
    }

    private static string? Money(decimal? value) =>
        value is null ? null : "$" + value.Value.ToString("0.##", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"```(?:json)?\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Fence();
}
