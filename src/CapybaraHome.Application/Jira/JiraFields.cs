using System.Globalization;
using System.Text.Json;

namespace CapybaraHome.Application.Jira;

/// <summary>Reads the fields of a JIRA issue out of the JSON the search returns.</summary>
public static class JiraFields
{
    /// <summary>Walks a snapshot, which may be one search response, an array of them, or an array of issues.</summary>
    public static IEnumerable<JsonElement> Issues(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    yield return issue;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var element in root.EnumerateArray())
        {
            if (element.TryGetProperty("issues", out _))
            {
                foreach (var issue in Issues(element))
                {
                    yield return issue;
                }
            }
            else
            {
                yield return element;
            }
        }
    }

    /// <summary>Returns an issue's fields object, or a default element when it carries none.</summary>
    public static JsonElement Fields(JsonElement issue) =>
        issue.TryGetProperty("fields", out var fields) ? fields : default;

    /// <summary>Returns an issue's key, or null.</summary>
    public static string? Key(JsonElement issue) =>
        issue.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;

    /// <summary>Returns a string field, or null when it is missing or not a string.</summary>
    public static string? Text(JsonElement issue, string field)
    {
        var fields = Fields(issue);
        return fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty(field, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>Returns the name of a named sub-object, such as the issue type or the status.</summary>
    public static string? Named(JsonElement issue, string field)
    {
        var fields = Fields(issue);
        return fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty(field, out var node)
            && node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
    }

    /// <summary>Returns the issue's labels, in the order JIRA gives them.</summary>
    public static IEnumerable<string> Labels(JsonElement issue)
    {
        var fields = Fields(issue);
        if (fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty("labels", out var labels)
            || labels.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var label in labels.EnumerateArray())
        {
            if (label.ValueKind == JsonValueKind.String && label.GetString() is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    /// <summary>Returns the description as markdown, or null when the field is empty.</summary>
    public static string? Description(JsonElement issue)
    {
        var fields = Fields(issue);
        var markdown = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("description", out var description)
            ? AdfToMarkdown.Convert(description)
            : null;

        return string.IsNullOrWhiteSpace(markdown) ? null : markdown;
    }

    /// <summary>Returns a date field, or null.</summary>
    public static DateOnly? Date(JsonElement issue, string field)
    {
        var text = Text(issue, field);
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date) ? date : null;
    }

    /// <summary>Returns a timestamp field, or null.</summary>
    public static DateTimeOffset? Timestamp(JsonElement issue, string field)
    {
        var text = Text(issue, field);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
    }
}
