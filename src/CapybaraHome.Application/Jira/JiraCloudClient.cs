using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CapybaraHome.Application.Jira;

/// <summary>A call to JIRA that came back with something other than the data asked for.</summary>
/// <param name="message">What to tell whoever is running the wizard.</param>
public class JiraRequestException(string message) : Exception(message);

/// <summary>
/// Reads a JIRA Cloud site over its REST API with an account email and an API token. Every call
/// takes the connection, so no credential is held between calls.
/// </summary>
public partial class JiraCloudClient(HttpClient http)
{
    /// <summary>Issue fields the import reads. Asked for by name so a site with many fields stays cheap.</summary>
    private const string ImportFields = "summary,description,issuetype,status,duedate,resolutiondate,created,updated,parent";

    private const int PageSize = 100;

    /// <summary>Stops a site that keeps handing out page tokens from looping forever.</summary>
    private const int MaxPages = 500;

    /// <summary>Checks the site address and the credentials by reading the account they belong to.</summary>
    /// <exception cref="JiraRequestException">The site did not answer, or refused the credentials.</exception>
    public async Task<JiraAccount> VerifyAsync(JiraConnection connection, CancellationToken cancellationToken = default)
    {
        using var document = await GetAsync(connection, "rest/api/3/myself", cancellationToken);
        var root = document.RootElement;

        var name = root.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : null;
        var email = root.TryGetProperty("emailAddress", out var address) ? address.GetString() : null;

        return new JiraAccount(name ?? connection.Email, email);
    }

    /// <summary>Lists every project the account can see, by key.</summary>
    /// <exception cref="JiraRequestException">The site did not answer, or refused the credentials.</exception>
    public async Task<IReadOnlyList<JiraProjectSummary>> ListProjectsAsync(
        JiraConnection connection,
        CancellationToken cancellationToken = default)
    {
        var projects = new List<JiraProjectSummary>();
        var startAt = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            using var document = await GetAsync(
                connection,
                $"rest/api/3/project/search?startAt={startAt}&maxResults=50&orderBy=key",
                cancellationToken);

            var root = document.RootElement;
            if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var value in values.EnumerateArray())
            {
                var key = value.TryGetProperty("key", out var keyNode) ? keyNode.GetString() : null;
                var name = value.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                if (key is { Length: > 0 })
                {
                    projects.Add(new JiraProjectSummary(key, name ?? key));
                }
            }

            startAt += values.GetArrayLength();
            var isLast = !root.TryGetProperty("isLast", out var last) || last.ValueKind != JsonValueKind.False;
            if (isLast || values.GetArrayLength() == 0)
            {
                break;
            }
        }

        return projects;
    }

    /// <summary>Asks the site roughly how many issues a query matches, so the wizard can say what it is about to pull.</summary>
    /// <returns>The site's estimate, or null when it would not give one.</returns>
    public async Task<int?> CountIssuesAsync(JiraConnection connection, string jql, CancellationToken cancellationToken = default)
    {
        using var request = Request(connection, HttpMethod.Post, "rest/api/3/search/approximate-count");
        request.Content = JsonContent.Create(new { jql });

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = await ReadAsync(response, cancellationToken);
        return document.RootElement.TryGetProperty("count", out var count) && count.TryGetInt32(out var value)
            ? value
            : null;
    }

    /// <summary>Pulls every issue a query matches, following the site's page tokens.</summary>
    /// <param name="connection">Site and credentials.</param>
    /// <param name="jql">The query to run.</param>
    /// <param name="progress">Told the running issue count after each page, for the wizard to show.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <exception cref="JiraRequestException">The site did not answer, or refused the credentials or the query.</exception>
    public async Task<JiraIssueSnapshot> FetchIssuesAsync(
        JiraConnection connection,
        string jql,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new JiraIssueSnapshot();

        try
        {
            string? pageToken = null;

            for (var page = 0; page < MaxPages; page++)
            {
                var path = new StringBuilder("rest/api/3/search/jql?maxResults=")
                    .Append(PageSize)
                    .Append("&fields=")
                    .Append(Uri.EscapeDataString(ImportFields))
                    .Append("&jql=")
                    .Append(Uri.EscapeDataString(jql));

                if (pageToken is { Length: > 0 })
                {
                    path.Append("&nextPageToken=").Append(Uri.EscapeDataString(pageToken));
                }

                var document = await GetAsync(connection, path.ToString(), cancellationToken);
                snapshot.Add(document);
                progress?.Report(snapshot.Issues.Count);

                var root = document.RootElement;
                var isLast = !root.TryGetProperty("isLast", out var last) || last.ValueKind != JsonValueKind.False;
                var next = root.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;

                if (isLast || next is null or { Length: 0 } || next == pageToken)
                {
                    break;
                }

                pageToken = next;
            }

            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    /// <summary>Builds the query that pulls one project, oldest issue first.</summary>
    /// <exception cref="ArgumentException">The key is not a JIRA project key.</exception>
    public static string ProjectQuery(string? projectKey)
    {
        if (projectKey is null || !ProjectKeyPattern().IsMatch(projectKey))
        {
            throw new ArgumentException("Choose a project to import.", nameof(projectKey));
        }

        return $"project = {projectKey} ORDER BY created ASC";
    }

    private async Task<JsonDocument> GetAsync(JiraConnection connection, string path, CancellationToken cancellationToken)
    {
        using var request = Request(connection, HttpMethod.Get, path);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new JiraRequestException($"{connection.BaseUri.Host} could not be reached: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new JiraRequestException($"{connection.BaseUri.Host} did not answer in time.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new JiraRequestException(Explain(connection, response));
            }

            return await ReadAsync(response, cancellationToken);
        }
    }

    private static HttpRequestMessage Request(JiraConnection connection, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(connection.BaseUri, path));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Email}:{connection.ApiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string Explain(JiraConnection connection, HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized =>
            "The email address or the API token was not accepted. Tokens are made at id.atlassian.com under Security.",
        HttpStatusCode.Forbidden =>
            "The account signed in but is not allowed to read this. Check it can see the project in JIRA.",
        HttpStatusCode.NotFound =>
            $"{connection.BaseUri.Host} answered, but not as a JIRA Cloud site. Check the address.",
        HttpStatusCode.BadRequest =>
            "JIRA rejected the request. The project may have been removed since the list was read.",
        HttpStatusCode.TooManyRequests =>
            "JIRA is rate limiting this account. Wait a minute and run the import again.",
        _ => $"JIRA answered {(int)response.StatusCode} {response.ReasonPhrase}.",
    };

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,254}$")]
    private static partial Regex ProjectKeyPattern();
}
