using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapybaraHome.Tests;

/// <summary>
/// Drives the whole connector sign-in against the running host the way claude.ai does it: read
/// the two discovery documents, register a client, send a person through this installation's own
/// sign-in to approve it, trade the code for tokens and then reach <c>/mcp</c> with them.
/// </summary>
public class OAuthFlowTests
{
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string Verifier = "a-verifier-long-enough-to-be-a-real-one-0123456789";

    [Fact]
    public async Task The_protected_resource_metadata_names_this_host_as_its_own_authorization_server()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp" })
        {
            var document = await ReadJsonAsync(client, path);

            Assert.Equal("http://localhost/mcp", document.GetProperty("resource").GetString());
            Assert.Equal(
                "http://localhost",
                Assert.Single(document.GetProperty("authorization_servers").EnumerateArray()).GetString());
            Assert.Equal("header", Assert.Single(document.GetProperty("bearer_methods_supported").EnumerateArray()).GetString());
            Assert.Equal("capybarahome", Assert.Single(document.GetProperty("scopes_supported").EnumerateArray()).GetString());
        }
    }

    [Fact]
    public async Task The_authorization_server_metadata_offers_pkce_and_the_two_grants()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var document = await ReadJsonAsync(client, "/.well-known/oauth-authorization-server");

        Assert.Equal("http://localhost", document.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost/oauth/authorize", document.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost/oauth/token", document.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost/oauth/register", document.GetProperty("registration_endpoint").GetString());
        Assert.Equal("http://localhost/oauth/revoke", document.GetProperty("revocation_endpoint").GetString());
        Assert.Equal(
            "S256",
            Assert.Single(document.GetProperty("code_challenge_methods_supported").EnumerateArray()).GetString());
        Assert.Equal(
            ["authorization_code", "refresh_token"],
            document.GetProperty("grant_types_supported").EnumerateArray().Select(g => g.GetString()).ToArray());
        Assert.Equal("code", Assert.Single(document.GetProperty("response_types_supported").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task A_configured_issuer_replaces_the_address_the_request_arrived_on()
    {
        await using var app = await AppFactory.CreateAsync(
            stubSignIn: false,
            settings: new Dictionary<string, string> { ["OAuth:Issuer"] = "https://home.example.test/" });
        var client = Client(app);

        var document = await ReadJsonAsync(client, "/.well-known/oauth-authorization-server");

        Assert.Equal("https://home.example.test", document.GetProperty("issuer").GetString());
        Assert.Equal("https://home.example.test/oauth/token", document.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task The_discovery_documents_answer_before_the_installation_has_an_account()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false, seedFirstUser: false);
        var client = Client(app);

        foreach (var path in new[]
            {
                "/.well-known/oauth-protected-resource",
                "/.well-known/oauth-protected-resource/mcp",
                "/.well-known/oauth-authorization-server",
            })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_unauthenticated_mcp_call_is_told_where_to_read_the_resource_metadata()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "initialize" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.GetValues("WWW-Authenticate"));
        Assert.Contains("Bearer", challenge);
        Assert.Contains(
            "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"",
            challenge);
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    [InlineData("/.well-known/oauth-authorization-server")]
    public async Task A_browser_based_client_can_ask_whether_it_may_read_the_documents(string path)
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("GET", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Contains("Authorization", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Headers")));
    }

    [Theory]
    [InlineData("not-base64-at-all!!")]
    [InlineData("bm8tY29sb24taW4taGVyZQ==")]
    public async Task A_basic_header_that_cannot_be_read_leaves_the_client_unnamed(string parameter)
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "whatever",
                ["redirect_uri"] = Callback,
                ["code_verifier"] = Verifier,
            }),
        };
        request.Headers.Authorization = new("Basic", parameter);

        var response = await client.SendAsync(request);

        // Nothing names a client, so this is an unknown client rather than a bad secret.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", (await JsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_client_registers_itself_and_is_given_a_client_id_and_no_secret()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var response = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("client_id").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("client_secret").ValueKind);
        Assert.Equal("Claude", body.GetProperty("client_name").GetString());
        Assert.Equal("none", body.GetProperty("token_endpoint_auth_method").GetString());
        Assert.Equal(Callback, Assert.Single(body.GetProperty("redirect_uris").EnumerateArray()).GetString());
        Assert.True(body.GetProperty("client_id_issued_at").GetInt64() > 0);
    }

    [Fact]
    public async Task A_confidential_client_is_given_a_secret_that_does_not_expire()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var response = await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Something with a secret",
            redirect_uris = new[] { Callback },
            token_endpoint_auth_method = "client_secret_post",
        });

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("client_secret").GetString()));
        Assert.Equal(0, body.GetProperty("client_secret_expires_at").GetInt32());
    }

    [Theory]
    [InlineData("{\"redirect_uris\":[\"http://evil.test/cb\"]}", "invalid_redirect_uri")]
    [InlineData("{\"redirect_uris\":[]}", "invalid_redirect_uri")]
    [InlineData("{\"redirect_uris\":[\"https://a.test/cb\"],\"grant_types\":[\"implicit\"]}", "invalid_client_metadata")]
    [InlineData("{\"redirect_uris\":[\"https://a.test/cb\"],\"response_types\":[\"token\"]}", "invalid_client_metadata")]
    [InlineData("not json at all", "invalid_client_metadata")]
    [InlineData("[]", "invalid_client_metadata")]
    public async Task A_registration_this_server_will_not_take_is_refused_with_a_reason(string body, string error)
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var response = await client.PostAsync(
            "/oauth/register",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(error, answer.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(answer.GetProperty("error_description").GetString()));
    }

    [Fact]
    public async Task A_callback_this_installation_does_not_allow_is_refused_at_registration()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        // The phishing this closes: register a client calling itself Claude, with a callback of
        // your own, then send the owner a link to the consent page.
        var response = await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Claude",
            redirect_uris = new[] { "https://evil.example/cb" },
            token_endpoint_auth_method = "none",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var answer = await JsonAsync(response);
        Assert.Equal("invalid_redirect_uri", answer.GetProperty("error").GetString());
        Assert.Contains("OAuth:AllowedRedirectUris", answer.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task A_configured_allowlist_replaces_the_default_one()
    {
        await using var app = await AppFactory.CreateAsync(
            stubSignIn: false,
            settings: new Dictionary<string, string>
            {
                ["OAuth:AllowedRedirectUris:0"] = "https://somewhere.example/cb",
            });
        var client = Client(app);

        var allowed = await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Something else",
            redirect_uris = new[] { "https://somewhere.example/cb" },
            token_endpoint_auth_method = "none",
        });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);

        var refused = await RegisterAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task An_allowlist_variable_left_blank_falls_back_to_the_default_rather_than_refusing_everything()
    {
        // What a compose file that names the variable without a value produces.
        await using var app = await AppFactory.CreateAsync(
            stubSignIn: false,
            settings: new Dictionary<string, string> { ["OAuth:AllowedRedirectUris:0"] = string.Empty });
        var client = Client(app);

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client)).StatusCode);
    }

    [Fact]
    public async Task The_consent_page_names_the_host_the_access_goes_to_and_says_the_client_named_itself()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));

        Assert.Contains("It will be sent to <strong style=\"font-size:1.15rem\">claude.ai</strong>", consent);
        Assert.Contains("may register itself under any name", consent);
        Assert.Contains(Callback, consent);
    }

    [Fact]
    public async Task An_authorize_request_naming_somebody_elses_resource_is_sent_back_as_an_error()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var response = await browser.GetAsync(
            AuthorizeUrl(clientId).Replace(
                Uri.EscapeDataString("http://localhost/mcp"),
                Uri.EscapeDataString("https://somewhere.example/mcp")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("invalid_target", HttpUtility.ParseQueryString(response.Headers.Location!.Query)["error"]);
    }

    [Fact]
    public async Task An_approval_posted_with_somebody_elses_resource_issues_no_code()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));
        var response = await browser.PostAsync("/oauth/authorize/decide", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = FormPost.Hidden(consent, "__RequestVerificationToken"),
            ["decision"] = "approve",
            ["clientId"] = clientId,
            ["redirectUri"] = Callback,
            ["codeChallenge"] = Challenge(Verifier),
            ["state"] = "the-state",
            ["resource"] = "https://somewhere.example/mcp",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("invalid_target", query["error"]);
        Assert.Null(query["code"]);
    }

    [Fact]
    public async Task The_consent_page_sends_a_signed_out_visitor_through_the_apps_own_sign_in_first()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);
        var clientId = await RegisteredClientIdAsync(client);

        var response = await client.GetAsync(AuthorizeUrl(clientId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("/sign-in", location.AbsolutePath);
        Assert.Contains("oauth%2Fauthorize", location.Query);
    }

    [Fact]
    public async Task The_whole_flow_ends_with_an_access_token_that_reaches_the_mcp_endpoint()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));
        Assert.Contains("Authorize Claude", consent);
        Assert.Contains(Callback, consent);

        var code = await ApproveAsync(browser, clientId, consent);
        var tokens = await TokenAsync(browser, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["redirect_uri"] = Callback,
            ["code_verifier"] = Verifier,
        });

        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
        Assert.Equal(3600, tokens.GetProperty("expires_in").GetInt32());
        Assert.Equal("capybarahome", tokens.GetProperty("scope").GetString());

        var access = tokens.GetProperty("access_token").GetString()!;
        var connector = app.CreateClient();
        connector.DefaultRequestHeaders.Authorization = new("Bearer", access);
        connector.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        var initialized = await connector.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "tests", version = "1.0" },
            },
        });

        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);

        // The grant is for the MCP endpoint. /api carries the whole export and an import that
        // replaces the data, which the MCP tools cannot reach, so the same token is refused there.
        Assert.Equal(HttpStatusCode.Unauthorized, (await connector.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await connector.GetAsync("/api/export")).StatusCode);
    }

    [Fact]
    public async Task The_refresh_token_buys_another_access_token_and_then_stops_working()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var granted = await GrantAsync(app, browser, clientId);
        var refresh = granted.GetProperty("refresh_token").GetString()!;

        var refreshed = await TokenAsync(browser, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token"
            ,
            ["refresh_token"] = refresh,
            ["client_id"] = clientId,
        });

        Assert.NotEqual(
            granted.GetProperty("access_token").GetString(),
            refreshed.GetProperty("access_token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await McpAsync(app, refreshed)).StatusCode);

        var replay = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
            ["client_id"] = clientId,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_grant", (await JsonAsync(replay)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_denied_request_is_sent_back_with_access_denied_and_no_code()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));
        var response = await DecideAsync(browser, clientId, consent, "deny");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal("the-state", query["state"]);
        Assert.Null(query["code"]);
    }

    [Fact]
    public async Task An_authorize_request_for_an_unknown_client_or_redirect_uri_is_answered_here()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var unknown = await browser.GetAsync(AuthorizeUrl("not-a-client-id"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("No client is registered here", await unknown.Content.ReadAsStringAsync());

        var elsewhere = await browser.GetAsync(AuthorizeUrl(clientId, redirectUri: "https://evil.test/cb"));
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Contains("not for that redirect URI", await elsewhere.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("token", null, "unsupported_response_type")]
    [InlineData("code", "", "invalid_request")]
    public async Task An_authorize_request_the_server_cannot_serve_is_sent_back_as_an_error(
        string responseType,
        string? challenge,
        string error)
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var response = await browser.GetAsync(AuthorizeUrl(clientId, responseType: responseType, challenge: challenge));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("claude.ai", location.Host);
        Assert.Equal(error, HttpUtility.ParseQueryString(location.Query)["error"]);
    }

    [Fact]
    public async Task A_plain_code_challenge_method_is_refused_because_oauth_2_1_does_not_allow_it()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var response = await browser.GetAsync($"{AuthorizeUrl(clientId)}".Replace("S256", "plain"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("invalid_request", HttpUtility.ParseQueryString(response.Headers.Location!.Query)["error"]);
    }

    [Fact]
    public async Task An_approval_posted_for_a_redirect_uri_the_client_never_registered_is_refused()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));
        var response = await browser.PostAsync("/oauth/authorize/decide", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = FormPost.Hidden(consent, "__RequestVerificationToken"),
            ["decision"] = "approve",
            ["clientId"] = clientId,
            ["redirectUri"] = "https://evil.test/cb",
            ["codeChallenge"] = Challenge(Verifier),
            ["state"] = "the-state",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_approval_posted_without_an_antiforgery_token_is_refused()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var response = await browser.PostAsync("/oauth/authorize/decide", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["decision"] = "approve",
            ["clientId"] = clientId,
            ["redirectUri"] = Callback,
            ["codeChallenge"] = Challenge(Verifier),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_decision_cannot_be_posted_without_signing_in()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);

        var response = await browser.PostAsync("/oauth/authorize/decide", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["decision"] = "approve",
            ["clientId"] = clientId,
            ["redirectUri"] = Callback,
        }));

        // Authorization runs before the form is looked at, so the post is sent to sign in rather
        // than issuing a code to anyone who can reach the endpoint.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task The_token_endpoint_refuses_a_grant_type_it_does_not_offer_and_a_body_it_cannot_read()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var unsupported = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = await RegisteredClientIdAsync(client),
        }));
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal("unsupported_grant_type", (await JsonAsync(unsupported)).GetProperty("error").GetString());

        var notAForm = await client.PostAsync("/oauth/token", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, notAForm.StatusCode);
        Assert.Equal("invalid_request", (await JsonAsync(notAForm)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_unknown_client_at_the_token_endpoint_is_answered_with_a_401()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = Client(app);

        var response = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "not-a-client-id",
            ["code"] = "whatever",
            ["code_verifier"] = Verifier,
            ["redirect_uri"] = Callback,
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", (await JsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_confidential_client_may_authenticate_with_the_basic_header()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);

        var registration = await JsonAsync(await browser.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Claude",
            redirect_uris = new[] { Callback },
            token_endpoint_auth_method = "client_secret_basic",
        }));

        var clientId = registration.GetProperty("client_id").GetString()!;
        var secret = registration.GetProperty("client_secret").GetString()!;
        await SignInAsync(browser);

        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));
        var code = await ApproveAsync(browser, clientId, consent);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = Callback,
                ["code_verifier"] = Verifier,
            }),
        };
        request.Headers.Authorization = new(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}")));

        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", Assert.Single(response.Headers.GetValues("Cache-Control")));
    }

    [Fact]
    public async Task Revoking_a_token_takes_the_access_away_and_answers_the_same_either_way()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var granted = await GrantAsync(app, browser, clientId);
        Assert.Equal(HttpStatusCode.OK, (await McpAsync(app, granted)).StatusCode);

        var revoked = await browser.PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = granted.GetProperty("refresh_token").GetString()!,
        }));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        var unknown = await browser.PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "nothing-like-a-token",
        }));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(app, granted)).StatusCode);
    }

    [Fact]
    public async Task The_settings_screen_lists_the_app_that_was_authorized_and_can_revoke_it()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        var clientId = await RegisteredClientIdAsync(browser);
        await SignInAsync(browser);

        var granted = await GrantAsync(app, browser, clientId);

        var settings = await browser.GetStringAsync("/settings");
        Assert.Contains("Connected apps", settings);
        Assert.Contains("Claude", settings);
        Assert.Contains(Callback, settings);

        var revoked = await FormPost.PostAsync(browser, "/settings", $"revoke-client-1");
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(app, granted)).StatusCode);
        Assert.Contains("revoked", await browser.GetStringAsync("/settings"));
    }

    [Fact]
    public async Task A_hand_minted_token_still_reaches_the_api_alongside_oauth()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var browser = Client(app);
        await SignInAsync(browser);

        await FormPost.PostAsync(browser, "/settings", "mint-token", new() { ["tokenForm.Name"] = "By hand" });
        var page = await browser.GetStringAsync("/settings");

        // The secret is shown once, on the redirect that follows the mint, so it is read there.
        var minted = await FormPost.PostAsync(browser, "/settings", "mint-token", new() { ["tokenForm.Name"] = "Another" });
        var html = await minted.Content.ReadAsStringAsync();
        var marker = "<code class=\"secret\">";
        var start = html.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, "The minted secret was not shown.");
        var secret = html[(start + marker.Length)..html.IndexOf('<', start + marker.Length)];

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);
        Assert.Contains("Connector tokens", page);
    }

    [Fact]
    public async Task A_bearer_token_that_is_not_one_of_ours_is_refused()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "capy_at_made-up-entirely");

        var response = await client.GetAsync("/api/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> McpAsync(AppFactory app, JsonElement tokens)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("access_token").GetString());
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        return await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "tests", version = "1.0" },
            },
        });
    }

    private static async Task<JsonElement> GrantAsync(AppFactory app, HttpClient browser, string clientId)
    {
        var consent = await browser.GetStringAsync(AuthorizeUrl(clientId));
        var code = await ApproveAsync(browser, clientId, consent);

        return await TokenAsync(browser, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["redirect_uri"] = Callback,
            ["code_verifier"] = Verifier,
        });
    }

    private static async Task<JsonElement> TokenAsync(HttpClient client, Dictionary<string, string> fields)
    {
        var response = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<string> ApproveAsync(HttpClient browser, string clientId, string consent)
    {
        var response = await DecideAsync(browser, clientId, consent, "approve");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("claude.ai", location.Host);

        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("the-state", query["state"]);

        var code = query["code"];
        Assert.False(string.IsNullOrWhiteSpace(code));
        return code!;
    }

    private static Task<HttpResponseMessage> DecideAsync(
        HttpClient browser,
        string clientId,
        string consent,
        string decision) =>
        browser.PostAsync("/oauth/authorize/decide", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = FormPost.Hidden(consent, "__RequestVerificationToken"),
            ["decision"] = decision,
            ["clientId"] = clientId,
            ["redirectUri"] = Callback,
            ["codeChallenge"] = Challenge(Verifier),
            ["state"] = "the-state",
            ["scope"] = "capybarahome",
            ["resource"] = "http://localhost/mcp",
        }));

    private static async Task SignInAsync(HttpClient client)
    {
        var page = await client.GetStringAsync("/sign-in");
        var response = await client.PostAsync("/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "sign-in",
            ["__RequestVerificationToken"] = FormPost.Hidden(page, "__RequestVerificationToken"),
            ["form.Username"] = AppFactory.SeededUsername,
            ["form.Password"] = AppFactory.SeededPassword,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client) =>
        await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Claude",
            redirect_uris = new[] { Callback },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
        });

    private static async Task<string> RegisteredClientIdAsync(HttpClient client) =>
        (await JsonAsync(await RegisterAsync(client))).GetProperty("client_id").GetString()!;

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static string AuthorizeUrl(
        string clientId,
        string redirectUri = Callback,
        string responseType = "code",
        string? challenge = null)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["response_type"] = responseType,
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = challenge ?? Challenge(Verifier),
            ["code_challenge_method"] = "S256",
            ["state"] = "the-state",
            ["scope"] = "capybarahome",
            ["resource"] = "http://localhost/mcp",
        };

        var query = string.Join(
            '&',
            parameters
                .Where(p => p.Value is not null)
                .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));

        return $"/oauth/authorize?{query}";
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static HttpClient Client(AppFactory app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
