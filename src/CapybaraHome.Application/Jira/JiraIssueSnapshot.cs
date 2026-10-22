using System.Text.Json;

namespace CapybaraHome.Application.Jira;

/// <summary>
/// The issues one fetch pulled back, held as the pages they arrived in. The elements belong to
/// those pages, so the snapshot has to outlive any reading of them.
/// </summary>
public sealed class JiraIssueSnapshot : IDisposable
{
    private readonly List<JsonDocument> pages = [];
    private readonly List<JsonElement> issues = [];

    /// <summary>Every issue fetched, in the order the site returned it.</summary>
    public IReadOnlyList<JsonElement> Issues => issues;

    /// <summary>Takes ownership of one search response and collects the issues in it.</summary>
    /// <param name="page">A parsed search response. The snapshot disposes it.</param>
    public void Add(JsonDocument page)
    {
        pages.Add(page);

        if (page.RootElement.TryGetProperty("issues", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            issues.AddRange(array.EnumerateArray());
        }
    }

    /// <summary>Releases the pages.</summary>
    public void Dispose()
    {
        foreach (var page in pages)
        {
            page.Dispose();
        }

        pages.Clear();
        issues.Clear();
    }
}
