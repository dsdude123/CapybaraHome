using CapybaraHome.Application.Homes;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the addresses the routing calls go to, since HeiGIT's hosted API and a self-hosted
/// instance serve the same endpoints under different paths.
/// </summary>
public class RoutingEndpointsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_address_stays_no_address(string? typed)
    {
        Assert.Null(RoutingEndpoints.Normalize(typed));
    }

    [Theory]
    [InlineData("https://api.openrouteservice.org")]
    [InlineData("https://api.openrouteservice.org/")]
    [InlineData("https://openrouteservice.org")]
    [InlineData("https://api.heigit.com")]
    [InlineData("api.openrouteservice.org")]
    public void A_retired_host_becomes_the_one_that_answers(string typed)
    {
        Assert.Equal(RoutingEndpoints.HostedBaseUrl, RoutingEndpoints.Normalize(typed));
    }

    [Theory]
    [InlineData("https://api.heigit.org", "https://api.heigit.org")]
    [InlineData("  https://api.heigit.org/  ", "https://api.heigit.org")]
    [InlineData("api.heigit.org", "https://api.heigit.org")]
    [InlineData("http://ors.local:8080/ors/", "http://ors.local:8080/ors")]
    [InlineData("ors.local:8080/ors", "https://ors.local:8080/ors")]
    public void Any_other_address_is_only_tidied(string typed, string expected)
    {
        Assert.Equal(expected, RoutingEndpoints.Normalize(typed));
    }

    [Theory]
    [InlineData("https://api.heigit.org", true)]
    [InlineData("https://heigit.org", true)]
    [InlineData("http://ors.local:8080/ors", false)]
    [InlineData("https://api.heigit.org.example.com", false)]
    public void The_hosted_service_is_told_apart_from_an_instance_of_your_own(string baseUrl, bool hosted)
    {
        Assert.Equal(hosted, RoutingEndpoints.IsHosted(baseUrl));
    }

    [Fact]
    public void The_hosted_service_geocodes_through_pelias_and_routes_under_its_own_prefix()
    {
        Assert.Equal(
            "pelias/v1/search?text=400%20Broad%20St&size=1",
            RoutingEndpoints.Geocode(RoutingEndpoints.HostedBaseUrl, "400 Broad St"));

        Assert.Equal(
            "openrouteservice/v2/directions/driving-car",
            RoutingEndpoints.Directions(RoutingEndpoints.HostedBaseUrl));
    }

    [Fact]
    public void An_instance_of_your_own_keeps_the_plain_paths()
    {
        const string BaseUrl = "http://ors.local:8080/ors";

        Assert.Equal("geocode/search?text=400%20Broad%20St&size=1", RoutingEndpoints.Geocode(BaseUrl, "400 Broad St"));
        Assert.Equal("v2/directions/driving-car", RoutingEndpoints.Directions(BaseUrl));
    }
}
