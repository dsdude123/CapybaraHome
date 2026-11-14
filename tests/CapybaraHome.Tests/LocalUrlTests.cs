using CapybaraHome.Web;

namespace CapybaraHome.Tests;

/// <summary>Covers the check the editors put a return address through before following it.</summary>
public class LocalUrlTests
{
    [Theory]
    [InlineData("meals")]
    [InlineData("meals/plan")]
    [InlineData("meals/plan?text=soup&tag=1")]
    [InlineData("meals/4?return=meals%2Fplan")]
    [InlineData("report")]
    public void A_page_of_this_app_is_kept(string value)
    {
        Assert.Equal(value, LocalUrl.Within(value));
    }

    [Fact]
    public void Surrounding_space_is_trimmed()
    {
        Assert.Equal("meals/plan", LocalUrl.Within("  meals/plan  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com/meals")]
    [InlineData("//example.com/meals")]
    [InlineData("/meals")]
    [InlineData("\\\\example.com\\meals")]
    [InlineData("meals\\plan")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("#top")]
    [InlineData("meals\nplan")]
    [InlineData("meals plan")]
    public void Anything_else_is_refused(string? value)
    {
        Assert.Null(LocalUrl.Within(value));
    }
}
