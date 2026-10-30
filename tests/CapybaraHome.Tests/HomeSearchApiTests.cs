using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the REST surface the connector and the UI share for the home search.</summary>
public class HomeSearchApiTests
{
    // The API writes the enumerations as names, so a client reading them back needs the converter.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_property_is_created_read_changed_and_deleted()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var created = await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest(
            "Harbor Steps",
            Address: "1221 1st Ave",
            Rent: 2380m,
            Utilities: [new PropertyUtilityInput(UtilityKind.Electricity, 70m)],
            Amenities: ["Gym"]), Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var property = (await created.Content.ReadFromJsonAsync<PropertyView>(Json))!;
        Assert.Equal(2450m, property.MonthlyTotal);

        var scored = await client.GetFromJsonAsync<ScoredProperty>($"/api/properties/{property.Id}", Json);
        Assert.Equal("Harbor Steps", scored!.Property.Name);
        Assert.Equal(0m, scored.Score.Total);

        var patched = await client.PatchAsJsonAsync($"/api/properties/{property.Id}",
            new UpdatePropertyRequest(Name: "Harbor Steps 1408"), Json);
        Assert.Equal("Harbor Steps 1408", (await patched.Content.ReadFromJsonAsync<PropertyView>(Json))!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/properties/{property.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/properties/{property.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_board_names_the_current_home_and_ranks_the_candidates()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        await client.PutAsJsonAsync("/api/home-search/settings",
            new HomeSearchSettingsRequest(MonthlyFeePointsPerDollar: 0.1m), Json);

        await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest("Now", IsCurrent: true, Rent: 2000m), Json);
        await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest("Dearer", Rent: 2400m), Json);
        await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest("Cheaper", Rent: 1700m), Json);

        var board = await client.GetFromJsonAsync<HomeSearchBoard>("/api/properties", Json);

        Assert.Equal("Now", board!.Current!.Name);
        Assert.Equal(["Cheaper", "Dearer"], board.Candidates.Select(c => c.Property.Name));
        Assert.Equal(30m, board.Candidates[0].Score.Total);
        Assert.Equal(0m, board.CurrentScore!.Total);
    }

    [Fact]
    public async Task A_blank_name_is_refused_with_the_reason()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var refused = await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest("  "), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var created = await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest("Named"), Json);
        var property = (await created.Content.ReadFromJsonAsync<PropertyView>(Json))!;

        var patched = await client.PatchAsJsonAsync($"/api/properties/{property.Id}",
            new UpdatePropertyRequest(Name: " "), Json);
        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
    }

    [Fact]
    public async Task Reading_or_changing_something_that_is_not_there_answers_not_found()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/properties/404")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PatchAsJsonAsync("/api/properties/404", new UpdatePropertyRequest(Name: "No"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/properties/404/notes", new PropertyNoteRequest("No"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/properties/notes/404")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/properties/404/drives/measure", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/home-search/destinations/404")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/home-search/priorities/404")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/api/home-search/destinations/404", new SearchDestinationRequest("No"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/api/home-search/priorities/404", new SearchPriorityRequest("No"), Json)).StatusCode);
    }

    [Fact]
    public async Task Notes_are_written_and_deleted_over_the_api()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        var property = await PropertyAsync(client, "Noted");

        var written = await client.PostAsJsonAsync($"/api/properties/{property.Id}/notes",
            new PropertyNoteRequest("Toured it"), Json);

        Assert.Equal(HttpStatusCode.Created, written.StatusCode);
        var note = (await written.Content.ReadFromJsonAsync<PropertyNoteView>(Json))!;

        var blank = await client.PostAsJsonAsync($"/api/properties/{property.Id}/notes",
            new PropertyNoteRequest("   "), Json);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/properties/notes/{note.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_marking_and_a_drive_time_are_set_and_cleared_over_the_api()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var property = await PropertyAsync(client, "Marked");

        var destination = (await (await client.PostAsJsonAsync("/api/home-search/destinations",
            new SearchDestinationRequest("Work", "400 Broad St", 3m), Json))
            .Content.ReadFromJsonAsync<SearchDestinationView>(Json))!;

        var priority = (await (await client.PostAsJsonAsync("/api/home-search/priorities",
            new SearchPriorityRequest("Laundry", 10m), Json))
            .Content.ReadFromJsonAsync<SearchPriorityView>(Json))!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/properties/{property.Id}/priorities/{priority.Id}", new PropertyPriorityRequest(true), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/properties/{property.Id}/drives/{destination.Id}", new PropertyDriveTimeRequest(18.5d, 12.4d), Json)).StatusCode);

        var scored = await client.GetFromJsonAsync<ScoredProperty>($"/api/properties/{property.Id}", Json);
        Assert.Equal(18.5d, scored!.Property.DriveTimes.Single().Minutes);
        Assert.Equal(12.4d, scored.Property.DriveTimes.Single().Miles);
        Assert.True(scored.Property.Priorities.Single().Meets);
        Assert.Equal(10m, scored.Score.Priorities);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/properties/{property.Id}/drives/{destination.Id}", new PropertyDriveTimeRequest(null), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/properties/{property.Id}/drives/{destination.Id}", new PropertyDriveTimeRequest(null), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/properties/404/priorities/{priority.Id}", new PropertyPriorityRequest(true), Json)).StatusCode);
    }

    [Fact]
    public async Task Cars_are_listed_changed_and_deleted_over_the_api()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var vehicle = (await (await client.PostAsJsonAsync("/api/home-search/vehicles",
            new VehicleRequest("The hatchback", 34.5d, 4.19m), Json)).Content.ReadFromJsonAsync<VehicleView>(Json))!;

        Assert.Equal(34.5d, vehicle.MilesPerGallon);
        Assert.Single((await client.GetFromJsonAsync<List<VehicleView>>("/api/home-search/vehicles", Json))!);

        await client.PutAsJsonAsync($"/api/home-search/vehicles/{vehicle.Id}",
            new VehicleRequest("The wagon", 30d, 4.05m), Json);

        var held = await client.GetFromJsonAsync<List<VehicleView>>("/api/home-search/vehicles", Json);
        Assert.Equal("The wagon", held!.Single().Name);
        Assert.Equal(4.05m, held!.Single().FuelPricePerGallon);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/home-search/vehicles",
            new VehicleRequest("  "), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            $"/api/home-search/vehicles/{vehicle.Id}", new VehicleRequest(""), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/home-search/vehicles/{vehicle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/home-search/vehicles/{vehicle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/home-search/vehicles/{vehicle.Id}", new VehicleRequest("Gone"), Json)).StatusCode);
    }

    [Fact]
    public async Task Destinations_and_priorities_are_listed_changed_and_deleted_over_the_api()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var destination = (await (await client.PostAsJsonAsync("/api/home-search/destinations",
            new SearchDestinationRequest("Work"), Json)).Content.ReadFromJsonAsync<SearchDestinationView>(Json))!;

        var priority = (await (await client.PostAsJsonAsync("/api/home-search/priorities",
            new SearchPriorityRequest("Laundry", 10m), Json)).Content.ReadFromJsonAsync<SearchPriorityView>(Json))!;

        Assert.Single((await client.GetFromJsonAsync<List<SearchDestinationView>>("/api/home-search/destinations", Json))!);
        Assert.Single((await client.GetFromJsonAsync<List<SearchPriorityView>>("/api/home-search/priorities", Json))!);

        await client.PutAsJsonAsync($"/api/home-search/destinations/{destination.Id}",
            new SearchDestinationRequest("The office", null, 2m, 3), Json);
        await client.PutAsJsonAsync($"/api/home-search/priorities/{priority.Id}",
            new SearchPriorityRequest("In-unit laundry", 12m), Json);

        var destinations = await client.GetFromJsonAsync<List<SearchDestinationView>>("/api/home-search/destinations", Json);
        Assert.Equal("The office", destinations!.Single().Name);
        Assert.Equal(3, destinations!.Single().RoundTripsPerWeek);

        var priorities = await client.GetFromJsonAsync<List<SearchPriorityView>>("/api/home-search/priorities", Json);
        Assert.Equal(12m, priorities!.Single().Points);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/home-search/destinations",
            new SearchDestinationRequest("  "), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/home-search/priorities",
            new SearchPriorityRequest(" "), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            $"/api/home-search/destinations/{destination.Id}", new SearchDestinationRequest(""), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            $"/api/home-search/priorities/{priority.Id}", new SearchPriorityRequest(""), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/home-search/destinations/{destination.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/home-search/priorities/{priority.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_settings_answer_whether_a_key_is_held_without_handing_it_back()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var saved = await client.PutAsJsonAsync("/api/home-search/settings", new HomeSearchSettingsRequest(
            RoutingBaseUrl: "https://api.openrouteservice.org", RoutingApiKey: "a-secret-key"), Json);

        var body = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain("a-secret-key", body);

        var settings = await client.GetFromJsonAsync<HomeSearchSettingsView>("/api/home-search/settings", Json);
        Assert.True(settings!.HasRoutingApiKey);
        Assert.True(settings.CanMeasureDriveTimes);
        Assert.DoesNotContain("a-secret-key", await (await client.GetAsync("/api/home-search/settings")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Measuring_with_no_routing_service_is_refused_with_the_reason()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        var created = await client.PostAsJsonAsync("/api/properties",
            new CreatePropertyRequest("Driven", Address: "1221 1st Ave"), Json);
        var property = (await created.Content.ReadFromJsonAsync<PropertyView>(Json))!;

        // With nothing to drive to there is no call to make, so that is reported rather than refused.
        var nowhere = await client.PostAsync($"/api/properties/{property.Id}/drives/measure", null);
        Assert.Equal(HttpStatusCode.OK, nowhere.StatusCode);
        Assert.Contains("no destinations", await nowhere.Content.ReadAsStringAsync());

        await client.PostAsJsonAsync("/api/home-search/destinations", new SearchDestinationRequest("Work"), Json);

        var refused = await client.PostAsync($"/api/properties/{property.Id}/drives/measure", null);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("No routing service", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_picture_cannot_be_read_without_signing_in()
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: false);
        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var served = await client.GetAsync("/homes/photos/1");

        Assert.NotEqual(HttpStatusCode.OK, served.StatusCode);
    }

    private static async Task<PropertyView> PropertyAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest(name), Json);
        return (await created.Content.ReadFromJsonAsync<PropertyView>(Json))!;
    }
}
