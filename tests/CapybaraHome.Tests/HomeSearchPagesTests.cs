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
            new("square-feet", "850"),
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
        Assert.Equal(850, property.SquareFeet);
        Assert.Equal(["Gym", "Pool"], property.Amenities.Select(a => a.Name));
        Assert.Equal("Walk to the water", property.Pros.Single().Text);
        Assert.Equal("Small kitchen", property.Cons.Single().Text);
        Assert.False(property.IsCurrent);

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("Harbor Steps", page);
        Assert.Contains("$2,475", page);
        Assert.Contains("Size: 850 sq ft, $2.80 rent a sq ft", page);

        var list = await client.GetStringAsync("/homes");
        Assert.Contains("850 sq ft", list);
        Assert.Contains("$2.80", list);
    }

    [Fact]
    public async Task Blanking_the_square_feet_on_the_editor_clears_it()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Roomy", IsCurrent: true, SquareFeet: 1200));

        var editor = await client.GetStringAsync($"/homes/{property.Id}/edit");
        Assert.Contains("value=\"1200\"", editor);

        var current = await client.GetStringAsync("/homes");
        Assert.Contains("1,200 sq ft", current);

        await PostManyAsync(client, $"/homes/{property.Id}/edit", "property-editor",
        [
            new("name", "Roomy"),
            new("is-current", "true"),
            new("square-feet", string.Empty),
            new("action", "save"),
        ]);

        Assert.Null((await ReadAsync(app, property.Id)).SquareFeet);
        Assert.Contains("Size: not recorded", await client.GetStringAsync($"/homes/{property.Id}"));
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
            ["size"] = "0.1",
        });

        Assert.Contains("Saved the weights.", await saved.Content.ReadAsStringAsync());
        Assert.Contains("value=\"0.1\"", await client.GetStringAsync("/homes/settings"));
        Assert.DoesNotContain("No scoring weights are set yet", await client.GetStringAsync("/homes"));

        using var scope = app.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<HomeSearchService>().GetSettingsAsync();
        Assert.Equal(0.05m, settings.MonthlyFeePointsPerDollar);
        Assert.Equal(4m, settings.ConPoints);
        Assert.Equal(0.1m, settings.SizePointsPerSquareFoot);
    }

    [Fact]
    public async Task Size_is_scored_against_the_current_home_and_shown_with_its_rent_a_square_foot()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<HomeSearchService>()
                .SaveSettingsAsync(new HomeSearchSettingsRequest(SizePointsPerSquareFoot: 0.1m));
        }

        var now = await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true, Rent: 2100m, SquareFeet: 700));
        var same = await CreateAsync(app, new CreatePropertyRequest("Same", SquareFeet: 700));
        var bigger = await CreateAsync(app, new CreatePropertyRequest("Bigger", Rent: 2380m, SquareFeet: 850));
        var smaller = await CreateAsync(app, new CreatePropertyRequest("Smaller", SquareFeet: 600));

        var gained = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{bigger.Id}"));
        Assert.Contains("+150 sq ft", gained);
        Assert.Contains("+15", gained);

        var lost = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{smaller.Id}"));
        Assert.Contains("\u2212100 sq ft", lost);
        Assert.Contains("Size: 600 sq ft</p>", lost);

        Assert.Matches(@"<td>Size</td>\s*<td>0 sq ft</td>", await client.GetStringAsync($"/homes/{same.Id}"));
        Assert.Matches(@"<td>Size</td>\s*<td>\u2014</td>", WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{now.Id}")));

        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/homes"));
        Assert.Contains("700 sq ft, $3.00 rent a sq ft", list);
        Assert.Contains("Rent a sq ft</span> $2.80", list);
        Assert.Contains("Rent a sq ft</span> \u2014", list);
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

        // The old host has been switched off, so the box comes back holding the one that answers.
        Assert.Contains("value=\"https://api.heigit.org\"", page);

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
        Assert.Contains("The drive to Work has no car with both a mileage and a fuel price", noPrice);
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
    public async Task A_toll_on_the_route_is_charged_each_way_and_the_credit_comes_off()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3, carpool: 5m);
        await VehicleAsync(app, "The hatchback", 30d, 4.50m);
        await TollRoadAsync(app, "520", 4.50m);
        await TollRoadAsync(app, "99", 2.25m);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, work.Id, 20d, 10d, DriveTimeSource.Measured, ["WA 520"]);
        }

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));

        // No box to tick: the tolls come from the route.
        Assert.DoesNotContain("Crosses", html);

        // $9 of fuel and $27 of tolls, less three days of $5 credit.
        Assert.Contains("$27.00", html);
        Assert.Contains("$15.00 off", html);
        Assert.Contains("$21.00", html);
        Assert.Contains("$91.00", html);

        // The credit comes off the tolls on the page, not onto them.
        Assert.DoesNotContain("$42.00", html);
        Assert.Contains("The tolls less the credit are $12.00 of the week,", html);
        Assert.Contains("$52.00 of the month, whichever car it is driven in.", html);
    }

    [Fact]
    public async Task A_listing_link_is_saved_shown_as_a_link_and_refused_when_it_is_not_one()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var saved = await PostManyAsync(client, "/homes/new", "property-editor",
        [
            new("name", "Harbor Steps"),
            new("listing", "https://example.com/listings/42"),
            new("action", "save"),
        ]);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var property = await OneAsync(app);
        Assert.Equal("https://example.com/listings/42", property.ListingUrl);

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains(
            "<a href=\"https://example.com/listings/42\" target=\"_blank\" rel=\"noopener noreferrer\">",
            page);

        // The box comes back filled, so editing the rest does not drop the link.
        Assert.Contains("value=\"https://example.com/listings/42\"", await client.GetStringAsync($"/homes/{property.Id}/edit"));

        // Something that is not a web address is refused, with the reason on the editor.
        var refused = await PostManyAsync(client, $"/homes/{property.Id}/edit", "property-editor",
        [
            new("name", "Harbor Steps"),
            new("listing", "javascript:alert(1)"),
            new("action", "save"),
        ]);

        Assert.Contains("A listing link has to be a web address", await refused.Content.ReadAsStringAsync());
        Assert.Equal("https://example.com/listings/42", (await OneAsync(app)).ListingUrl);

        // Emptying the box takes the link off, and the page says there is none.
        await PostManyAsync(client, $"/homes/{property.Id}/edit", "property-editor",
        [
            new("name", "Harbor Steps"),
            new("listing", ""),
            new("action", "save"),
        ]);

        Assert.Null((await OneAsync(app)).ListingUrl);
        Assert.Contains("Listing:", await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("no link", await client.GetStringAsync($"/homes/{property.Id}"));
    }

    [Fact]
    public async Task A_toll_road_is_ticked_against_a_drive_and_the_route_hands_over()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3);
        var bridge = await TollRoadAsync(app, "520", 4.50m);
        var tunnel = await TollRoadAsync(app, "99", 2.25m);

        // A route that crosses nothing tolled: both boxes start clear and nothing is charged.
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, work.Id, 20d, 10d, DriveTimeSource.Measured, []);
        }

        var fresh = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Matches($"id=\"toll-{work.Id}-{bridge.Id}\"[^>]*(?!checked)", fresh);
        Assert.Contains("Ticked from the measured route.", WebUtility.HtmlDecode(fresh));
        Assert.DoesNotContain("$27.00", fresh);

        // He takes the tolled way in practice, so he ticks it.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "20"),
            new($"miles-{work.Id}", "10"),
            new($"toll-{work.Id}", bridge.Id.ToString()),
        ]);

        var marked = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("$27.00", marked);

        // Ticking a box is not retyping the time, so the drive is still a measured one.
        Assert.Equal(DriveTimeSource.Measured, (await OneDriveAsync(app, property.Id)).Source);
        Assert.Contains("Marked by hand, so the measured route no longer decides.", marked);
        Assert.Equal([bridge.Id], (await OneDriveAsync(app, property.Id)).TollRoadOverrideIds);

        // Both of them, to prove the second box works as well as the first.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "20"),
            new($"miles-{work.Id}", "10"),
            new($"toll-{work.Id}", bridge.Id.ToString()),
            new($"toll-{work.Id}", tunnel.Id.ToString()),
        ]);

        Assert.Equal([bridge.Id, tunnel.Id], (await OneDriveAsync(app, property.Id)).TollRoadOverrideIds);
        Assert.Contains("$40.50", WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}")));

        // Clearing every box where the route found none of them is the route's own answer, so the
        // marks come off and a later measurement counts again.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "20"),
            new($"miles-{work.Id}", "10"),
        ]);

        var handedBack = await OneDriveAsync(app, property.Id);
        Assert.Null(handedBack.TollRoadOverrideIds);
        Assert.DoesNotContain("$27.00", await client.GetStringAsync($"/homes/{property.Id}"));
    }

    [Fact]
    public async Task Ticks_that_match_the_route_leave_it_in_charge()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3);
        var bridge = await TollRoadAsync(app, "520", 4.50m);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, work.Id, 20d, 10d, DriveTimeSource.Measured, ["WA 520"]);
        }

        // The box the route ticked is left as it is, so the route keeps the drive.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "22"),
            new($"miles-{work.Id}", "10"),
            new($"toll-{work.Id}", bridge.Id.ToString()),
        ]);

        var drive = await OneDriveAsync(app, property.Id);
        Assert.Null(drive.TollRoadOverrideIds);
        Assert.Equal(["WA 520"], drive.TolledRoads);
        Assert.Contains("$27.00", WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}")));

        // Unticking it is a mark, and the charge goes with it.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "22"),
            new($"miles-{work.Id}", "10"),
        ]);

        Assert.Empty((await OneDriveAsync(app, property.Id)).TollRoadOverrideIds!);
        Assert.DoesNotContain("$27.00", await client.GetStringAsync($"/homes/{property.Id}"));
    }

    [Fact]
    public async Task A_destination_can_be_given_one_car_or_left_averaged_over_them_all()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var parents = await DestinationAsync(app, "Parents", trips: 1);
        var thirsty = await VehicleAsync(app, "The truck", 15d, 4m);
        await VehicleAsync(app, "The hatchback", 60d, 4m);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, parents.Id, 30d, 30d, DriveTimeSource.Measured, []);
        }

        // Left averaged: 60 miles a week is 4 gallons in the truck at $16 and 1 in the hatchback
        // at $4, so $10 between them.
        var averaged = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("averaged over 2 cars", averaged);
        Assert.Contains("$10.00", averaged);

        // Set to the truck, the fuel is the truck's alone.
        await PostAsync(client, "/homes/settings", $"destination-{parents.Id}",
            new() { ["action"] = "save", ["name"] = "Parents", ["trips"] = "1", ["vehicle"] = thirsty.Id.ToString() });

        Assert.Equal(thirsty.Id, (await DestinationsAsync(app)).Single().VehicleId);

        var named = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("The truck", named);
        Assert.Contains("$16.00", named);

        // And back to averaged by picking the empty option.
        await PostAsync(client, "/homes/settings", $"destination-{parents.Id}",
            new() { ["action"] = "save", ["name"] = "Parents", ["trips"] = "1", ["vehicle"] = string.Empty });

        Assert.Null((await DestinationsAsync(app)).Single().VehicleId);
        Assert.Contains("averaged over 2 cars", WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}")));
    }

    [Fact]
    public async Task A_fraction_of_a_round_trip_a_week_is_saved_and_costed()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var parents = await DestinationAsync(app, "Parents", trips: 1);
        await VehicleAsync(app, "The hatchback", 30d, 4.50m);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, parents.Id, 40d, 30d, DriveTimeSource.Measured, []);
        }

        await PostAsync(client, "/homes/settings", $"destination-{parents.Id}",
            new() { ["action"] = "save", ["name"] = "Parents", ["trips"] = "0.5" });

        Assert.Equal(0.5m, (await DestinationsAsync(app)).Single().RoundTripsPerWeek);

        // Half a round trip is 30 miles a week, one gallon at $4.50.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("0.5 \u00d7 30 mi each way", page);
        Assert.Contains("$4.50", page);

        // The box is filled with the fraction rather than rounded off.
        Assert.Contains("value=\"0.5\"", await client.GetStringAsync("/homes/settings"));
    }

    [Fact]
    public async Task Every_drive_to_a_destination_is_stretched_by_its_multiplier()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3);

        await PostAsync(client, "/homes/settings", $"destination-{work.Id}",
            new() { ["action"] = "save", ["name"] = "Work", ["trips"] = "3", ["multiplier"] = "1.25" });

        Assert.Equal(1.25d, (await DestinationsAsync(app)).Single().DriveTimeMultiplier);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, work.Id, 20d, 10d, DriveTimeSource.Measured, []);
        }

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("20 min \u00d71.25", page);
        Assert.Contains("= 25 min counted", page);

        // The list shows the stretched drive too, since that is what the score is weighed in.
        Assert.Contains("25 min", WebUtility.HtmlDecode(await client.GetStringAsync("/homes")));

        // Saving the form without touching the numbers leaves the measurement, and the stretch,
        // where they were.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "20"),
            new($"miles-{work.Id}", "10"),
        ]);

        Assert.Contains("= 25 min counted", WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}")));

        // A time typed in by hand is stretched as well: the routing service coming up short is
        // the reason to type one in at all.
        await PostManyAsync(client, $"/homes/{property.Id}", "property-drives",
        [
            new($"drive-{work.Id}", "32"),
            new($"miles-{work.Id}", "10"),
        ]);

        var typed = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("typed in", typed);
        Assert.Contains("32 min \u00d71.25", typed);
        Assert.Contains("= 40 min counted", typed);
    }

    [Fact]
    public async Task The_credit_still_comes_off_the_tolls_with_no_car_to_cost_the_fuel_in()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var property = await CreateAsync(app, new CreatePropertyRequest("Driven"));
        var work = await DestinationAsync(app, "Work", trips: 3, carpool: 5m);
        await TollRoadAsync(app, "520", 4.50m);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PropertyService>().SetDriveTimeAsync(
                property.Id, work.Id, 20d, 10d, DriveTimeSource.Measured, ["WA 520"]);
        }

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));

        Assert.Contains("No cars yet.", html);
        Assert.Contains("$15.00 off", html);
        Assert.Contains("The tolls less the credit are $12.00 of the week,", html);
        Assert.Contains("$52.00 of the month, whichever car it is driven in.", html);
        Assert.DoesNotContain("$42.00", html);
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
        await TollRoadAsync(app, "99", 2.25m);

        await PostAsync(client, $"/homes/{property.Id}", "property-measure", []);

        var drive = (await ReadAsync(app, property.Id)).DriveTimes.Single();
        Assert.Equal(["SR 99 Tunnel", "Evergreen Point Floating Bridge"], drive.TolledRoads);
        Assert.Equal(10d, drive.Miles!.Value, 3);

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.Contains("Tolled roads on the route: SR 99 Tunnel, Evergreen Point Floating Bridge", html);
        Assert.Contains("uses a tolled road nothing prices yet:", html);
        Assert.Contains("Evergreen Point Floating Bridge.", html);
        Assert.Contains("$13.50", html);

        // Retyping the time by hand keeps the tolled roads the route found.
        await PostAsync(client, $"/homes/{property.Id}", "property-drives",
            new() { [$"drive-{drive.DestinationId}"] = "22", [$"miles-{drive.DestinationId}"] = "10" });

        Assert.Equal(
            ["SR 99 Tunnel", "Evergreen Point Floating Bridge"],
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

    [Fact]
    public async Task The_score_carries_a_line_for_what_a_month_of_driving_costs()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<HomeSearchService>()
                .SaveSettingsAsync(new HomeSearchSettingsRequest(MonthlyFeePointsPerDollar: 0.05m));
        }

        var current = await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true));
        var candidate = await CreateAsync(app, new CreatePropertyRequest("There"));
        var work = await DestinationAsync(app, "Work", trips: 3);
        await VehicleAsync(app, "The hatchback", 30d, 4.50m);

        await PostAsync(client, $"/homes/{current.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "40", [$"miles-{work.Id}"] = "20" });
        await PostAsync(client, $"/homes/{candidate.Id}", "property-drives",
            new() { [$"drive-{work.Id}"] = "20", [$"miles-{work.Id}"] = "10" });

        // Sixty miles a week fewer at thirty to the gallon and $4.50 a gallon: $9 a week, $39 a
        // month, which at a twentieth of a point a dollar is a point and a bit.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{candidate.Id}"));

        Assert.Contains("Driving a month", page);
        Assert.Contains("$39", page);
        Assert.Contains("+1.95", page);

        // The current home scores nothing against itself, so the line reads as no difference.
        Assert.DoesNotContain(
            "+1.95", WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{current.Id}")));
    }

    [Fact]
    public async Task A_scenario_is_switched_from_the_board_and_the_scores_follow_it()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var current = await CreateAsync(app, new CreatePropertyRequest("Now", IsCurrent: true, Rent: 2100m));
        await CreateAsync(app, new CreatePropertyRequest("There", Rent: 1900m));

        // The first scenario scores the fees; a second one does not.
        await PostAsync(client, "/homes/settings", "home-weights", new() { ["fees"] = "0.05" });

        var board = WebUtility.HtmlDecode(await client.GetStringAsync("/homes"));
        Assert.Contains("Scored under Default", board);
        Assert.Contains("+10", board);

        await PostAsync(client, "/homes/settings", "profile-new", new() { ["name"] = "No weights" });

        var profiles = await ProfilesAsync(app);
        var second = profiles.Single(p => p.Name == "No weights");
        Assert.True(second.IsActive);

        // A dropdown appears once there is more than one, and switching changes the scores.
        var switcher = await client.GetStringAsync("/homes");
        Assert.Contains("board-profile", switcher);
        Assert.DoesNotContain("+10", WebUtility.HtmlDecode(switcher));

        var first = profiles.Single(p => p.Name == HomeSearchService.DefaultProfileName);
        await PostAsync(client, "/homes", "board-profile", new() { ["profile"] = first.Id.ToString() });

        Assert.True((await ProfilesAsync(app)).Single(p => p.Id == first.Id).IsActive);
        Assert.Contains("+10", WebUtility.HtmlDecode(await client.GetStringAsync("/homes")));

        // The current home is still the current home: properties are not part of a scenario.
        Assert.Contains(current.Name, await client.GetStringAsync("/homes"));
    }

    [Fact]
    public async Task A_scenario_is_copied_renamed_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/homes/settings", "home-weights", new() { ["fees"] = "0.05" });
        await DestinationAsync(app, "Work", trips: 5);

        var first = (await ProfilesAsync(app)).Single();

        // Copying with the box left empty uses the name the placeholder shows, and the copy is
        // what is then being edited.
        await PostAsync(client, "/homes/settings", $"profile-{first.Id}",
            new() { ["action"] = "copy", ["name"] = first.Name, ["copy-name"] = string.Empty });

        var copy = (await ProfilesAsync(app)).Single(p => p.Name == "Default copy");
        Assert.True(copy.IsActive);
        Assert.Equal(1, copy.DestinationCount);
        Assert.Equal(0.05m, (await SettingsAsync(app)).MonthlyFeePointsPerDollar);

        // Renaming it, and editing its destination, leaves the original alone.
        await PostAsync(client, "/homes/settings", $"profile-{copy.Id}",
            new() { ["action"] = "rename", ["name"] = "Two cars" });

        Assert.Equal("Two cars", (await ProfilesAsync(app)).Single(p => p.Id == copy.Id).Name);

        var copiedWork = (await DestinationsAsync(app)).Single();
        await PostAsync(client, "/homes/settings", $"destination-{copiedWork.Id}",
            new() { ["action"] = "save", ["name"] = "Work", ["trips"] = "3" });

        await PostAsync(client, "/homes/settings", $"profile-{first.Id}", new() { ["action"] = "switch", ["name"] = "Default" });
        Assert.Equal(5m, (await DestinationsAsync(app)).Single().RoundTripsPerWeek);

        // The page says what each scenario holds, and the last one cannot be deleted.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/homes/settings"));
        Assert.Contains("showing now", page);
        Assert.Contains("1 destination, 0 cars, 0 toll roads", page);

        await PostAsync(client, "/homes/settings", $"profile-{copy.Id}", new() { ["action"] = "delete", ["name"] = "Two cars" });
        Assert.Single(await ProfilesAsync(app));

        var refused = await PostAsync(client, "/homes/settings", $"profile-{first.Id}",
            new() { ["action"] = "delete", ["name"] = "Default" });

        Assert.Contains("the last scenario stays", await refused.Content.ReadAsStringAsync());
        Assert.Single(await ProfilesAsync(app));
    }

    private static async Task<IReadOnlyList<SearchProfileView>> ProfilesAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>().ListProfilesAsync();
    }

    private static async Task<HomeSearchSettingsView> SettingsAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomeSearchService>().GetSettingsAsync();
    }

    private static async Task<PropertyView> OneAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<PropertyService>().ListAsync()).Single();
    }

    private static async Task<PropertyDriveTimeView> OneDriveAsync(AppFactory app, int propertyId)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<PropertyService>().GetAsync(propertyId))!
            .DriveTimes.Single();
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
