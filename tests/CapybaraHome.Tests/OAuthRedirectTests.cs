using System.Web;
using CapybaraHome.Web.Auth;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the URLs a client is sent back to, including the callback that already carries a
/// query of its own and the one that ends up with nothing to add.
/// </summary>
public class OAuthRedirectTests
{
    [Fact]
    public void A_code_is_added_to_a_plain_callback()
    {
        var url = OAuthRedirect.Code("https://claude.ai/cb", "the-code", "the-state");

        Assert.StartsWith("https://claude.ai/cb?", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("the-code", query["code"]);
        Assert.Equal("the-state", query["state"]);
    }

    [Fact]
    public void A_callback_that_already_carries_a_query_keeps_it()
    {
        var url = OAuthRedirect.Code("https://claude.ai/cb?tenant=home", "the-code", null);

        Assert.StartsWith("https://claude.ai/cb?tenant=home&", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("home", query["tenant"]);
        Assert.Equal("the-code", query["code"]);
        Assert.Null(query["state"]);
    }

    [Fact]
    public void An_error_names_the_code_the_description_and_the_state()
    {
        var url = OAuthRedirect.Error("https://claude.ai/cb", "invalid_target", "Not this server.", "the-state");

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("invalid_target", query["error"]);
        Assert.Equal("Not this server.", query["error_description"]);
        Assert.Equal("the-state", query["state"]);
    }

    [Fact]
    public void With_nothing_to_add_the_callback_is_returned_as_it_stands()
    {
        Assert.Equal(
            "https://claude.ai/cb",
            OAuthRedirect.Code("https://claude.ai/cb", string.Empty, null));
    }

    [Fact]
    public void A_value_that_would_break_the_query_is_escaped()
    {
        var url = OAuthRedirect.Error("https://claude.ai/cb", "invalid_request", "A & B = C?", "a state");

        Assert.DoesNotContain("A & B", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("A & B = C?", query["error_description"]);
        Assert.Equal("a state", query["state"]);
    }
}
