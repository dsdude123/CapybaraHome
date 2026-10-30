using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Html.Parser;

namespace CapybaraHome.Application.Reports;

/// <summary>Reads the headlines the report ends with, from a syndication feed or a news page.</summary>
public partial class NewsFeed(HttpClient client)
{
    /// <summary>Shortest headline the page scrape treats as one.</summary>
    private const int ShortestHeadline = 10;

    /// <summary>
    /// Words a headline the scrape keeps does not open with. A TEGNA site files its explainers and
    /// its service pieces under the same article paths as the news, and they all start like this.
    /// </summary>
    private static readonly HashSet<string> QuestionOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "how", "what", "where", "when", "why", "who", "which", "whose", "whom",
    };

    /// <summary>Returns the first <paramref name="limit"/> entries of the feed at <paramref name="url"/>.</summary>
    public async Task<IReadOnlyList<NewsHeadline>> ReadFeedAsync(string url, int limit, CancellationToken cancellationToken = default)
    {
        var text = await client.GetStringAsync(url, cancellationToken);
        var document = XDocument.Parse(text);

        var entries = document.Descendants()
            .Where(e => e.Name.LocalName is "item" or "entry")
            .Select(Entry)
            .Where(entry => entry.Title.Length > 0)
            .Take(limit);

        return [.. entries];
    }

    /// <summary>
    /// Returns the first <paramref name="limit"/> headlines of the news page at <paramref name="url"/>.
    /// Articles are picked out by the <c>/article/</c> in their address, which is the steadiest mark
    /// of a story on the site rather than a section or a promotion.
    /// </summary>
    public async Task<IReadOnlyList<NewsHeadline>> ReadPageAsync(string url, int limit, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // The page answers a plain client with a cut-down version that carries no stories.
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);

        var headlines = new List<NewsHeadline>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href") ?? string.Empty;
            if (!href.Contains("/article/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var heading = anchor.QuerySelector("h1, h2, h3, h4");
            var title = Collapse(heading is null ? anchor.TextContent : heading.TextContent);

            if (title.Length < ShortestHeadline || !seen.Add(title) || !IsNews(title))
            {
                continue;
            }

            headlines.Add(new NewsHeadline(title, href));
            if (headlines.Count >= limit)
            {
                break;
            }
        }

        return headlines;
    }

    /// <summary>Whether a headline is a story rather than a forecast or an explainer.</summary>
    private static bool IsNews(string title)
    {
        if (title.Contains("weather forecast", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var opener = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return !QuestionOpeners.Contains(opener.TrimEnd('?', ','));
    }

    /// <summary>
    /// Reads one feed entry, whichever of the two feed formats wrote it. A wire escapes the
    /// punctuation in its own headlines, so what the XML gives back is unescaped once more before
    /// it is printed: otherwise an ampersand reaches the page as the five characters that spell it.
    /// </summary>
    private static NewsHeadline Entry(XElement element)
    {
        var title = WebUtility.HtmlDecode(Collapse(Child(element, "title")));
        var link = Collapse(Child(element, "link"));

        if (link.Length == 0)
        {
            link = element.Elements()
                .Where(e => e.Name.LocalName == "link")
                .Select(e => e.Attribute("href")?.Value ?? string.Empty)
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
        }

        return new NewsHeadline(title, link);
    }

    private static string Child(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
