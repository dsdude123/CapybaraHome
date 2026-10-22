using CapybaraHome.Application.Users;

namespace CapybaraHome.Tests;

/// <summary>Covers the password hashing and the one-time-password computation on their own.</summary>
public class PasswordAndTotpTests
{
    [Fact]
    public void A_hash_verifies_its_own_password_and_nothing_else()
    {
        var hash = PasswordHasher.Hash("a-long-enough-password");

        Assert.True(PasswordHasher.Verify("a-long-enough-password", hash));
        Assert.False(PasswordHasher.Verify("a-long-enough-passwore", hash));
        Assert.False(PasswordHasher.Verify(null, hash));
        Assert.False(PasswordHasher.Verify("a-long-enough-password", null));
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        var first = PasswordHasher.Hash("a-long-enough-password");
        var second = PasswordHasher.Hash("a-long-enough-password");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("a-long-enough-password", first));
        Assert.True(PasswordHasher.Verify("a-long-enough-password", second));
    }

    [Fact]
    public void A_password_under_the_minimum_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(new string('x', PasswordHasher.MinimumLength - 1)));
    }

    [Fact]
    public void A_damaged_hash_verifies_nothing_rather_than_throwing()
    {
        Assert.False(PasswordHasher.Verify("a-long-enough-password", "not-a-hash"));
        Assert.False(PasswordHasher.Verify("a-long-enough-password", "pbkdf2-sha256$210000$not-base64$nor-this"));
    }

    // RFC 6238 appendix B, the SHA-1 rows. The secret there is the ASCII string
    // "12345678901234567890", which is this in the Base32 an authenticator app takes.
    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(20000000000, "353130")]
    public void Codes_match_the_published_test_vectors(long unixSeconds, string expected)
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        Assert.True(TotpService.IsValid(secret, expected, at));
    }

    [Fact]
    public void A_code_is_accepted_one_step_either_side_and_no_further()
    {
        var secret = TotpService.GenerateSecret();
        var now = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        var code = TotpService.ComputeCode(secret, now);

        Assert.True(TotpService.IsValid(secret, code, now.AddSeconds(-30)));
        Assert.True(TotpService.IsValid(secret, code, now));
        Assert.True(TotpService.IsValid(secret, code, now.AddSeconds(30)));

        Assert.False(TotpService.IsValid(secret, code, now.AddSeconds(-60)));
        Assert.False(TotpService.IsValid(secret, code, now.AddSeconds(60)));
        Assert.False(TotpService.IsValid(secret, code, now.AddSeconds(120)));
    }

    [Fact]
    public void A_malformed_code_or_secret_is_refused_rather_than_throwing()
    {
        var secret = TotpService.GenerateSecret();

        Assert.False(TotpService.IsValid(secret, "12345", DateTimeOffset.UtcNow));
        Assert.False(TotpService.IsValid(secret, null, DateTimeOffset.UtcNow));
        Assert.False(TotpService.IsValid("not base32 at all!", "123456", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void The_enrolment_uri_carries_the_secret_and_the_account()
    {
        var secret = TotpService.GenerateSecret();

        var uri = TotpService.BuildUri(secret, "conner");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={secret}", uri);
        Assert.Contains("CapybaraHome%3Aconner", uri);
    }
}
