using CapybaraHome.Application.Users;

namespace CapybaraHome.Tests;

/// <summary>Covers the connector's bearer tokens: minted once, hashed at rest, revocable.</summary>
public class ApiTokenServiceTests
{
    [Fact]
    public async Task A_minted_secret_authenticates_and_is_not_stored_in_the_clear()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var tokens = new ApiTokenService(db.Context, db.Clock);
        var user = (await users.SignInAsync("https://issuer.example", "sub-1", "first@example.com", "First")).User!;

        var minted = await tokens.CreateAsync("Phone", user.Id);

        Assert.NotEmpty(minted.Secret);
        Assert.DoesNotContain(minted.Secret, db.Context.ApiTokens.Select(t => t.SecretHash));

        var authenticated = await tokens.AuthenticateAsync(minted.Secret);
        Assert.Equal(user.Id, authenticated!.Id);
    }

    [Fact]
    public async Task A_revoked_secret_stops_working()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var tokens = new ApiTokenService(db.Context, db.Clock);
        var user = (await users.SignInAsync("https://issuer.example", "sub-1", "first@example.com", "First")).User!;
        var minted = await tokens.CreateAsync("Phone", user.Id);

        Assert.True(await tokens.RevokeAsync(minted.Token.Id));

        Assert.Null(await tokens.AuthenticateAsync(minted.Secret));
    }

    [Fact]
    public async Task An_unknown_secret_is_rejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tokens = new ApiTokenService(db.Context, db.Clock);

        Assert.Null(await tokens.AuthenticateAsync("not-a-real-token"));
        Assert.Null(await tokens.AuthenticateAsync(string.Empty));
    }
}
