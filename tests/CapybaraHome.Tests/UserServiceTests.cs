using CapybaraHome.Application.Users;

namespace CapybaraHome.Tests;

/// <summary>Covers the account rules: who may sign in, who may add accounts, and the codes.</summary>
public class UserServiceTests
{
    private const string Password = "a-long-enough-password";

    [Fact]
    public async Task The_first_account_claims_the_installation()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);

        Assert.False(await users.AnyUsersAsync());

        var first = await users.CreateFirstUserAsync("Conner", "Conner", Password);

        Assert.True(first.IsFirstUser);
        Assert.Equal("conner", first.Username);
        Assert.True(await users.AnyUsersAsync());
    }

    [Fact]
    public async Task A_second_first_account_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        await users.CreateFirstUserAsync("first", "First", Password);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => users.CreateFirstUserAsync("second", "Second", Password));
    }

    [Fact]
    public async Task The_right_password_signs_in_and_stamps_the_time()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        await users.CreateFirstUserAsync("first", "First", Password);

        db.Clock.Advance(TimeSpan.FromHours(2));
        var result = await users.SignInAsync("First", Password, null);

        Assert.Equal(SignInOutcome.Succeeded, result.Outcome);
        Assert.Equal(db.Clock.GetUtcNow(), result.User!.LastSignInAt);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_username_are_refused_the_same_way()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        await users.CreateFirstUserAsync("first", "First", Password);

        var wrongPassword = await users.SignInAsync("first", "not-the-password", null);
        var noSuchUser = await users.SignInAsync("nobody", Password, null);

        Assert.Equal(SignInOutcome.WrongCredentials, wrongPassword.Outcome);
        Assert.Equal(SignInOutcome.WrongCredentials, noSuchUser.Outcome);
        Assert.Null(wrongPassword.User);
        Assert.Null(noSuchUser.User);
    }

    [Fact]
    public async Task The_first_account_can_add_another_and_that_one_cannot()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);

        var second = await users.CreateUserAsync("second", "Second", Password, first.Id);

        Assert.False(second.IsFirstUser);
        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("second", Password, null)).Outcome);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => users.CreateUserAsync("third", "Third", Password, second.Id));
    }

    [Fact]
    public async Task A_taken_username_and_a_short_password_are_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);

        await Assert.ThrowsAsync<ArgumentException>(
            () => users.CreateUserAsync("FIRST", "Another", Password, first.Id));

        await Assert.ThrowsAsync<ArgumentException>(
            () => users.CreateUserAsync("second", "Second", "short", first.Id));
    }

    [Fact]
    public async Task Changing_a_password_needs_the_current_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);

        await Assert.ThrowsAsync<ArgumentException>(
            () => users.ChangePasswordAsync(first.Id, "not-the-password", "another-long-password"));

        await users.ChangePasswordAsync(first.Id, Password, "another-long-password");

        Assert.Equal(SignInOutcome.WrongCredentials, (await users.SignInAsync("first", Password, null)).Outcome);
        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("first", "another-long-password", null)).Outcome);
    }

    [Fact]
    public async Task The_first_account_can_reset_someone_elses_password()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);
        var second = await users.CreateUserAsync("second", "Second", Password, first.Id);

        await users.ResetPasswordAsync(second.Id, "a-reset-password", first.Id);
        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("second", "a-reset-password", null)).Outcome);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => users.ResetPasswordAsync(first.Id, "another-long-password", second.Id));
    }

    [Fact]
    public async Task An_authenticator_is_only_required_once_a_code_confirms_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);

        var enrolment = await users.BeginTotpEnrolmentAsync(first.Id);

        // Started but unconfirmed: sign-in still works without a code, so a half-finished
        // setup cannot lock anyone out.
        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("first", Password, null)).Outcome);

        Assert.False(await users.ConfirmTotpAsync(first.Id, "000000"));
        Assert.True(await users.ConfirmTotpAsync(first.Id, CodeFor(enrolment.Secret, db.Clock.GetUtcNow())));

        Assert.Equal(SignInOutcome.CodeRequired, (await users.SignInAsync("first", Password, null)).Outcome);
        Assert.Equal(SignInOutcome.WrongCode, (await users.SignInAsync("first", Password, "000000")).Outcome);

        // Confirming spent that step's code, so signing in needs the next one.
        db.Clock.Advance(TimeSpan.FromSeconds(30));

        var withCode = await users.SignInAsync("first", Password, CodeFor(enrolment.Secret, db.Clock.GetUtcNow()));
        Assert.Equal(SignInOutcome.Succeeded, withCode.Outcome);
        Assert.True(withCode.User!.TotpEnabled);
    }

    [Fact]
    public async Task A_code_that_signed_in_once_cannot_sign_in_again()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);
        var enrolment = await users.BeginTotpEnrolmentAsync(first.Id);
        Assert.True(await users.ConfirmTotpAsync(first.Id, CodeFor(enrolment.Secret, db.Clock.GetUtcNow())));

        db.Clock.Advance(TimeSpan.FromSeconds(30));
        var code = CodeFor(enrolment.Secret, db.Clock.GetUtcNow());

        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("first", Password, code)).Outcome);

        // Someone who held the password and saw that code gets nothing from it, for the rest of
        // the step and for the drift window after it.
        Assert.Equal(SignInOutcome.WrongCode, (await users.SignInAsync("first", Password, code)).Outcome);

        db.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(SignInOutcome.WrongCode, (await users.SignInAsync("first", Password, code)).Outcome);

        // The next code the app shows still works.
        var next = CodeFor(enrolment.Secret, db.Clock.GetUtcNow());
        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("first", Password, next)).Outcome);
    }

    [Fact]
    public async Task Re_enrolling_forgets_the_codes_the_old_secret_spent()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);
        var old = await users.BeginTotpEnrolmentAsync(first.Id);
        Assert.True(await users.ConfirmTotpAsync(first.Id, CodeFor(old.Secret, db.Clock.GetUtcNow())));

        // A fresh secret starts over, so the same step is usable again under it.
        var replacement = await users.BeginTotpEnrolmentAsync(first.Id);
        Assert.True(await users.ConfirmTotpAsync(first.Id, CodeFor(replacement.Secret, db.Clock.GetUtcNow())));
    }

    [Fact]
    public async Task Turning_codes_off_needs_the_password()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);
        var enrolment = await users.BeginTotpEnrolmentAsync(first.Id);
        await users.ConfirmTotpAsync(first.Id, CodeFor(enrolment.Secret, db.Clock.GetUtcNow()));

        await Assert.ThrowsAsync<ArgumentException>(() => users.DisableTotpAsync(first.Id, "not-the-password"));

        await users.DisableTotpAsync(first.Id, Password);

        Assert.Equal(SignInOutcome.Succeeded, (await users.SignInAsync("first", Password, null)).Outcome);
    }

    [Fact]
    public async Task The_first_account_cannot_be_removed_and_the_others_can()
    {
        await using var db = await TestDatabase.CreateAsync();
        var users = new UserService(db.Context, db.Clock);
        var first = await users.CreateFirstUserAsync("first", "First", Password);
        var second = await users.CreateUserAsync("second", "Second", Password, first.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => users.DeleteUserAsync(first.Id, first.Id));

        Assert.True(await users.DeleteUserAsync(second.Id, first.Id));
        Assert.Single(await users.ListAsync());
    }

    // The same computation an authenticator app does, so the tests exercise the real check.
    private static string CodeFor(string secret, DateTimeOffset at) => TotpService.ComputeCode(secret, at);
}
