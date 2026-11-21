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

    [Fact]
    public void With_no_authorization_endpoint_configured_the_consent_page_is_the_issuers_own()
    {
        Assert.Equal("https://home.example/oauth/authorize", Issuer(null).AuthorizationEndpointFor(Request()));
        Assert.Equal(
            "https://proxied.example/oauth/authorize",
            Issuer("https://proxied.example").AuthorizationEndpointFor(Request()));
    }

    [Fact]
    public void A_configured_authorization_endpoint_sends_the_browser_to_another_host()
    {
        var issuer = Issuer("https://public.example", "https://home.internal.example/oauth/authorize");

        Assert.Equal("https://home.internal.example/oauth/authorize", issuer.AuthorizationEndpointFor(Request()));

        // Only the consent page moves: the issuer, and so the token and registration endpoints
        // and the resource the consent page checks against, stay on the public address.
        Assert.Equal("https://public.example", issuer.For(Request()));
        Assert.True(issuer.IsOwnResource(Request(), "https://public.example/mcp"));
    }

    private static OAuthIssuer Issuer(string? configured, string? authorizationEndpoint = null) =>
        new(Options.Create(new OAuthOptions { Issuer = configured, AuthorizationEndpoint = authorizationEndpoint }));

    private static HttpRequest Request()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("home.example");
        return context.Request;
    }
}
