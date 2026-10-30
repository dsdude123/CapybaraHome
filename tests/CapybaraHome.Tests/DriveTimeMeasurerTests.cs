using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers measuring a property's drives, including what happens when one of them cannot be.</summary>
public class DriveTimeMeasurerTests
{
    private const string OnePlace = """
    { "features": [ { "geometry": { "coordinates": [-122.33, 47.61] }, "properties": { "label": "Somewhere" } } ] }
    """;

    private const string NoPlace = """{ "features": [] }""";

    private const string OneRoute = """
    { "routes": [ { "summary": { "distance": 12400.0, "duration": 1110.0 } } ] }
    """;

    [Fact]
    public async Task A_measurement_places_both_ends_and_records_the_drive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OnePlace).Reply(OnePlace).Reply(OneRoute);
        var (measurer, properties, search) = Services(db, http);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        var destination = await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(1, outcome!.Measured);
        Assert.Empty(outcome.Problems);

        var drive = (await properties.GetAsync(property.Id))!.DriveTimes.Single();
        Assert.Equal(18.5d, drive.Minutes);
        Assert.Equal(7.705d, drive.Miles!.Value, 3);
        Assert.Equal(DriveTimeSource.Measured, drive.Source);

        Assert.Equal(47.61d, (await properties.GetAsync(property.Id))!.Latitude);
        Assert.Equal(47.61d, (await search.GetDestinationAsync(destination.Id))!.Latitude);
    }

    [Fact]
    public async Task Somewhere_already_placed_is_not_looked_up_again()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OneRoute);
        var (measurer, properties, search) = Services(db, http);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        await properties.UpdateAsync(property.Id, new UpdatePropertyRequest(Latitude: 47.6d, Longitude: -122.3d));

        var destination = await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));
        await search.PlaceDestinationAsync(destination.Id, 47.7d, -122.4d);

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(1, outcome!.Measured);
        Assert.Single(http.Requests);
        Assert.Contains("directions", http.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task With_no_destinations_there_is_nothing_to_measure()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OneRoute);
        var (measurer, properties, _) = Services(db, http);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(0, outcome!.Measured);
        Assert.Contains("no destinations", outcome.Problems.Single());
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_property_with_no_address_stops_before_the_destinations_are_touched()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OnePlace);
        var (measurer, properties, search) = Services(db, http);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(0, outcome!.Measured);
        Assert.Contains("no address", outcome.Problems.Single());
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_address_the_geocoder_does_not_know_is_reported_and_the_rest_carry_on()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OnePlace).Reply(NoPlace).Reply(OnePlace).Reply(OneRoute);
        var (measurer, properties, search) = Services(db, http);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Nowhere", "Not a real place"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(1, outcome!.Measured);
        Assert.Contains("did not recognise Nowhere", outcome.Problems.Single());
    }

    [Fact]
    public async Task A_destination_with_no_address_is_reported_without_stopping_the_others()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OnePlace).Reply(OnePlace).Reply(OneRoute);
        var (measurer, properties, search) = Services(db, http);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Unplaced"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(1, outcome!.Measured);
        Assert.Contains("Unplaced has no address", outcome.Problems.Single());
    }

    [Fact]
    public async Task A_pair_of_points_with_no_road_between_them_is_reported_as_such()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply(OnePlace).Reply(OnePlace).Reply("""{ "routes": [] }""");
        var (measurer, properties, search) = Services(db, http);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(0, outcome!.Measured);
        Assert.Contains("no road route", outcome.Problems.Single());
    }

    [Fact]
    public async Task A_service_that_refuses_the_route_is_reported_against_that_destination()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp()
            .Reply(OnePlace)
            .Reply(OnePlace)
            .Reply("""{ "error": "quota exceeded" }""", System.Net.HttpStatusCode.TooManyRequests);

        var (measurer, properties, search) = Services(db, http);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        var outcome = await measurer.MeasureAsync(property.Id);

        Assert.Equal(0, outcome!.Measured);
        Assert.Contains("Work: ", outcome.Problems.Single());
        Assert.Contains("429", outcome.Problems.Single());
    }

    [Fact]
    public async Task With_no_routing_service_the_whole_measurement_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var search = new HomeSearchService(db.Context, db.Clock);
        var properties = new PropertyService(db.Context, db.Clock);
        var routing = new RoutingClient(new FakeHttp().Reply(OnePlace).Client(), search);
        var measurer = new DriveTimeMeasurer(properties, search, routing);

        var property = await properties.CreateAsync(new CreatePropertyRequest("Candidate", Address: "1221 1st Ave"));
        await search.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        await Assert.ThrowsAsync<RoutingException>(() => measurer.MeasureAsync(property.Id));
    }

    [Fact]
    public async Task Measuring_a_property_that_is_not_there_gives_nothing_back()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (measurer, _, _) = Services(db, new FakeHttp().Reply(OnePlace));

        Assert.Null(await measurer.MeasureAsync(404));
    }

    private static (DriveTimeMeasurer Measurer, PropertyService Properties, HomeSearchService Search) Services(
        TestDatabase db,
        FakeHttp http)
    {
        var search = new HomeSearchService(db.Context, db.Clock);
        search.SaveSettingsAsync(new HomeSearchSettingsRequest(
            RoutingBaseUrl: "https://api.openrouteservice.org", RoutingApiKey: "a-key")).GetAwaiter().GetResult();

        var properties = new PropertyService(db.Context, db.Clock);
        var routing = new RoutingClient(http.Client(), search);

        return (new DriveTimeMeasurer(properties, search, routing), properties, search);
    }
}
