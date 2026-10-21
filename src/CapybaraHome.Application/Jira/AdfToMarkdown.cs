using System.Text;
using System.Text.Json;

namespace CapybaraHome.Application.Jira;

/// <summary>
/// Turns a JIRA Atlassian Document Format node tree into markdown. Only the node types the
/// task descriptions actually use are handled; anything else contributes its text and nothing
/// more, so no content is lost even when its formatting is.
/// </summary>
public static class AdfToMarkdown
{
    /// <summary>Converts a description field, which may already be plain markdown or may be ADF.</summary>
    /// <param name="field">The raw <c>description</c> value from the JIRA issue.</param>
    /// <returns>Markdown, or null when the field is absent or holds an empty document.</returns>
    public static string? Convert(JsonElement field)
    {
        if (field.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (field.ValueKind == JsonValueKind.String)
        {
            var text = field.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        if (field.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var builder = new StringBuilder();
        WriteChildren(field, builder, listPrefix: null, depth: 0);

        var markdown = builder.ToString().Trim();
        return markdown.Length == 0 ? null : markdown;
    }

    private static void WriteChildren(JsonElement node, StringBuilder builder, string? listPrefix, int depth)
    {
        if (!node.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var child in content.EnumerateArray())
        {
            WriteNode(child, builder, listPrefix, depth, index);
            index++;
        }
    }

    private static void WriteNode(JsonElement node, StringBuilder builder, string? listPrefix, int depth, int index)
    {
        var type = node.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;

        switch (type)
        {
            case "text":
                builder.Append(ApplyMarks(node));
                break;

            case "hardBreak":
                builder.Append("  \n");
                break;

            case "paragraph":
                WriteChildren(node, builder, listPrefix: null, depth);
                builder.Append("\n\n");
                break;

            case "heading":
                var level = node.TryGetProperty("attrs", out var headingAttrs) && headingAttrs.TryGetProperty("level", out var levelNode)
                    ? levelNode.GetInt32()
                    : 1;
                builder.Append(new string('#', Math.Clamp(level, 1, 6))).Append(' ');
                WriteChildren(node, builder, listPrefix: null, depth);
                builder.Append("\n\n");
                break;

            case "bulletList":
                WriteList(node, builder, depth, ordered: false);
                break;

            case "orderedList":
                WriteList(node, builder, depth, ordered: true);
                break;

            case "listItem":
                builder.Append(new string(' ', depth * 2)).Append(listPrefix ?? "- ");
                WriteListItem(node, builder, depth);
                break;

            case "codeBlock":
                builder.Append("```\n");
                WriteChildren(node, builder, listPrefix: null, depth);
                builder.Append("\n```\n\n");
                break;

            case "blockquote":
                var quoted = new StringBuilder();
                WriteChildren(node, quoted, listPrefix: null, depth);
                foreach (var line in quoted.ToString().TrimEnd().Split('\n'))
                {
                    builder.Append("> ").Append(line).Append('\n');
                }

                builder.Append('\n');
                break;

            case "rule":
                builder.Append("---\n\n");
                break;

            case "mediaSingle" or "mediaGroup" or "media":
                builder.Append("_(attachment)_\n\n");
                break;

            default:
                WriteChildren(node, builder, listPrefix, depth);
                break;
        }
    }

    private static void WriteList(JsonElement node, StringBuilder builder, int depth, bool ordered)
    {
        if (!node.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var number = 1;
        foreach (var item in content.EnumerateArray())
        {
            var prefix = ordered ? $"{number}. " : "- ";
            WriteNode(item, builder, prefix, depth, number - 1);
            number++;
        }

        if (depth == 0)
        {
            builder.Append('\n');
        }
    }

    private static void WriteListItem(JsonElement node, StringBuilder builder, int depth)
    {
        var inner = new StringBuilder();
        WriteChildren(node, inner, listPrefix: null, depth + 1);

        var text = inner.ToString().TrimEnd();
        var lines = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        builder.Append(lines.Length == 0 ? string.Empty : lines[0].Trim()).Append('\n');

        foreach (var extra in lines.Skip(1))
        {
            builder.Append(new string(' ', (depth + 1) * 2)).Append(extra.Trim()).Append('\n');
        }
    }

    private static string ApplyMarks(JsonElement node)
    {
        var text = node.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
        if (text.Length == 0 || !node.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array)
        {
            return text;
        }

        string? href = null;

        foreach (var mark in marks.EnumerateArray())
        {
            var type = mark.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
            text = type switch
            {
                "strong" => $"**{text}**",
                "em" => $"_{text}_",
                "strike" => $"~~{text}~~",
                "code" => $"`{text}`",
                _ => text,
            };

            if (type == "link" && mark.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("href", out var hrefNode))
            {
                href = hrefNode.GetString();
            }
        }

        return href is { Length: > 0 } ? $"[{text}]({href})" : text;
    }
}
