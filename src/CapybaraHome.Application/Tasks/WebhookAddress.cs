using System.Net;
using System.Net.Sockets;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Decides which addresses a webhook may be sent to. A webhook is a URL the user types and the
/// server then fetches, so the checks here are what stops one from being pointed back at the
/// container or at a cloud instance's metadata service. Addresses on the home network are
/// allowed on purpose: a webhook to something else on the LAN is the ordinary case for this app.
/// </summary>
public static class WebhookAddress
{
    /// <summary>
    /// Checks a URL as typed and returns the message to show when it cannot be used, or null when
    /// it can. A host given as a name is only resolved when the call is made, so a name that
    /// resolves to a refused address fails then rather than here.
    /// </summary>
    /// <param name="url">URL as the user typed it.</param>
    /// <param name="parsed">The parsed URL, when the check passed.</param>
    public static string? Validate(string? url, out Uri? parsed)
    {
        parsed = null;
        var text = (url ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return "A webhook needs a URL.";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return "That is not a URL. It has to start with http:// or https://.";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return "A webhook URL has to be http or https.";
        }

        if (uri.UserInfo.Length > 0)
        {
            return "Leave the username and password out of the URL.";
        }

        if (IPAddress.TryParse(uri.DnsSafeHost, out var address) && !IsAllowed(address))
        {
            return "That address cannot be called: it is the server itself or a link-local address.";
        }

        parsed = uri;
        return null;
    }

    /// <summary>Whether a webhook may be sent to <paramref name="address"/>.</summary>
    /// <param name="address">Address the host resolved to.</param>
    public static bool IsAllowed(IPAddress address)
    {
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(plain) || plain.Equals(IPAddress.Any) || plain.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (plain.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !plain.IsIPv6LinkLocal && !plain.IsIPv6Multicast;
        }

        var octets = plain.GetAddressBytes();

        // 169.254/16 is link-local, which is where a cloud instance's metadata service answers,
        // and 224/4 upwards is multicast and broadcast rather than a host that can accept a call.
        return !(octets[0] == 169 && octets[1] == 254) && octets[0] < 224;
    }
}
