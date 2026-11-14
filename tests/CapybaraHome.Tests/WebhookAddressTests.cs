using System.Net;
using CapybaraHome.Application.Tasks;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers which addresses a webhook may be sent to. A webhook is a URL the user types and the
/// server fetches, so these are the checks that keep one from being aimed back at the container.
/// </summary>
public class WebhookAddressTests
{
    [Theory]
    [InlineData("https://hooks.example.com/task")]
    [InlineData("http://tokyo-3-wsl.nerv.jpn.com:8123/api/webhook/x")]
    [InlineData("http://192.168.1.50/hook")]
    [InlineData("http://10.0.0.8:1880/endpoint")]
    public void A_URL_on_the_network_or_the_web_is_accepted(string url)
    {
        Assert.Null(WebhookAddress.Validate(url, out var parsed));
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("https://someone:secret@example.com/x")]
    [InlineData("http://127.0.0.1:8080/x")]
    [InlineData("http://[::1]/x")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/x")]
    public void A_URL_that_cannot_be_called_comes_back_with_a_reason(string url)
    {
        Assert.NotNull(WebhookAddress.Validate(url, out var parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("192.168.4.4", true)]
    [InlineData("172.16.0.9", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("fd00::1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("239.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    public void The_resolved_address_decides_whether_the_call_goes_out(string address, bool allowed)
    {
        Assert.Equal(allowed, WebhookAddress.IsAllowed(IPAddress.Parse(address)));
    }
}
