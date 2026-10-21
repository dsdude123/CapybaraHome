using System.Net;

namespace CapybaraHome.Tests;

/// <summary>
/// Drives the real sign-in pages against the running host, with no stand-in for the cookie, so
/// the setup redirect, the form post and the cookie that follows are all exercised.
/// </summary>
public class SignInFlowTests
{
    [Fact]
    public async Task An_installation_with_no_account_sends_a_browser_to_setup()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false, seedFirstUser: false);
        var client = Client(app);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/setup", Target(response));
    }

    [Fact]
    public async Task Setup_creates_the_first_account_and_signs_it_in()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false, seedFirstUser: false);
        var client = Client(app);

        var posted = await PostFormAsync(client, "/setup", "setup", new()
        {
            ["form.Username"] = "conner",
            ["form.DisplayName"] = "Conner",
            ["form.Password"] = AppFactory.SeededPassword,
            ["form.Confirm"] = AppFactory.SeededPassword,
        });

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal("/", Target(posted));

        var dashboard = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    [Fact]
    public async Task Setup_refuses_two_different_passwords()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false, seedFirstUser: false);
        var client = Client(app);

        var posted = await PostFormAsync(client, "/setup", "setup", new()
        {
            ["form.Username"] = "conner",
            ["form.DisplayName"] = "Conner",
            ["form.Password"] = AppFactory.SeededPassword,
            ["form.Confirm"] = "something-else-entirely",
        });

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains("not the same", await posted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_signed_out_visit_lands_on_the_sign_in_page()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var response = await client.GetAsync("/tasks");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/sign-in", Target(response));
    }

    [Fact]
    public async Task The_right_password_signs_in_and_the_wrong_one_does_not()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var refused = await PostFormAsync(client, "/sign-in", "sign-in", new()
        {
            ["form.Username"] = AppFactory.SeededUsername,
            ["form.Password"] = "not-the-password",
        });

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("do not match an account", await refused.Content.ReadAsStringAsync());

        var accepted = await PostFormAsync(client, "/sign-in", "sign-in", new()
        {
            ["form.Username"] = AppFactory.SeededUsername,
            ["form.Password"] = AppFactory.SeededPassword,
        });

        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);

        var tasks = await client.GetAsync("/tasks");
        Assert.Equal(HttpStatusCode.OK, tasks.StatusCode);
    }

    [Fact]
    public async Task Signing_out_takes_the_cookie_away_again()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        await PostFormAsync(client, "/sign-in", "sign-in", new()
        {
            ["form.Username"] = AppFactory.SeededUsername,
            ["form.Password"] = AppFactory.SeededPassword,
        });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tasks")).StatusCode);

        var token = await AntiforgeryTokenAsync(client, "/sign-in");
        var signedOut = await client.PostAsync(
            "/sign-out",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/tasks")).StatusCode);
    }

    [Fact]
    public async Task A_form_post_without_an_antiforgery_token_is_refused()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var posted = await client.PostAsync("/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "sign-in",
            ["form.Username"] = AppFactory.SeededUsername,
            ["form.Password"] = AppFactory.SeededPassword,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, posted.StatusCode);
    }

    // A redirect's Location may be absolute or relative; the path is what the test is about.
    private static string Target(HttpResponseMessage response)
    {
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("The response carried no Location header.");

        return location.IsAbsoluteUri
            ? location.PathAndQuery
            : location.OriginalString;
    }

    private static HttpClient Client(AppFactory app) =>
        app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        string handler,
        Dictionary<string, string> fields)
    {
        fields["_handler"] = handler;
        fields["__RequestVerificationToken"] = await AntiforgeryTokenAsync(client, path);

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    // The page carries the token in a hidden field, which is also what sets the matching cookie.
    private static async Task<string> AntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var marker = "name=\"__RequestVerificationToken\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, $"No antiforgery token on {path}.");

        start += marker.Length;
        return html[start..html.IndexOf('"', start)];
    }
}
