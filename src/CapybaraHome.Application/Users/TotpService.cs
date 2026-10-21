using System.Security.Cryptography;
using System.Text;

namespace CapybaraHome.Application.Users;

/// <summary>
/// Time-based one-time passwords as RFC 6238 describes them: HMAC-SHA1 over a 30 second
/// counter, truncated to six digits. Authenticator apps all speak this, so nothing else
/// has to be configured for one to work.
/// </summary>
public static class TotpService
{
    private const int SecretBytes = 20;
    private const int Digits = 6;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>How many steps either side of now a code is still accepted, to allow for clock drift.</summary>
    private const int Window = 1;

    /// <summary>Returns a fresh secret, Base32 encoded as authenticator apps expect.</summary>
    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>Builds the <c>otpauth://</c> URI an authenticator app reads from a QR code or a paste.</summary>
    /// <param name="secret">Base32 secret from <see cref="GenerateSecret"/>.</param>
    /// <param name="account">Name the app shows for the account, usually the username.</param>
    /// <param name="issuer">Name the app shows for the service.</param>
    public static string BuildUri(string secret, string account, string issuer = "CapybaraHome")
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period=30";
    }

    /// <summary>True when <paramref name="code"/> is the code for <paramref name="secret"/> at or near <paramref name="now"/>.</summary>
    public static bool IsValid(string? secret, string? code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var typed = new string(code.Where(char.IsDigit).ToArray());
        if (typed.Length != Digits)
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Base32.Decode(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var counter = now.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

        for (var offset = -Window; offset <= Window; offset++)
        {
            var expected = Compute(key, counter + offset);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(typed)))
            {
                return true;
            }
        }

        return false;
    }

    // RFC 4226 dynamic truncation: the low nibble of the last byte picks where in the MAC to read
    // the four bytes that become the code.
    private static string Compute(byte[] key, long counter)
    {
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        var mac = HMACSHA1.HashData(key, message);
        var start = mac[^1] & 0x0F;
        var binary = ((mac[start] & 0x7F) << 24)
            | (mac[start + 1] << 16)
            | (mac[start + 2] << 8)
            | mac[start + 3];

        return (binary % (int)Math.Pow(10, Digits)).ToString(new string('0', Digits));
    }

    /// <summary>Base32 as RFC 4648 defines it, which is the alphabet authenticator apps use.</summary>
    private static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Encode(byte[] bytes)
        {
            var builder = new StringBuilder();
            var buffer = 0;
            var bits = 0;

            foreach (var value in bytes)
            {
                buffer = (buffer << 8) | value;
                bits += 8;

                while (bits >= 5)
                {
                    builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            if (bits > 0)
            {
                builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            }

            return builder.ToString();
        }

        public static byte[] Decode(string text)
        {
            var bytes = new List<byte>();
            var buffer = 0;
            var bits = 0;

            foreach (var character in text.Where(c => c != '=' && !char.IsWhiteSpace(c)))
            {
                var index = Alphabet.IndexOf(char.ToUpperInvariant(character));
                if (index < 0)
                {
                    throw new FormatException("The secret is not Base32.");
                }

                buffer = (buffer << 5) | index;
                bits += 5;

                if (bits >= 8)
                {
                    bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }

            return [.. bytes];
        }
    }
}
