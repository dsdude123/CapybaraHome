using System.Net;
using CapybaraHome.Application.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the calls to OpenRouteService and what the app does with the answers.</summary>
public class RoutingClientTests
{
    private const string OnePlace = """
    { "features": [ { "geometry": { "type": "Point", "coordinates": [-122.33, 47.61] },
                      "properties": { "label": "400 Broad St, Seattle, WA" } } ] }
    """;

    private const string OneRoute = """
    { "routes": [ { "summary": { "distance": 12400.0, "duration": 1110.0 } } ] }
    """;

    [Fact]
    public async Task An_address_comes_back_as_coordinates_the_right_way_round()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, http) = await ClientAsync(db, OnePlace);

        var placed = await client.PlaceAsync("400 Broad St");

        Assert.Equal(47.61d, placed!.Latitude);
        Assert.Equal(-122.33d, placed.Longitude);
        Assert.Equal("400 Broad St, Seattle, WA", placed.Label);
        Assert.Contains("geocode/search?text=400%20Broad%20St", http.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("a-key", http.Requests[0].Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task An_address_the_geocoder_does_not_know_comes_back_as_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, """{ "features": [] }""");

        Assert.Null(await client.PlaceAsync("Nowhere at all"));
    }

    [Fact]
    public async Task A_place_with_no_coordinates_on_it_is_an_error_rather_than_a_silent_zero()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, """{ "features": [ { "properties": { "label": "Somewhere" } } ] }""");

        var thrown = await Assert.ThrowsAsync<RoutingException>(() => client.PlaceAsync("Somewhere"));
        Assert.Contains("coordinates", thrown.Message);
    }

    [Fact]
    public async Task Looking_up_nothing_is_refused_before_the_call_is_made()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, http) = await ClientAsync(db, OnePlace);

        await Assert.ThrowsAsync<RoutingException>(() => client.PlaceAsync("   "));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_route_comes_back_as_minutes_and_miles()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, http) = await ClientAsync(db, OneRoute);

        var drive = await client.MeasureAsync(47.6d, -122.3d, 47.7d, -122.4d);

        Assert.Equal(18.5d, drive!.Minutes);
        Assert.Equal(7.705d, drive.Miles, 3);
        Assert.Equal(HttpMethod.Post, http.Requests[0].Method);
        Assert.Contains("v2/directions/driving-car", http.Requests[0].RequestUri!.ToString());

        Assert.Contains("[-122.3,47.6]", http.Bodies[0]);
        Assert.Contains("\"extra_info\":[\"tollways\"]", http.Bodies[0]);
        Assert.Empty(drive.TolledRoads);
    }

    [Fact]
    public async Task A_route_names_the_roads_that_run_over_a_tolled_stretch()
    {
        const string Tolled = """
        {
          "routes": [ {
            "summary": { "distance": 16093.44, "duration": 1200.0 },
            "segments": [
              { "steps": [
                { "name": "1st Avenue", "way_points": [0, 4] },
                { "name": "WA 520", "way_points": [4, 9] },
                { "name": "WA 520", "way_points": [9, 12] }
              ] },
              { "steps": [
                { "name": "-", "way_points": [12, 14] },
                { "name": "", "way_points": [14, 15] },
                { "name": "Bellevue Way", "way_points": [15, 20] },
                { "name": "Unnamed shape" },
                { "way_points": [20, 21] }
              ] }
            ],
            "extras": { "tollways": { "values": [ [0, 4, 0], [4, 15, 1], [15, 20, 0], "bad" ] } }
          } ]
        }
        """;

        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, Tolled);

        var drive = await client.MeasureAsync(47.6d, -122.3d, 47.7d, -122.4d);

        // The unnamed steps over the tolled stretch name nothing, and a road is named once.
        Assert.Equal(["WA 520"], drive!.TolledRoads);
        Assert.Equal(10d, drive.Miles, 3);
    }

    [Fact]
    public async Task A_route_with_no_steps_names_no_tolled_road_even_when_one_is_tolled()
    {
        const string Stepless = """
        {
          "routes": [ {
            "summary": { "distance": 1000.0, "duration": 60.0 },
            "extras": { "tollways": { "values": [ [0, 4, 1] ] } }
          } ]
        }
        """;

        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, Stepless);

        Assert.Empty((await client.MeasureAsync(47.6d, -122.3d, 47.7d, -122.4d))!.TolledRoads);
    }

    [Theory]
    [InlineData("""{ "routes": [] }""")]
    [InlineData("""{ "routes": [ { } ] }""")]
    [InlineData("""{ "error": "no route" }""")]
    public async Task A_pair_of_points_with_no_road_between_them_comes_back_as_nothing(string payload)
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, payload);

        Assert.Null(await client.MeasureAsync(47.6d, -122.3d, 0d, 0d));
    }

    [Fact]
    public async Task A_refusal_from_the_service_is_reported_with_its_status_and_body()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, """{ "error": "quota exceeded" }""", HttpStatusCode.TooManyRequests);

        var thrown = await Assert.ThrowsAsync<RoutingException>(() => client.MeasureAsync(1d, 2d, 3d, 4d));

        Assert.Contains("429", thrown.Message);
        Assert.Contains("quota exceeded", thrown.Message);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_reported_rather_than_thrown_raw()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, "<html>a proxy page</html>");

        var thrown = await Assert.ThrowsAsync<RoutingException>(() => client.MeasureAsync(1d, 2d, 3d, 4d));
        Assert.Contains("not JSON", thrown.Message);
    }

    [Fact]
    public async Task A_long_refusal_is_cut_short_before_it_goes_on_a_page()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (client, _) = await ClientAsync(db, new string('x', 900), HttpStatusCode.BadGateway);

        var thrown = await Assert.ThrowsAsync<RoutingException>(() => client.MeasureAsync(1d, 2d, 3d, 4d));

        Assert.EndsWith("…", thrown.Message);
        Assert.True(thrown.Message.Length < 400);
    }

    [Fact]
    public async Task With_no_service_set_up_nothing_is_called_at_all()
    {
        await using var db = await TestDatabase.CreateAsync();
        var search = new HomeSearchService(db.Context, db.Clock);
        var http = new FakeHttp().Reply(OnePlace);
        var client = new RoutingClient(http.Client(), search);

        await Assert.ThrowsAsync<RoutingException>(() => client.PlaceAsync("400 Broad St"));
        await Assert.ThrowsAsync<RoutingException>(() => client.MeasureAsync(1d, 2d, 3d, 4d));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_self_hosted_instance_is_called_without_a_key()
    {
        await using var db = await TestDatabase.CreateAsync();
        var search = new HomeSearchService(db.Context, db.Clock);
        await search.SaveSettingsAsync(new HomeSearchSettingsRequest(RoutingBaseUrl: "http://ors.local:8080/ors/"));

        var http = new FakeHttp().Reply(OnePlace);
        var client = new RoutingClient(http.Client(), search);

        await client.PlaceAsync("400 Broad St");

        Assert.StartsWith("http://ors.local:8080/ors/geocode/search", http.Requests[0].RequestUri!.ToString());
        Assert.False(http.Requests[0].Headers.Contains("Authorization"));
    }

    private static async Task<(RoutingClient Client, FakeHttp Http)> ClientAsync(
        TestDatabase db,
        string payload,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var search = new HomeSearchService(db.Context, db.Clock);
        await search.SaveSettingsAsync(new HomeSearchSettingsRequest(
            RoutingBaseUrl: "https://api.openrouteservice.org", RoutingApiKey: "a-key"));

        var http = new FakeHttp().Reply(payload, status);
        return (new RoutingClient(http.Client(), search), http);
    }
}
