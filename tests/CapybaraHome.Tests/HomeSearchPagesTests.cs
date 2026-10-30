using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;
using CapybaraHome.Web;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Tests;

/// <summary>
/// Drives the home search pages the way a browser with no JavaScript does. Every control the
/// pages offer is pressed here, because a control that only works in one of its states is the
/// failure this suite exists to stop.
/// </summary>
public class HomeSearchPagesTests
{
    [Fact]
    public async Task The_editor_saves_a_property_with_all_four_of_its_lists()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var saved = await PostManyAsync(client, "/homes/new", "property-editor",
        [
            new("name", "  Harbor Steps  "),
            new("address", "1221 1st Ave"),
            new("rent", "2380"),
            new("parking", "0"),
            new("pet-rent", "25"),
            new("deposit", "300"),
            new("internet", "Fibre included"),
            new(PropertyRows.UtilityKindField, "Electricity"),
            new(PropertyRows.UtilityLabelField, string.Empty),
            new(PropertyRows.UtilityCostField, "70"),
            new("amenity", "Gym"),
            new("amenity", "Pool"),
            new("pro", "Walk to the water"),
            new("con", "Small kitchen"),
            new("action", "save"),
        ]);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var property = await OneAsync(app);
        Assert.Equal("Harbor Steps", property.Name);
        Assert.Equal(2475m, property.MonthlyTotal);
        Assert.Equal(["Gym", "Pool"], property.Amenities.Select(a => a.Name));
        Assert.Equal("Walk to the water", property.Pros.Single().Text);
        Assert.Equal("Small kitchen", property.Cons.Single().Text);
        Assert.False(property.IsCurrent);

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("Harbor Steps", page);
        Assert.Contains("$2,475", page);
    }

    [Theory]
    [InlineData("utility")]
    [InlineData("amenity")]
    [InlineData("pro")]
    [InlineData("con")]
    public async Task Every_list_on_the_editor_can_be_grown_and_cut_back(string list)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var grown = await PostManyAsync(client, "/homes/new", "property-editor",
            [new("name", "Growing"), new("action", PropertyRows.AddAction(list))]);

        var html = await grown.Content.ReadAsStringAsync();
        var field = list == "utility" ? PropertyRows.UtilityCostField : list;
        Assert.Equal(1, Inputs(html, field));

        var again = await PostManyAsync(client, "/homes/new", "property-editor",
        [
            new("name", "Growing"),
            .. Lines(list, ["First", "Second"]),
            new("action", PropertyRows.AddAction(list)),
        ]);

        Assert.Equal(3, Inputs(await again.Content.ReadAsStringAsync(), field));

        var cut = await PostManyAsync(client, "/homes/new", "property-editor",
        [
            new("name", "Growing"),
            .. Lines(list, ["First", "Second"]),
            new("action", PropertyRows.RemoveAction(list, 0)),
        ]);

        var left = await cut.Content.ReadAsStringAsync();
        Assert.Equal(1, Inputs(left, field));
        Assert.DoesNotContain("value=\"First\"", left);
        Assert.Contains(list == "utility" ? "value=\"Second\"" : "value=\"Second\"", left);
    }

    [Theory]
    [MemberData(nameof(UtilityKinds))]
    public async Task Every_kind_of_utility_can_be_chosen_and_comes_back_on_the_page(UtilityKind kind)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostManyAsync(client, "/homes/new", "property-editor",
        [
            new("name", "Billed"),
            new(PropertyRows.UtilityKindField, kind.ToString()),
            new(PropertyRows.UtilityLabelField, kind == UtilityKind.Other ? "Valet trash" : string.Empty),
            new(PropertyRows.UtilityCostField, "42"),
            new("action", "save"),
        ]);

        var property = await OneAsync(app);
        Assert.Equal(kind, property.Utilities.Single().Kind);

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains(UtilityKindNames.Label(kind, property.Utilities.Single().Label), page);
    }

    /// <summary>Every utility kind the picker offers, so a kind added later cannot ship untested.</summary>
    public static TheoryData<UtilityKind> UtilityKinds()
    {
        var data = new TheoryData<UtilityKind>();
        foreach (var kind in UtilityKindNames.All)
        {
            data.Add(kind);
        }

        return data;
    }

    [Fact]
    public async Task A_blank_name_comes_back_as_an_error_on_the_editor_rather_than_a_blank_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var refused = await PostManyAsync(client, "/homes/new", "property-editor",
            [new("name", "   "), new("action", "save")]);

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("A property needs a name.", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Marking_a_property_as_the_current_home_moves_it_off_the_one_that_held_it()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var first = await CreateAsync(app, new CreatePropertyRequest("First", IsCurrent: true));
        var second = await CreateAsync(app, new CreatePropertyRequest("Second"));

        await PostManyAsync(client, $"/homes/{second.Id}/edit", "property-editor",
            [new("name", "Second"), new("is-current", "true"), new("action", "save")]);

        using var scope = app.Services.CreateScope();
        var properties = scope.ServiceProvider.GetRequiredService<PropertyService>();

        Assert.Equal(second.Id, (await properties.GetCurrentAsync())!.Id);
        Assert.False((await properties.GetAsync(first.Id))!.IsCurrent);

        var list = await client.GetStringAsync("/homes");
        Assert.Contains("Where I live now", list);
        Assert.Contains("Second", list);
    }

    [Theory]
    [InlineData("unmarked", null)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public async Task Every_priority_marking_can_be_posted_from_the_property_page(string posted, bool? expected)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Marked"));
        var priority = await PriorityAsync(app, "In-unit laundry", 10m);

        var saved = await PostAsync(client, $"/homes/{property.Id}", "property-priorities",
            new() { [$"priority-{priority.Id}"] = posted });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var held = (await ReadAsync(app, property.Id)).Priorities;
        Assert.Equal(expected, held.SingleOrDefault()?.Meets);

        // Razor writes a plus sign as an entity, so the page is decoded before the points are read.
        var html = WebUtility.HtmlDecode(await saved.Content.ReadAsStringAsync());
        Assert.Contains($"value=\"{posted}\" selected", html);
        Assert.Contains("+10", html);
        Assert.Contains(expected == true ? "1 of 1" : "0 of 1", html);
    }

    [Fact]
    public async Task A_drive_time_is_typed_in_and_blanking_the_box_forgets_it()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var destination = await DestinationAsync(app, "Work", weight: 3m);

        var typed = await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{destination.Id}"] = "18.5", [$"miles-{destination.Id}"] = "12.4" });

        Assert.Contains("typed in", await typed.Content.ReadAsStringAsync());
        Assert.Equal(18.5d, (await ReadAsync(app, property.Id)).DriveTimes.Single().Minutes);
        Assert.Equal(12.4d, (await ReadAsync(app, property.Id)).DriveTimes.Single().Miles);

        await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{destination.Id}"] = string.Empty });

        Assert.Empty((await ReadAsync(app, property.Id)).DriveTimes);
    }

    [Fact]
    public async Task With_no_routing_service_the_measure_button_is_off_and_says_why()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven", Address: "1221 1st Ave"));
        await DestinationAsync(app, "Work");

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("Measuring needs a routing service", page);
        Assert.Matches("Measure them from the address[\\s\\S]{0,80}", page);

        // Pressing it anyway, as a client that ignores the disabled attribute can, is reported.
        var pressed = await PostAsync(client, $"/homes/{property.Id}", "property-measure", []);
        Assert.Equal(HttpStatusCode.OK, pressed.StatusCode);
        Assert.Contains("No routing service is set up yet", await pressed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_note_is_written_and_can_be_taken_back_off()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Noted"));

        var written = await PostAsync(client, $"/homes/{property.Id}", "property-note",
            new() { ["body"] = "Toured it on the 29th." });

        Assert.Contains("Toured it on the 29th.", await written.Content.ReadAsStringAsync());

        var blank = await PostAsync(client, $"/homes/{property.Id}", "property-note", new() { ["body"] = "   " });
        Assert.Contains("A note needs something in it.", await blank.Content.ReadAsStringAsync());

        var noteId = (await ReadAsync(app, property.Id)).Notes.Single().Id;
        var deleted = await PostAsync(client, $"/homes/{property.Id}", $"note-delete-{noteId}", []);

        Assert.Contains("No notes yet.", await deleted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_picture_is_uploaded_served_back_and_deleted()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Pictured"));

        var uploaded = await UploadAsync(client, property.Id, "kitchen.png", "image/png", "a picture");

        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var page = await uploaded.Content.ReadAsStringAsync();
        Assert.Contains("Added kitchen.png.", page);
        Assert.Contains("The kitchen", page);

        var photoId = (await ReadAsync(app, property.Id)).Photos.Single().Id;
        var served = await client.GetAsync($"/homes/photos/{photoId}");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal("a picture", await served.Content.ReadAsStringAsync());

        var deleted = await PostAsync(client, $"/homes/{property.Id}", $"photo-delete-{photoId}", []);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Empty((await ReadAsync(app, property.Id)).Photos);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/homes/photos/{photoId}")).StatusCode);
    }

    [Fact]
    public async Task Something_that_is_not_a_picture_comes_back_to_the_page_with_the_reason()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Pictured"));

        var refused = await UploadAsync(client, property.Id, "notes.pdf", "application/pdf", "not a picture");

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("is not a JPEG, PNG, GIF or WebP picture", await refused.Content.ReadAsStringAsync());
        Assert.Empty((await ReadAsync(app, property.Id)).Photos);
    }

    [Fact]
    public async Task The_weights_are_saved_from_the_settings_page_and_the_warning_goes_away()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        Assert.Contains("No scoring weights are set yet", await client.GetStringAsync("/homes"));

        var saved = await PostAsync(client, "/homes/settings", "home-weights", new()
        {
            ["fees"] = "0.05",
            ["deposit"] = "0.01",
            ["drive"] = "1.5",
            ["amenity"] = "3",
            ["pro"] = "2",
            ["con"] = "4",
        });

        Assert.Contains("Saved the weights.", await saved.Content.ReadAsStringAsync());
        Assert.DoesNotContain("No scoring weights are set yet", await client.GetStringAsync("/homes"));

        using var scope = app.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<HomeSearchService>().GetSettingsAsync();
        Assert.Equal(0.05m, settings.MonthlyFeePointsPerDollar);
        Assert.Equal(4m, settings.ConPoints);
    }

    [Fact]
    public async Task The_routing_service_is_saved_and_its_key_can_be_forgotten()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var saved = await PostAsync(client, "/homes/settings", "home-routing", new()
        {
            ["action"] = "save",
            ["url"] = "https://api.openrouteservice.org",
            ["key"] = "a-secret-key",
        });

        var page = await saved.Content.ReadAsStringAsync();
        Assert.Contains("A key is held", page);

        // The key must never be rendered back into the page it was typed on.
        Assert.DoesNotContain("a-secret-key", page);

        var forgotten = await PostAsync(client, "/homes/settings", "home-routing", new() { ["action"] = "forget" });
        Assert.Contains("Needed by the hosted service", await forgotten.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_destination_is_added_changed_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var added = await PostAsync(client, "/homes/settings", "destination-new",
            new() { ["name"] = "Work", ["address"] = "400 Broad St", ["weight"] = "3" });

        Assert.Contains("Work", await added.Content.ReadAsStringAsync());

        var id = (await DestinationsAsync(app)).Single().Id;

        var changed = await PostAsync(client, "/homes/settings", $"destination-{id}",
            new() { ["action"] = "save", ["name"] = "The office", ["address"] = "400 Broad St", ["weight"] = "2" });

        Assert.Contains("The office", await changed.Content.ReadAsStringAsync());
        Assert.Equal(2m, (await DestinationsAsync(app)).Single().Weight);

        var blank = await PostAsync(client, "/homes/settings", $"destination-{id}",
            new() { ["action"] = "save", ["name"] = "  ", ["weight"] = "1" });

        Assert.Contains("A destination needs a name.", await blank.Content.ReadAsStringAsync());

        var deleted = await PostAsync(client, "/homes/settings", $"destination-{id}", new() { ["action"] = "delete" });
        Assert.Contains("No destinations yet.", await deleted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_priority_is_added_changed_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var added = await PostAsync(client, "/homes/settings", "priority-new",
            new() { ["name"] = "In-unit laundry", ["points"] = "10" });

        Assert.Contains("In-unit laundry", await added.Content.ReadAsStringAsync());

        var id = (await PrioritiesAsync(app)).Single().Id;

        var changed = await PostAsync(client, "/homes/settings", $"priority-{id}",
            new() { ["action"] = "save", ["name"] = "Laundry in the unit", ["points"] = "-5" });

        Assert.Contains("Laundry in the unit", await changed.Content.ReadAsStringAsync());
        Assert.Equal(-5m, (await PrioritiesAsync(app)).Single().Points);

        var blank = await PostAsync(client, "/homes/settings", $"priority-{id}",
            new() { ["action"] = "save", ["name"] = "", ["points"] = "1" });

        Assert.Contains("A priority needs a name.", await blank.Content.ReadAsStringAsync());

        var deleted = await PostAsync(client, "/homes/settings", $"priority-{id}", new() { ["action"] = "delete" });
        Assert.Contains("No priorities yet.", await deleted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_blank_destination_or_priority_name_is_refused_when_one_is_being_added()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var destination = await PostAsync(client, "/homes/settings", "destination-new", new() { ["name"] = " " });
        Assert.Contains("A destination needs a name.", await destination.Content.ReadAsStringAsync());

        var priority = await PostAsync(client, "/homes/settings", "priority-new", new() { ["name"] = " " });
        Assert.Contains("A priority needs a name.", await priority.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_list_puts_the_best_scoring_candidate_first()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/homes/settings", "home-weights", new()
        {
            ["fees"] = "0.1", ["deposit"] = "0", ["drive"] = "0", ["amenity"] = "0", ["pro"] = "0", ["con"] = "0",
        });

        await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true, Rent: 2000m));
        await CreateAsync(app, new CreatePropertyRequest("Dearer", Rent: 2400m));
        await CreateAsync(app, new CreatePropertyRequest("Cheaper", Rent: 1700m));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/homes"));

        Assert.True(html.IndexOf("Cheaper", StringComparison.Ordinal) < html.IndexOf("Dearer", StringComparison.Ordinal));
        Assert.Contains("+30", html);
        Assert.Contains("-40", html);
    }

    [Fact]
    public async Task With_nothing_in_it_the_search_says_what_to_do_rather_than_showing_an_empty_table()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var html = await client.GetStringAsync("/homes");

        Assert.Contains("Nothing is marked as your current home", html);
        Assert.Contains("No candidates yet.", html);

        var property = await CreateAsync(app, new CreatePropertyRequest("Alone"));
        var page = await client.GetStringAsync($"/homes/{property.Id}");

        Assert.Contains("No priorities yet.", page);
        Assert.Contains("No destinations yet.", page);
        Assert.Contains("No pictures yet.", page);
        Assert.Contains("No notes yet.", page);
        Assert.Contains("No home is marked as the one you live in", page);
    }

    [Fact]
    public async Task The_current_home_says_it_is_the_baseline_rather_than_scoring_against_itself()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true, Rent: 2000m));

        var page = await client.GetStringAsync($"/homes/{property.Id}");

        Assert.Contains("Where I live now", page);
        Assert.Contains("This is the baseline", page);
    }

    [Fact]
    public async Task A_property_is_deleted_from_its_editor()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Going"));

        var deleted = await PostAsync(client, $"/homes/{property.Id}/edit", "property-delete", []);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Contains("No candidates yet.", await client.GetStringAsync("/homes"));
    }

    [Fact]
    public async Task A_property_that_is_not_there_says_so_rather_than_failing()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var page = await client.GetAsync("/homes/404");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("That property is not here.", await page.Content.ReadAsStringAsync());

        // The editor has nothing to edit, so it sends the browser back to the list.
        var editor = await client.GetAsync("/homes/404/edit");
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
    }

    [Fact]
    public async Task The_list_shows_a_drive_for_every_destination_and_a_dash_where_there_is_none()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var work = await DestinationAsync(app, "Work", weight: 3m);
        await DestinationAsync(app, "Climbing gym");

        await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true, Rent: 2000m));
        var candidate = await CreateAsync(app, new CreatePropertyRequest("Candidate", Rent: 1900m));

        await PostAsync(client, $"/homes/{candidate.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "12" });

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/homes"));

        Assert.Contains("Climbing gym", html);
        Assert.Contains("12 min", html);
        Assert.Contains("—", html);
    }

    [Fact]
    public async Task Measuring_from_the_property_page_records_the_drives_and_says_how_many()
    {
        const string Place = """
        { "features": [ { "geometry": { "coordinates": [-122.33, 47.61] }, "properties": { "label": "Somewhere" } } ] }
        """;

        const string Route = """
        { "routes": [ { "summary": { "distance": 12400.0, "duration": 1110.0 } } ] }
        """;

        var routing = new FakeHttp().Reply(Place).Reply(Place).Reply(Route);

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.AddHttpClient<RoutingClient>().ConfigurePrimaryHttpMessageHandler(routing.Handler));

        var client = Client(app);

        await PostAsync(client, "/homes/settings", "home-routing", new()
        {
            ["action"] = "save",
            ["url"] = "https://api.openrouteservice.org",
            ["key"] = "a-key",
        });

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven", Address: "1221 1st Ave"));
        await DestinationAsync(app, "Work", address: "400 Broad St");

        var measured = await PostAsync(client, $"/homes/{property.Id}", "property-measure", []);

        var html = await measured.Content.ReadAsStringAsync();
        Assert.Contains("Measured 1 drive(s).", html);
        Assert.Contains("measured", html);
        Assert.Equal(18.5d, (await ReadAsync(app, property.Id)).DriveTimes.Single().Minutes);
    }

    private static List<KeyValuePair<string, string>> Lines(string list, IEnumerable<string> values)
    {
        var fields = new List<KeyValuePair<string, string>>();

        foreach (var value in values)
        {
            if (list == "utility")
            {
                fields.Add(new(PropertyRows.UtilityKindField, "Electricity"));
                fields.Add(new(PropertyRows.UtilityLabelField, string.Empty));
                fields.Add(new(PropertyRows.UtilityCostField, value));
            }
            else
            {
                fields.Add(new(list, value));
            }
        }

        return fields;
    }

    private static async Task<PropertyView> CreateAsync(AppFactory app, CreatePropertyRequest request)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PropertyService>().CreateAsync(request);
    }

    private static async Task<PropertyView> ReadAsync(AppFactory app, int id)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<PropertyService>().GetAsync(id))!;
    }

    [Fact]
    public async Task A_car_is_added_changed_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var added = await PostAsync(client, "/homes/settings", "vehicle-new",
            new() { ["name"] = "The hatchback", ["mpg"] = "34.5", ["price"] = "4.19" });

        Assert.Contains("The hatchback", await added.Content.ReadAsStringAsync());

        var id = (await VehiclesAsync(app)).Single().Id;

        var changed = await PostAsync(client, "/homes/settings", $"vehicle-{id}",
            new() { ["action"] = "save", ["name"] = "The wagon", ["mpg"] = "30", ["price"] = "4.05" });

        Assert.Contains("The wagon", await changed.Content.ReadAsStringAsync());
        Assert.Equal(30d, (await VehiclesAsync(app)).Single().MilesPerGallon);
        Assert.Equal(4.05m, (await VehiclesAsync(app)).Single().FuelPricePerGallon);

        var blank = await PostAsync(client, "/homes/settings", $"vehicle-{id}",
            new() { ["action"] = "save", ["name"] = "  ", ["mpg"] = "30", ["price"] = "4" });

        Assert.Contains("A car needs a name.", await blank.Content.ReadAsStringAsync());

        var deleted = await PostAsync(client, "/homes/settings", $"vehicle-{id}", new() { ["action"] = "delete" });
        Assert.Contains("No cars yet.", await deleted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_blank_car_name_is_refused_when_one_is_being_added()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var vehicle = await PostAsync(client, "/homes/settings", "vehicle-new", new() { ["name"] = " " });
        Assert.Contains("A car needs a name.", await vehicle.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_car_with_no_mileage_or_price_says_it_cannot_be_costed()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/homes/settings", "vehicle-new",
            new() { ["name"] = "Unknown", ["mpg"] = string.Empty, ["price"] = string.Empty });

        var page = await client.GetStringAsync("/homes/settings");
        Assert.Contains("Needs both a mileage and a fuel price", page);
    }

    [Fact]
    public async Task The_round_trips_a_week_are_saved_with_the_destination()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/homes/settings", "destination-new",
            new() { ["name"] = "Work", ["weight"] = "3", ["trips"] = "3" });

        Assert.Equal(3, (await DestinationsAsync(app)).Single().RoundTripsPerWeek);

        var id = (await DestinationsAsync(app)).Single().Id;

        await PostAsync(client, "/homes/settings", $"destination-{id}",
            new() { ["action"] = "save", ["name"] = "Work", ["weight"] = "3", ["trips"] = "2" });

        Assert.Equal(2, (await DestinationsAsync(app)).Single().RoundTripsPerWeek);
    }

    [Fact]
    public async Task The_fuel_card_costs_a_three_day_work_week_in_each_car()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3);
        await VehicleAsync(app, "The hatchback", 30d, 4.50m);

        await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "20", [$"miles-{work.Id}"] = "10" });

        var page = await client.GetStringAsync($"/homes/{property.Id}");

        // Six one-way drives of ten miles, two gallons at $4.50, and the same week over a month.
        Assert.Contains("60 mi", page);
        Assert.Contains("$9.00", page);
        Assert.Contains("$39.00", page);
        Assert.Contains("The hatchback", page);
    }

    [Fact]
    public async Task The_driving_card_says_what_is_missing_rather_than_costing_nothing_quietly()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));

        var noTrips = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("Nothing to cost yet.", noTrips);

        var work = await DestinationAsync(app, "Work", trips: 3);
        await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "20" });

        var noMiles = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("The drive to Work has no distance yet", noMiles);

        await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "20", [$"miles-{work.Id}"] = "10" });

        var noCars = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("No cars yet.", noCars);

        await VehicleAsync(app, "Unpriced", 30d, 0m);
        var noPrice = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("None of the cars has both a mileage and a fuel price yet", noPrice);
    }

    [Fact]
    public async Task A_toll_road_is_added_changed_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var added = await PostAsync(client, "/homes/settings", "toll-new",
            new() { ["road"] = "520", ["price"] = "4.50" });

        Assert.Contains("value=\"520\"", await added.Content.ReadAsStringAsync());

        var id = (await TollRoadsAsync(app)).Single().Id;

        await PostAsync(client, "/homes/settings", $"toll-{id}",
            new() { ["action"] = "save", ["road"] = "SR 520", ["price"] = "4.80" });

        var changed = (await TollRoadsAsync(app)).Single();
        Assert.Equal("SR 520", changed.Road);
        Assert.Equal(4.80m, changed.PeakToll);

        var blank = await PostAsync(client, "/homes/settings", $"toll-{id}",
            new() { ["action"] = "save", ["road"] = " ", ["price"] = "1" });

        Assert.Contains("A toll road needs the name to look for.", await blank.Content.ReadAsStringAsync());

        var deleted = await PostAsync(client, "/homes/settings", $"toll-{id}", new() { ["action"] = "delete" });
        Assert.Contains("No toll roads yet.", await deleted.Content.ReadAsStringAsync());

        var refused = await PostAsync(client, "/homes/settings", "toll-new", new() { ["road"] = "" });
        Assert.Contains("A toll road needs the name to look for.", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_carpool_credit_is_saved_with_the_destination()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/homes/settings", "destination-new",
            new() { ["name"] = "Work", ["weight"] = "3", ["trips"] = "3", ["carpool"] = "5" });

        Assert.Equal(5m, (await DestinationsAsync(app)).Single().CarpoolCreditPerDay);

        var id = (await DestinationsAsync(app)).Single().Id;

        await PostAsync(client, "/homes/settings", $"destination-{id}",
            new() { ["action"] = "save", ["name"] = "Work", ["weight"] = "3", ["trips"] = "3", ["carpool"] = "4.50" });

        Assert.Equal(4.50m, (await DestinationsAsync(app)).Single().CarpoolCreditPerDay);
    }

    [Fact]
    public async Task A_toll_ticked_on_a_typed_in_drive_is_charged_each_way_and_the_credit_comes_off()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3, carpool: 5m);
        await VehicleAsync(app, "The hatchback", 30d, 4.50m);
        var toll = await TollRoadAsync(app, "520", 4.50m);
        await TollRoadAsync(app, "99", 2.25m);

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("Crosses 520", page);
        Assert.Contains("Crosses 99", page);

        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "20"),
            new($"miles-{work.Id}", "10"),
            new($"toll-{work.Id}", toll.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]);

        Assert.Equal(["520"], (await ReadAsync(app, property.Id)).DriveTimes.Single().TolledRoads);

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));

        // $9 of fuel and $27 of tolls, less three days of $5 credit.
        Assert.Contains("$27.00", html);
        Assert.Contains("\u2212$15.00", html);
        Assert.Contains("$21.00", html);
        Assert.Contains("$91.00", html);

        // Saving with the box unticked takes the toll back off.
        await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "20", [$"miles-{work.Id}"] = "10" });

        Assert.Empty((await ReadAsync(app, property.Id)).DriveTimes.Single().TolledRoads);
    }

    [Fact]
    public async Task A_measured_route_names_its_tolled_roads_and_flags_any_nothing_prices()
    {
        const string Place = """
        { "features": [ { "geometry": { "coordinates": [-122.33, 47.61] }, "properties": { "label": "Somewhere" } } ] }
        """;

        const string Route = """
        {
          "routes": [ {
            "summary": { "distance": 16093.44, "duration": 1200.0 },
            "segments": [ { "steps": [
              { "name": "1st Avenue", "way_points": [0, 4] },
              { "name": "SR 99 Tunnel", "way_points": [4, 9] },
              { "name": "Evergreen Point Floating Bridge", "way_points": [9, 14] },
              { "name": "-", "way_points": [14, 15] }
            ] } ],
            "extras": { "tollways": { "values": [ [0, 4, 0], [4, 14, 1], [14, 15, 0] ] } }
          } ]
        }
        """;

        var routing = new FakeHttp().Reply(Place).Reply(Place).Reply(Route);

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.AddHttpClient<RoutingClient>().ConfigurePrimaryHttpMessageHandler(routing.Handler));

        var client = Client(app);

        await PostAsync(client, "/homes/settings", "home-routing", new()
        {
            ["action"] = "save",
            ["url"] = "https://api.openrouteservice.org",
            ["key"] = "a-key",
        });

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven", Address: "1221 1st Ave"));
        await DestinationAsync(app, "Work", address: "400 Broad St", trips: 3);
        var toll = await TollRoadAsync(app, "99", 2.25m);

        await PostAsync(client, $"/homes/{property.Id}", "property-measure", []);

        var drive = (await ReadAsync(app, property.Id)).DriveTimes.Single();
        Assert.Equal(["SR 99 Tunnel", "Evergreen Point Floating Bridge"], drive.TolledRoads);
        Assert.Equal(10d, drive.Miles!.Value, 3);

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("Tolled roads on the route: SR 99 Tunnel, Evergreen Point Floating Bridge", html);
        Assert.Contains("uses a tolled road nothing prices yet:", html);
        Assert.Contains("Evergreen Point Floating Bridge.", html);
        Assert.Contains("$13.50", html);

        // Saving the times by hand keeps the unpriced road, which has no box to tick.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{drive.DestinationId}", "20"),
            new($"miles-{drive.DestinationId}", "10"),
            new($"toll-{drive.DestinationId}", toll.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]);

        Assert.Equal(
            ["Evergreen Point Floating Bridge", "99"],
            (await ReadAsync(app, property.Id)).DriveTimes.Single().TolledRoads);
    }

    [Fact]
    public async Task The_list_shows_the_monthly_driving_cost_beside_a_candidate_and_the_current_home()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var current = await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true));
        var candidate = await CreateAsync(app, new CreatePropertyRequest("There"));
        var work = await DestinationAsync(app, "Work", trips: 3);
        await VehicleAsync(app, "The hatchback", 30d, 4.50m);

        await PostAsync(client, $"/homes/{current.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "40", [$"miles-{work.Id}"] = "20" });
        await PostAsync(client, $"/homes/{candidate.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "20", [$"miles-{work.Id}"] = "10" });

        var page = await client.GetStringAsync("/homes");

        Assert.Contains("Driving a month", page);
        Assert.Contains("$78 a month driving, over 120 miles a week", page);
        Assert.Contains("$39", page);
    }

    private static async Task<PropertyView> OneAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<PropertyService>().ListAsync()).Single();
    }

    private static async Task<SearchDestinationView> DestinationAsync(
        AppFactory app,
        string name,
        decimal weight = 1m,
        string? address = null,
        int trips = 0,
        decimal carpool = 0m)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>()
            .CreateDestinationAsync(new SearchDestinationRequest(name, address, weight, trips, carpool));
    }

    private static async Task<TollRoadView> TollRoadAsync(AppFactory app, string road, decimal price)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>()
            .CreateTollRoadAsync(new TollRoadRequest(road, price));
    }

    private static async Task<IReadOnlyList<TollRoadView>> TollRoadsAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>().ListTollRoadsAsync();
    }

    private static async Task<VehicleView> VehicleAsync(AppFactory app, string name, double mpg, decimal price)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>()
            .CreateVehicleAsync(new VehicleRequest(name, mpg, price));
    }

    private static async Task<IReadOnlyList<VehicleView>> VehiclesAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>().ListVehiclesAsync();
    }

    private static async Task<SearchPriorityView> PriorityAsync(AppFactory app, string name, decimal points)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>()
            .CreatePriorityAsync(new SearchPriorityRequest(name, points));
    }

    private static async Task<IReadOnlyList<SearchDestinationView>> DestinationsAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>().ListDestinationsAsync();
    }

    private static async Task<IReadOnlyList<SearchPriorityView>> PrioritiesAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>().ListPrioritiesAsync();
    }

    private static HttpClient Client(AppFactory app) =>
        app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true,
        });

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string handler,
        Dictionary<string, string> fields)
    {
        fields["_handler"] = handler;
        fields["__RequestVerificationToken"] = await TokenAsync(client, path);

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    // The editor posts repeated names, one per line of each list, which a dictionary cannot carry.
    private static async Task<HttpResponseMessage> PostManyAsync(
        HttpClient client,
        string path,
        string handler,
        List<KeyValuePair<string, string>> fields)
    {
        fields.Add(new("_handler", handler));
        fields.Add(new("__RequestVerificationToken", await TokenAsync(client, path)));

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        int propertyId,
        string fileName,
        string contentType,
        string content)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(await TokenAsync(client, $"/homes/{propertyId}")), "__RequestVerificationToken" },
            { new StringContent("The kitchen"), "caption" },
        };

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        return await client.PostAsync($"/homes/{propertyId}/photos", form);
    }

    private static int Inputs(string html, string name) => Regex.Matches(html, $"name=\"{name}\"").Count;

    // The page carries the token in a hidden field, which is also what sets the matching cookie.
    private static async Task<string> TokenAsync(HttpClient client, string path) =>
        Hidden(await client.GetStringAsync(path), "__RequestVerificationToken");

    private static string Hidden(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*?value=\"([^\"]*)\"");

        Assert.True(match.Success, $"No hidden field named {name} in the page.");
        return match.Groups[1].Value;
    }
}
