using CapybaraHome.Application.Users;

namespace CapybaraHome.Tests;

/// <summary>Covers the rule that the first identity claims the installation and the rest need an invite.</summary>
public class UserServiceTests
{
    private const string Issuer = "https://issuer.example";

    [Fact]
    public async Task The_first_identity_to_arrive_becomes_the_first_user()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);

        var result = await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First");

        Assert.Equal(SignInOutcome.FirstUser, result.Outcome);
        Assert.True(result.User!.IsFirstUser);
    }

    [Fact]
    public async Task A_second_identity_with_no_invite_is_turned_away()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First");

        var result = await users.SignInAsync(Issuer, "sub-2", "second@example.com", "Second");

        Assert.Equal(SignInOutcome.NotInvited, result.Outcome);
        Assert.Null(result.User);
        Assert.Single(await users.ListAsync());
    }

    [Fact]
    public async Task An_invited_address_gets_in_and_spends_its_invite()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First");
        await users.InviteAsync("Second@Example.com", first.User!.Id);

        var result = await users.SignInAsync(Issuer, "sub-2", "second@example.com", "Second");

        Assert.Equal(SignInOutcome.Invited, result.Outcome);
        Assert.False(result.User!.IsFirstUser);

        var again = await users.SignInAsync(Issuer, "sub-3", "second@example.com", "Impostor");
        Assert.Equal(SignInOutcome.NotInvited, again.Outcome);
    }

    [Fact]
    public async Task A_withdrawn_invite_no_longer_lets_anyone_in()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First");
        var invite = await users.InviteAsync("second@example.com", first.User!.Id);

        Assert.True(await users.RevokeInviteAsync(invite.Id));

        var result = await users.SignInAsync(Issuer, "sub-2", "second@example.com", "Second");
        Assert.Equal(SignInOutcome.NotInvited, result.Outcome);
    }

    [Fact]
    public async Task Signing_in_again_updates_the_name_and_the_last_sign_in()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First");

        db.Clock.Advance(TimeSpan.FromDays(1));
        var result = await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First Renamed");

        Assert.Equal(SignInOutcome.ExistingUser, result.Outcome);
        Assert.Equal("First Renamed", result.User!.DisplayName);
        Assert.Equal(db.Clock.GetUtcNow(), result.User.LastSignInAt);
    }

    [Fact]
    public async Task Inviting_an_address_twice_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.SignInAsync(Issuer, "sub-1", "first@example.com", "First");

        await users.InviteAsync("second@example.com", first.User!.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => users.InviteAsync("second@example.com", first.User.Id));
    }
}
