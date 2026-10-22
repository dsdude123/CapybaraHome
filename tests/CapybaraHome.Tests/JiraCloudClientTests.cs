using System.Net;
using System.Text;
using CapybaraHome.Application.Jira;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers reading a JIRA Cloud site: how the typed address is read, how the pages are followed and
/// what a refusal says. Every reply here is invented; no real site is contacted.
/// </summary>
public class JiraCloudClientTests
{
    private static readonly JiraConnection Connection =
        new(new Uri("https://example.atlassian.net/"), "someone@example.com", "a-token");

    [Theory]
    [InlineData("example.atlassian.net", "https://example.atlassian.net/")]
    [InlineData("https://example.atlassian.net", "https://example.atlassian.net/")]
    [InlineData("  https://example.atlassian.net/jira/software/projects  ", "https://example.atlassian.net/")]
    public void A_typed_address_becomes_the_root_of_the_site(string typed, string expected)
    {
        var connection = JiraConnection.Create(typed, "someone@example.com", "a-token");

        Assert.Equal(expected, connection.BaseUri.ToString());
    }

    [Theory]
    [InlineData("http://example.atlassian.net")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_address_that_is_not_an_https_site_is_refused(string typed) =>
        Assert.Throws<ArgumentException>(() => JiraConnection.Create(typed, "someone@example.com", "a-token"));

    [Fact]
    public void A_missing_token_is_refused_before_anything_is_sent() =>
        Assert.Throws<ArgumentException>(() => JiraConnection.Create("example.atlassian.net", "someone@example.com", ""));

    [Fact]
    public async Task Connecting_sends_the_email_and_token_as_basic_credentials()
    {
        var http = new FakeHttp().Reply("""{ "displayName": "Someone", "emailAddress": "someone@example.com" }""");
        var client = new JiraCloudClient(http.Client());

        var account = await client.VerifyAsync(Connection);

        Assert.Equal("Someone", account.DisplayName);
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("someone@example.com:a-token"));
        Assert.Equal(expected, http.LastAuthorization);
        Assert.Equal("https://example.atlassian.net/rest/api/3/myself", http.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task A_refused_token_is_explained_without_repeating_the_token()
    {
        var http = new FakeHttp().Reply("""{ "message": "no" }""", HttpStatusCode.Unauthorized);
        var client = new JiraCloudClient(http.Client());

        var error = await Assert.ThrowsAsync<JiraRequestException>(() => client.VerifyAsync(Connection));

        Assert.Contains("API token was not accepted", error.Message);
        Assert.DoesNotContain("a-token", error.Message);
    }

    [Fact]
    public async Task A_site_address_that_is_not_jira_is_explained()
    {
        var http = new FakeHttp().Reply("{}", HttpStatusCode.NotFound);
        var client = new JiraCloudClient(http.Client());

        var error = await Assert.ThrowsAsync<JiraRequestException>(() => client.VerifyAsync(Connection));

        Assert.Contains("Check the address", error.Message);
    }

    [Fact]
    public async Task The_project_list_follows_the_pages()
    {
        var http = new FakeHttp()
            .Reply("""{ "isLast": false, "values": [ { "key": "AAA", "name": "First" } ] }""")
            .Reply("""{ "isLast": true, "values": [ { "key": "BBB", "name": "Second" } ] }""");
        var client = new JiraCloudClient(http.Client());

        var projects = await client.ListProjectsAsync(Connection);

        Assert.Equal(["AAA", "BBB"], projects.Select(p => p.Key));
        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("startAt=1", http.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task The_issue_fetch_follows_the_page_tokens_and_reports_progress()
    {
        var http = new FakeHttp()
            .Reply("""
            { "isLast": false, "nextPageToken": "second-page", "issues": [
                { "key": "AAA-1", "fields": { "summary": "One", "issuetype": { "name": "Task" } } } ] }
            """)
            .Reply("""
            { "isLast": true, "issues": [
                { "key": "AAA-2", "fields": { "summary": "Two", "issuetype": { "name": "Task" } } } ] }
            """);
        var client = new JiraCloudClient(http.Client());
        var reported = new List<int>();

        using var snapshot = await client.FetchIssuesAsync(
            Connection,
            JiraCloudClient.ProjectQuery("AAA"),
            new Progress<int>(reported.Add));

        Assert.Equal(2, snapshot.Issues.Count);
        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("nextPageToken=second-page", http.Requests[1].RequestUri!.Query);
        Assert.Contains(Uri.EscapeDataString("project = AAA ORDER BY created ASC"), http.Requests[0].RequestUri!.Query);

        // Progress is posted without a synchronization context here, so it may arrive late.
        await Task.Delay(50);
        Assert.Equal(2, reported.LastOrDefault());
    }

    [Fact]
    public async Task A_page_token_the_site_repeats_does_not_loop()
    {
        var http = new FakeHttp().Reply("""
        { "isLast": false, "nextPageToken": "same", "issues": [
            { "key": "AAA-1", "fields": { "summary": "One", "issuetype": { "name": "Task" } } } ] }
        """);
        var client = new JiraCloudClient(http.Client());

        using var snapshot = await client.FetchIssuesAsync(Connection, JiraCloudClient.ProjectQuery("AAA"));

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(2, snapshot.Issues.Count);
    }

    [Fact]
    public async Task A_count_the_site_will_not_give_is_not_an_error()
    {
        var http = new FakeHttp().Reply("{}", HttpStatusCode.BadRequest);
        var client = new JiraCloudClient(http.Client());

        Assert.Null(await client.CountIssuesAsync(Connection, "project = AAA"));
    }

    [Fact]
    public async Task The_count_comes_from_the_approximate_count_endpoint()
    {
        var http = new FakeHttp().Reply("""{ "count": 949 }""");
        var client = new JiraCloudClient(http.Client());

        var count = await client.CountIssuesAsync(Connection, "project = AAA");

        Assert.Equal(949, count);
        Assert.Equal(HttpMethod.Post, http.Requests.Single().Method);
        Assert.EndsWith("/rest/api/3/search/approximate-count", http.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAA ORDER BY created; DROP")]
    [InlineData("AAA\" OR project = \"BBB")]
    public void A_project_key_that_is_not_a_key_never_reaches_a_query(string? key) =>
        Assert.Throws<ArgumentException>(() => JiraCloudClient.ProjectQuery(key));

    [Fact]
    public async Task What_the_fetch_pulled_back_imports_as_tasks()
    {
        var http = new FakeHttp().Reply("""
        { "isLast": true, "issues": [
            { "key": "AAA-1", "fields": { "summary": "A bucket", "issuetype": { "name": "Epic" } } },
            { "key": "AAA-2", "fields": {
                "summary": "A job of work",
                "issuetype": { "name": "Task" },
                "status": { "name": "In Progress" },
                "parent": { "key": "AAA-1", "fields": { "issuetype": { "name": "Epic" } } } } } ] }
        """);
        var client = new JiraCloudClient(http.Client());
        await using var db = await TestDatabase.CreateAsync();

        using var snapshot = await client.FetchIssuesAsync(Connection, JiraCloudClient.ProjectQuery("AAA"));
        var report = await new JiraImportService(db.Context).ImportAsync(snapshot.Issues);

        Assert.Equal(1, report.CategoriesAdded);
        Assert.Equal(1, report.TasksAdded);

        var task = await db.Context.Tasks.Include(t => t.Category).SingleAsync();
        Assert.Equal("A job of work", task.Title);
        Assert.Equal("A bucket", task.Category!.Name);
    }
}
