using CapybaraHome.Application.Users;
using CapybaraHome.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the address every OAuth document agrees on: whether it follows the request or the
/// configured issuer, and which resource indicators are taken as this installation's own.
/// </summary>
public class OAuthIssuerTests
{
    [Fact]
    public void With_nothing_configured_the_issuer_follows_the_request()
    {
        var issuer = Issuer(null);

        Assert.Equal("https://home.example", issuer.For(Request()));
        Assert.Equal("https://home.example/mcp", issuer.ResourceFor(Request()));
        Assert.Equal(
            "https://home.example/.well-known/oauth-protected-resource/mcp",
            issuer.ResourceMetadataFor(Request()));
    }

    [Fact]
    public void A_path_base_is_carried_into_the_issuer()
    {
        var request = Request();
        request.PathBase = "/capybara";

        Assert.Equal("https://home.example/capybara", Issuer(null).For(request));
    }

    [Theory]
    [InlineData("https://proxied.example", "https://proxied.example")]
    [InlineData("https://proxied.example/", "https://proxied.example")]
    public void A_configured_issuer_replaces_the_address_the_request_arrived_on(string configured, string expected)
    {
        Assert.Equal(expected, Issuer(configured).For(Request()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://home.example/mcp")]
    [InlineData("https://home.example/mcp/")]
    [InlineData("HTTPS://HOME.EXAMPLE/MCP")]
    public void A_resource_this_server_protects_or_none_at_all_is_accepted(string? resource)
    {
        Assert.True(Issuer(null).IsOwnResource(Request(), resource));
    }

    [Theory]
    [InlineData("https://somewhere.example/mcp")]
    [InlineData("https://home.example/something-else")]
    public void A_resource_somewhere_else_is_not(string resource)
    {
        Assert.False(Issuer(null).IsOwnResource(Request(), resource));
    }

    private static OAuthIssuer Issuer(string? configured) =>
        new(Options.Create(new OAuthOptions { Issuer = configured }));

    private static HttpRequest Request()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("home.example");
        return context.Request;
    }
}
