using System.Security.Cryptography;
using System.Text;
using CapybaraHome.Application.Users;
using CapybaraHome.Domain.Users;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the authorization server on its own: what it will register, how a code is checked
/// against its PKCE challenge, and what a replayed code or refresh token costs the grant it
/// belongs to.
/// </summary>
public class OAuthServiceTests
{
    private const string Verifier = "a-verifier-long-enough-to-be-a-real-one-0123456789";

    [Fact]
    public async Task A_public_client_registers_with_no_secret_and_a_confidential_one_with_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        var publicClient = await oauth.RegisterAsync(new OAuthClientRegistration(
            "Claude",
            ["https://claude.ai/api/mcp/auth_callback"],
            "none"));

        Assert.Null(publicClient.ClientSecret);
        Assert.Equal("Claude", publicClient.ClientName);
        Assert.NotEmpty(publicClient.ClientId);

        var confidential = await oauth.RegisterAsync(new OAuthClientRegistration(
            "Something else",
            ["https://example.test/callback"],
            "client_secret_post"));

        Assert.NotNull(confidential.ClientSecret);
        Assert.NotEqual(publicClient.ClientId, confidential.ClientId);
    }

    [Fact]
    public async Task A_registration_with_no_name_is_given_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        var registered = await oauth.RegisterAsync(new OAuthClientRegistration(
            null,
            ["https://example.test/callback"],
            null));

        Assert.Equal("Unnamed client", registered.ClientName);
        Assert.Equal("none", registered.TokenEndpointAuthMethod);
    }

    [Theory]
    [InlineData("http://example.test/callback", "invalid_redirect_uri")]
    [InlineData("not-a-uri", "invalid_redirect_uri")]
    [InlineData("https://example.test/callback#fragment", "invalid_redirect_uri")]
    public async Task A_redirect_uri_this_server_will_not_take_is_refused(string uri, string error)
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Client", [uri], "none")));

        Assert.Equal(error, refused.Error);
    }

    [Fact]
    public async Task A_registration_that_names_no_redirect_uris_at_all_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Client", null!, "none")));

        Assert.Equal("invalid_redirect_uri", refused.Error);
    }

    [Fact]
    public async Task A_loopback_redirect_uri_may_be_plain_http()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        var registered = await oauth.RegisterAsync(new OAuthClientRegistration(
            "A client on this machine",
            ["http://localhost:3000/callback", "http://127.0.0.1:3000/callback"],
            "none"));

        Assert.Equal(2, registered.RedirectUris.Count);
    }

    [Fact]
    public async Task A_registration_with_no_redirect_uri_or_an_unknown_auth_method_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        var noUri = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Client", [], "none")));
        Assert.Equal("invalid_redirect_uri", noUri.Error);

        var tooMany = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration(
                "Client",
                [
                    "https://example.test/c1",
                    "https://example.test/c2",
                    "https://example.test/c3",
                    "https://example.test/c4",
                    "https://example.test/c5",
                    "https://example.test/c6",
                ],
                "none")));
        Assert.Equal("invalid_redirect_uri", tooMany.Error);

        var method = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Client", ["https://example.test/c"], "private_key_jwt")));
        Assert.Equal("invalid_client_metadata", method.Error);
    }

    [Fact]
    public async Task A_registration_past_the_cap_drops_the_oldest_that_was_never_approved()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();

        for (var i = 0; i < 50; i++)
        {
            await oauth.RegisterAsync(new OAuthClientRegistration($"Client {i}", [$"https://example.test/cb{i}"], "none"));
        }

        var before = await oauth.ListClientsAsync();
        Assert.Equal(50, before.Count);

        // Registration needs no sign-in, so a caller that registers and walks away must not be
        // able to keep a real connector out. The oldest unapproved one goes instead.
        var latest = await oauth.RegisterAsync(
            new OAuthClientRegistration("One more", ["https://example.test/last"], "none"));

        var after = await oauth.ListClientsAsync();
        Assert.Equal(50, after.Count);
        Assert.Contains(after, c => c.ClientId == latest.ClientId);
        Assert.DoesNotContain(after, c => c.ClientId == before[^1].ClientId);
    }

    [Fact]
    public async Task Registration_stops_once_every_registered_client_has_been_authorized()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        await SeedUserAsync(db);

        for (var i = 0; i < 50; i++)
        {
            var client = await oauth.RegisterAsync(
                new OAuthClientRegistration($"Client {i}", [$"https://example.test/cb{i}"], "none"));
            await GrantAsync(db, oauth, client, $"https://example.test/cb{i}");
        }

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("One too many", ["https://example.test/last"], "none")));

        Assert.Equal("invalid_client_metadata", refused.Error);
        Assert.Contains("Revoke one in settings", refused.Message);

        // Revoking one makes room again, which is what the message tells the reader to do.
        var clients = await oauth.ListClientsAsync();
        Assert.True(await oauth.RevokeClientAsync(clients[0].Id));
        await oauth.RegisterAsync(new OAuthClientRegistration("Now it fits", ["https://example.test/last"], "none"));
    }

    [Fact]
    public async Task A_callback_outside_the_allowlist_is_refused_however_the_client_names_itself()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth("https://claude.ai/api/mcp/auth_callback");

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Claude", ["https://evil.example/cb"], "none")));

        Assert.Equal("invalid_redirect_uri", refused.Error);
        Assert.Contains("OAuth:AllowedRedirectUris", refused.Message);
    }

    [Fact]
    public async Task The_allowlist_matches_the_whole_origin_so_a_callback_path_may_move()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth("https://claude.ai/api/mcp/auth_callback");

        var registered = await oauth.RegisterAsync(
            new OAuthClientRegistration("Claude", ["https://claude.ai/api/mcp/somewhere_else"], "none"));

        Assert.Equal("https://claude.ai/api/mcp/somewhere_else", Assert.Single(registered.RedirectUris));

        // A different host on the same scheme is still somewhere else.
        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Claude", ["https://claude.ai.evil.example/cb"], "none")));
        Assert.Equal("invalid_redirect_uri", refused.Error);
    }

    [Fact]
    public async Task Claudes_own_callbacks_are_what_an_unconfigured_installation_accepts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = new OAuthService(db.Context, Options.Create(new OAuthOptions()), db.Clock);

        foreach (var callback in OAuthOptions.DefaultAllowedRedirectUris)
        {
            var registered = await oauth.RegisterAsync(new OAuthClientRegistration("Claude", [callback], "none"));
            Assert.Equal(callback, Assert.Single(registered.RedirectUris));
        }

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RegisterAsync(new OAuthClientRegistration("Claude", ["https://evil.example/cb"], "none")));
        Assert.Equal("invalid_redirect_uri", refused.Error);
    }

    [Fact]
    public async Task A_code_becomes_tokens_that_name_the_user_who_approved_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await RegisterAsync(oauth);

        var code = await oauth.IssueCodeAsync(
            client.ClientId,
            user,
            "https://example.test/callback",
            Challenge(Verifier),
            OAuthService.FullScope,
            "https://example.test/mcp");

        var tokens = await oauth.RedeemCodeAsync(
            code,
            client.ClientId,
            "https://example.test/callback",
            Verifier,
            null);

        Assert.StartsWith(OAuthService.AccessTokenPrefix, tokens.AccessToken);
        Assert.StartsWith(OAuthService.RefreshTokenPrefix, tokens.RefreshToken);
        Assert.Equal(3600, tokens.ExpiresInSeconds);

        var signedIn = await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken);
        Assert.Equal(user, signedIn!.Id);
    }

    [Fact]
    public async Task A_hand_minted_token_is_not_taken_as_an_oauth_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var minted = await new ApiTokenService(db.Context, db.Clock).CreateAsync("By hand", user);

        Assert.Null(await oauth.AuthenticateAccessTokenAsync(minted.Secret));
    }

    [Fact]
    public async Task The_wrong_code_verifier_does_not_redeem_the_code()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await RegisterAsync(oauth);

        var code = await oauth.IssueCodeAsync(
            client.ClientId, user, "https://example.test/callback", Challenge(Verifier), null, null);

        var refused = await Assert.ThrowsAsync<OAuthException>(() => oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/callback", "a-different-verifier-entirely", null));

        Assert.Equal("invalid_grant", refused.Error);
    }

    [Fact]
    public async Task A_code_redeemed_against_another_redirect_uri_or_client_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await RegisterAsync(oauth);
        var other = await oauth.RegisterAsync(new OAuthClientRegistration(
            "Another", ["https://other.test/callback"], "none"));

        var code = await oauth.IssueCodeAsync(
            client.ClientId, user, "https://example.test/callback", Challenge(Verifier), null, null);

        var wrongUri = await Assert.ThrowsAsync<OAuthException>(() => oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/elsewhere", Verifier, null));
        Assert.Equal("invalid_grant", wrongUri.Error);

        var wrongClient = await Assert.ThrowsAsync<OAuthException>(() => oauth.RedeemCodeAsync(
            code, other.ClientId, "https://example.test/callback", Verifier, null));
        Assert.Equal("invalid_grant", wrongClient.Error);
    }

    [Fact]
    public async Task A_code_offered_twice_takes_the_tokens_the_first_use_bought_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await RegisterAsync(oauth);

        var code = await oauth.IssueCodeAsync(
            client.ClientId, user, "https://example.test/callback", Challenge(Verifier), null, null);
        var tokens = await oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/callback", Verifier, null);

        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));

        var replay = await Assert.ThrowsAsync<OAuthException>(() => oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/callback", Verifier, null));

        Assert.Equal("invalid_grant", replay.Error);
        Assert.Null(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await RegisterAsync(oauth);

        var code = await oauth.IssueCodeAsync(
            client.ClientId, user, "https://example.test/callback", Challenge(Verifier), null, null);

        db.Clock.Advance(TimeSpan.FromMinutes(6));

        var refused = await Assert.ThrowsAsync<OAuthException>(() => oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/callback", Verifier, null));

        Assert.Equal("invalid_grant", refused.Error);
    }

    [Fact]
    public async Task An_access_token_stops_working_once_its_hour_is_up()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var tokens = await GrantAsync(db, oauth);

        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));

        db.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));
    }

    [Fact]
    public async Task A_refresh_rotates_the_pair_and_the_retired_token_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var client = await RegisterAsync(oauth);
        var tokens = await GrantAsync(db, oauth, client);

        var refreshed = await oauth.RefreshAsync(tokens.RefreshToken, client.ClientId, null);

        Assert.NotEqual(tokens.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(refreshed.AccessToken));

        // Offering the retired token back is treated as a leak, so the whole grant goes.
        var replay = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RefreshAsync(tokens.RefreshToken, client.ClientId, null));

        Assert.Equal("invalid_grant", replay.Error);
        Assert.Null(await oauth.AuthenticateAccessTokenAsync(refreshed.AccessToken));
    }

    [Fact]
    public async Task An_expired_refresh_token_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var client = await RegisterAsync(oauth);
        var tokens = await GrantAsync(db, oauth, client);

        db.Clock.Advance(TimeSpan.FromDays(61));

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RefreshAsync(tokens.RefreshToken, client.ClientId, null));

        Assert.Equal("invalid_grant", refused.Error);
    }

    [Fact]
    public async Task A_confidential_client_has_to_show_its_secret()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await oauth.RegisterAsync(new OAuthClientRegistration(
            "Confidential", ["https://example.test/callback"], "client_secret_post"));

        var code = await oauth.IssueCodeAsync(
            client.ClientId, user, "https://example.test/callback", Challenge(Verifier), null, null);

        var refused = await Assert.ThrowsAsync<OAuthException>(() => oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/callback", Verifier, "not-the-secret"));
        Assert.Equal("invalid_client", refused.Error);

        var accepted = await oauth.RedeemCodeAsync(
            code, client.ClientId, "https://example.test/callback", Verifier, client.ClientSecret);
        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(accepted.AccessToken));
    }

    [Fact]
    public async Task A_code_for_an_unregistered_client_or_redirect_uri_is_never_issued()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var user = await SeedUserAsync(db);
        var client = await RegisterAsync(oauth);

        var unknown = await Assert.ThrowsAsync<OAuthException>(() => oauth.IssueCodeAsync(
            "not-a-client-id", user, "https://example.test/callback", Challenge(Verifier), null, null));
        Assert.Equal("invalid_client", unknown.Error);

        var elsewhere = await Assert.ThrowsAsync<OAuthException>(() => oauth.IssueCodeAsync(
            client.ClientId, user, "https://evil.test/callback", Challenge(Verifier), null, null));
        Assert.Equal("invalid_request", elsewhere.Error);

        var noChallenge = await Assert.ThrowsAsync<OAuthException>(() => oauth.IssueCodeAsync(
            client.ClientId, user, "https://example.test/callback", string.Empty, null, null));
        Assert.Equal("invalid_request", noChallenge.Error);
    }

    [Fact]
    public async Task Revoking_a_client_stops_the_tokens_it_holds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var client = await RegisterAsync(oauth);
        var tokens = await GrantAsync(db, oauth, client);

        var listed = Assert.Single(await oauth.ListClientsAsync());
        Assert.Equal(2, listed.LiveTokens);
        Assert.Equal("Client", listed.ClientName);
        Assert.True(listed.RegisteredDynamically);
        Assert.NotNull(listed.LastUsedAt);

        Assert.True(await oauth.RevokeClientAsync(listed.Id));
        Assert.False(await oauth.RevokeClientAsync(listed.Id));

        Assert.Null(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));
        Assert.Equal(0, Assert.Single(await oauth.ListClientsAsync()).LiveTokens);

        var refused = await Assert.ThrowsAsync<OAuthException>(() =>
            oauth.RefreshAsync(tokens.RefreshToken, client.ClientId, null));
        Assert.Equal("invalid_client", refused.Error);
    }

    [Fact]
    public async Task Revoking_a_refresh_token_takes_the_access_token_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var tokens = await GrantAsync(db, oauth);

        Assert.False(await oauth.RevokeTokenAsync("nothing-like-a-token"));
        Assert.False(await oauth.RevokeTokenAsync(null));
        Assert.True(await oauth.RevokeTokenAsync(tokens.RefreshToken));

        Assert.Null(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));
    }

    [Fact]
    public async Task Revoking_one_access_token_leaves_the_refresh_token_alone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var client = await RegisterAsync(oauth);
        var tokens = await GrantAsync(db, oauth, client);

        Assert.True(await oauth.RevokeTokenAsync(tokens.AccessToken));
        Assert.Null(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));

        var refreshed = await oauth.RefreshAsync(tokens.RefreshToken, client.ClientId, null);
        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(refreshed.AccessToken));
    }

    [Fact]
    public async Task Pruning_clears_what_expired_long_ago_and_leaves_what_is_live()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var client = await RegisterAsync(oauth);
        var spent = await GrantAsync(db, oauth, client);

        db.Clock.Advance(TimeSpan.FromDays(200));
        var fresh = await GrantAsync(db, oauth, client);

        await oauth.PruneAsync();

        // The first grant's code and both its tokens are long past the refresh lifetime, so only
        // the second grant's rows are left.
        Assert.Equal(1, db.Context.OAuthAuthorizationCodes.Count());
        Assert.Equal(2, db.Context.OAuthTokens.Count());
        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(fresh.AccessToken));
        Assert.Null(await oauth.AuthenticateAccessTokenAsync(spent.AccessToken));
    }

    [Fact]
    public async Task A_registration_nobody_ever_approved_is_dropped_so_the_cap_cannot_be_filled()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var approved = await RegisterAsync(oauth);
        await GrantAsync(db, oauth, approved);
        await oauth.RegisterAsync(new OAuthClientRegistration("Never used", ["https://idle.test/cb"], "none"));

        Assert.Equal(2, (await oauth.ListClientsAsync()).Count);

        db.Clock.Advance(TimeSpan.FromDays(2));
        await oauth.PruneAsync();

        var left = Assert.Single(await oauth.ListClientsAsync());
        Assert.Equal(approved.ClientId, left.ClientId);
    }

    [Fact]
    public async Task A_fresh_registration_is_left_alone_by_a_prune()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        await oauth.RegisterAsync(new OAuthClientRegistration("Just registered", ["https://new.test/cb"], "none"));

        db.Clock.Advance(TimeSpan.FromHours(2));
        await oauth.PruneAsync();

        Assert.Single(await oauth.ListClientsAsync());
    }

    [Fact]
    public async Task The_consent_screen_only_finds_a_client_that_is_registered_and_live()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var client = await RegisterAsync(oauth);

        Assert.Null(await oauth.FindClientAsync(null));
        Assert.Null(await oauth.FindClientAsync("not-a-client"));
        Assert.Equal("Client", (await oauth.FindClientAsync(client.ClientId))!.ClientName);

        Assert.True(await oauth.IsRegisteredRedirectUriAsync(client.ClientId, "https://example.test/callback"));
        Assert.False(await oauth.IsRegisteredRedirectUriAsync(client.ClientId, "https://example.test/callback/"));
        Assert.False(await oauth.IsRegisteredRedirectUriAsync(client.ClientId, null));

        var listed = Assert.Single(await oauth.ListClientsAsync());
        Assert.True(await oauth.RevokeClientAsync(listed.Id));
        Assert.Null(await oauth.FindClientAsync(client.ClientId));
    }

    [Fact]
    public async Task Changing_a_password_drops_the_grants_that_were_authorized_under_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var users = new UserService(db.Context, db.Clock);
        var user = await users.CreateFirstUserAsync("conner", "Conner", "a-long-enough-password");
        var tokens = await GrantAsync(db, oauth);

        Assert.NotNull(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));

        await users.ChangePasswordAsync(user.Id, "a-long-enough-password", "a-different-long-password");

        Assert.Null(await oauth.AuthenticateAccessTokenAsync(tokens.AccessToken));
        Assert.Equal(0, Assert.Single(await oauth.ListClientsAsync()).LiveTokens);
    }

    [Fact]
    public async Task Resetting_a_password_or_turning_the_authenticator_off_drops_them_too()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oauth = db.OAuth();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("conner", "Conner", "a-long-enough-password");

        var reset = await GrantAsync(db, oauth);
        await users.ResetPasswordAsync(first.Id, "another-long-enough-password", first.Id);
        Assert.Null(await oauth.AuthenticateAccessTokenAsync(reset.AccessToken));

        var afterTotp = await GrantAsync(db, oauth);
        await users.DisableTotpAsync(first.Id, "another-long-enough-password");
        Assert.Null(await oauth.AuthenticateAccessTokenAsync(afterTotp.AccessToken));
    }

    [Fact]
    public async Task A_hand_minted_token_survives_a_password_change()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var user = await users.CreateFirstUserAsync("conner", "Conner", "a-long-enough-password");
        var tokens = new ApiTokenService(db.Context, db.Clock);
        var minted = await tokens.CreateAsync("Home Assistant", user.Id);

        await users.ChangePasswordAsync(user.Id, "a-long-enough-password", "a-different-long-password");

        // Deliberate: these are not tied to a sign-in, and revoking them would quietly stop
        // Home Assistant and every script. They are revoked by name on the settings screen.
        Assert.NotNull(await tokens.AuthenticateAsync(minted.Secret));
    }

    private static async Task<RegisteredOAuthClient> RegisterAsync(OAuthService oauth) =>
        await oauth.RegisterAsync(new OAuthClientRegistration("Client", ["https://example.test/callback"], "none"));

    private static async Task<IssuedOAuthTokens> GrantAsync(
        TestDatabase db,
        OAuthService oauth,
        RegisteredOAuthClient? client = null,
        string redirectUri = "https://example.test/callback")
    {
        client ??= await RegisterAsync(oauth);
        var user = await ExistingOrSeededUserAsync(db);
        var code = await oauth.IssueCodeAsync(client.ClientId, user, redirectUri, Challenge(Verifier), null, null);
        return await oauth.RedeemCodeAsync(code, client.ClientId, redirectUri, Verifier, client.ClientSecret);
    }

    private static async Task<int> ExistingOrSeededUserAsync(TestDatabase db)
    {
        var user = db.Context.Users.FirstOrDefault();
        return user?.Id ?? await SeedUserAsync(db);
    }

    private static async Task<int> SeedUserAsync(TestDatabase db)
    {
        var user = new AppUser
        {
            Username = "first",
            DisplayName = "First",
            PasswordHash = "not-checked-here",
            IsFirstUser = true,
            CreatedAt = db.Clock.GetUtcNow(),
        };

        db.Context.Users.Add(user);
        await db.Context.SaveChangesAsync();
        return user.Id;
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
