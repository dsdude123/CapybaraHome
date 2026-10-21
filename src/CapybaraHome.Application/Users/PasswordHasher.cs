using System.Security.Cryptography;

namespace CapybaraHome.Application.Users;

/// <summary>
/// Hashes and checks passwords with PBKDF2-HMAC-SHA256. A hash carries its own salt and
/// iteration count, so the count can be raised later without invalidating existing rows.
/// </summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 210_000;
    private const string Prefix = "pbkdf2-sha256";

    /// <summary>Shortest password the app accepts.</summary>
    public const int MinimumLength = 10;

    /// <summary>Returns a hash of <paramref name="password"/>, salted and ready to store.</summary>
    /// <exception cref="ArgumentException">The password is shorter than <see cref="MinimumLength"/>.</exception>
    public static string Hash(string password)
    {
        if ((password ?? string.Empty).Length < MinimumLength)
        {
            throw new ArgumentException($"A password needs at least {MinimumLength} characters.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password!, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

        return string.Join('$', Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    /// <summary>Checks <paramref name="password"/> against a stored hash, in time that does not vary with the answer.</summary>
    public static bool Verify(string? password, string? hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
