using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CapybaraHome.Tests;

/// <summary>
/// Drives tour mode the way a browser with no JavaScript does: the tour page, the submission, the
/// alert on the property page in each of its states, the attachments and the model settings.
/// </summary>
public class TourPagesTests
{
    [Fact]
    public async Task Every_property_but_the_home_lived_in_offers_a_tour()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var candidate = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));
        var current = await CreateAsync(app, new CreatePropertyRequest("Home", IsCurrent: true));

        Assert.Contains($"href=\"homes/{candidate.Id}/tour\"", await client.GetStringAsync($"/homes/{candidate.Id}"));
        Assert.DoesNotContain("Start tour", await client.GetStringAsync($"/homes/{current.Id}"));

        var refused = await client.GetStringAsync($"/homes/{current.Id}/tour");
        Assert.Contains("there is no tour to take", refused);
        Assert.DoesNotContain("Submit the tour", refused);

        Assert.Contains("That property is not here.", await client.GetStringAsync("/homes/404/tour"));
    }

    [Fact]
    public async Task The_tour_page_lists_only_the_priorities_still_unmarked()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        var bare = await client.GetStringAsync($"/homes/{property.Id}/tour");
        Assert.Contains("Every priority is already marked", bare);
        Assert.Contains("Tour of Maple Court", bare);
        Assert.Contains("name=\"notes\"", bare);
        Assert.Contains("name=\"pictures\"", bare);
        Assert.Contains("name=\"video\"", bare);
        Assert.Contains("accept=\"image/jpeg,image/png,image/gif,image/webp\"", bare);
        Assert.Contains("up to 16 MB each", bare);
        Assert.Contains("up to 8 GB", bare);

        using (var scope = app.Services.CreateScope())
        {
            var search = scope.ServiceProvider.GetRequiredService<HomeSearchService>();
            var marked = await search.CreatePriorityAsync(new SearchPriorityRequest("Dishwasher"));
            await search.CreatePriorityAsync(new SearchPriorityRequest("In-unit laundry"));
            await scope.ServiceProvider.GetRequiredService<PropertyService>().MarkPriorityAsync(property.Id, marked.Id, false);
        }

        var page = await client.GetStringAsync($"/homes/{property.Id}/tour");
        Assert.Contains("<li>In-unit laundry</li>", page);
        Assert.DoesNotContain("<li>Dishwasher</li>", page);
    }

    [Fact]
    public async Task A_submitted_tour_is_kept_with_the_property_and_shows_as_being_processed()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        var submitted = await SubmitAsync(
            client, property.Id, "Bright and quiet.",
            [("kitchen.jpg", "image/jpeg", "a picture"), ("bath.png", "image/png", "another")],
            ("walk.mp4", "video/mp4", new string('v', 64 * 1024)));

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal($"/homes/{property.Id}", submitted.RequestMessage!.RequestUri!.AbsolutePath);

        var page = await submitted.Content.ReadAsStringAsync();
        Assert.Contains("is being processed", page);
        Assert.Contains("role=\"status\"", page);
        Assert.DoesNotContain("role=\"alert\"", page);
        Assert.Matches(@"tour-notes-\d{4}-\d{2}-\d{2}-\d{4}\.txt", page);
        Assert.Contains(">walk.mp4</a>", page);
        Assert.Contains("64 KB", page);
        Assert.Contains("17 bytes", page);
        Assert.Equal(2, Regex.Matches(page, "<figure class=\"photo\"").Count);

        var notesId = Regex.Match(page, @"homes/attachments/(\d+)"">tour-notes").Groups[1].Value;
        var notes = await client.GetAsync($"/homes/attachments/{notesId}");
        Assert.Equal("Bright and quiet.", await notes.Content.ReadAsStringAsync());
        Assert.Equal("attachment", notes.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/homes/attachments/404")).StatusCode);

        var deleted = await PostAsync(client, $"/homes/{property.Id}", $"attachment-delete-{notesId}");
        var after = await deleted.Content.ReadAsStringAsync();
        Assert.Equal($"/homes/{property.Id}", deleted.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("tour-notes", after);
        Assert.Contains(">walk.mp4</a>", after);
    }

    [Fact]
    public async Task A_refused_submission_comes_back_to_the_tour_page_with_the_reason_and_the_notes()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        var empty = await SubmitAsync(client, property.Id, "  ", [], null);
        var emptyPage = await empty.Content.ReadAsStringAsync();
        Assert.Equal($"/homes/{property.Id}/tour", empty.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("There is nothing to submit", emptyPage);
        Assert.DoesNotContain("draft=", empty.RequestMessage.RequestUri.Query);

        var wrong = await SubmitAsync(client, property.Id, "Notes worth keeping.", [("doc.pdf", "application/pdf", "pdf")], null);
        var wrongPage = await wrong.Content.ReadAsStringAsync();
        Assert.Contains("doc.pdf is not a JPEG, PNG, GIF or WebP picture.", wrongPage);
        Assert.Contains(">Notes worth keeping.</textarea>", wrongPage);

        var many = Enumerable.Range(0, 101).Select(i => ($"p{i}.jpg", "image/jpeg", "x")).ToArray();
        var tooMany = await SubmitAsync(client, property.Id, null, many, null);
        Assert.Contains("at most 100 pictures", await tooMany.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(client, 404, "A note", [], null, tokenFrom: property.Id)).StatusCode);

        var page = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.DoesNotContain("is being processed", page);
        Assert.Contains("No attachments yet.", page);
        Assert.Contains("No pictures yet.", page);
    }

    [Fact]
    public async Task A_submission_without_the_antiforgery_token_is_refused()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        var form = new MultipartFormDataContent { { new StringContent("A note"), "notes" } };
        var refused = await client.PostAsync($"/homes/{property.Id}/tour", form);

        Assert.False(refused.IsSuccessStatusCode);
        Assert.DoesNotContain("is being processed", await client.GetStringAsync($"/homes/{property.Id}"));

        using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>().PropertyTours.ToListAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<PropertyAttachmentService>().ListAsync(property.Id));
    }

    [Fact]
    public async Task A_tour_larger_than_an_ordinary_request_is_still_taken()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        // Past the 17 MB every other request is held to, which the tour endpoint lifts for itself.
        var submitted = await SubmitAsync(
            client, property.Id, null, [], ("walk.mov", "video/quicktime", new string('v', 20 * 1024 * 1024)));

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Contains("20 MB", await submitted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_failed_tour_shows_its_error_and_can_be_retried_or_dismissed()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        await SubmitAsync(client, property.Id, "First tour.", [], null);
        await SubmitAsync(client, property.Id, "Second tour.", [], null);
        await ProcessAllAsync(app);

        var failed = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Equal(2, Regex.Matches(failed, "role=\"alert\"").Count);
        Assert.Contains("No model is set up to read tours.", failed);
        Assert.DoesNotContain("is being processed", failed);

        var ids = Regex.Matches(failed, @"value=""tour-retry-(\d+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(2, ids.Count);

        var retried = await PostAsync(client, $"/homes/{property.Id}", $"tour-retry-{ids[0]}");
        var retriedPage = await retried.Content.ReadAsStringAsync();
        Assert.Equal($"/homes/{property.Id}", retried.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Single(Regex.Matches(retriedPage, "role=\"status\""));
        Assert.Single(Regex.Matches(retriedPage, "role=\"alert\""));

        var dismissed = await PostAsync(client, $"/homes/{property.Id}", $"tour-dismiss-{ids[1]}");
        var dismissedPage = await dismissed.Content.ReadAsStringAsync();
        Assert.Single(Regex.Matches(dismissedPage, "role=\"status\""));
        Assert.DoesNotContain("role=\"alert\"", dismissedPage);
    }

    [Fact]
    public async Task A_tour_the_model_has_read_leaves_its_summary_and_no_alert()
    {
        var model = new FakeHttp().Reply(TourHarness.Chat(
            """{"summary":"A bright flat.","pros":["Bright"],"cons":["Loud street"],"discrepancies":["Rent differs"]}"""));

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.AddHttpClient<TourAiClient>().ConfigurePrimaryHttpMessageHandler(model.Handler));
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        var saved = await PostAsync(client, "/homes/settings", "home-tour-model", new()
        {
            ["url"] = "http://model.test/v1",
            ["model"] = "vision",
            ["transcription"] = "whisper",
            ["timeout"] = "3600",
        });
        Assert.Contains("Saved the model.", await saved.Content.ReadAsStringAsync());

        await SubmitAsync(client, property.Id, "Bright, but the street is loud.", [], null);
        await ProcessAllAsync(app);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/homes/{property.Id}"));
        Assert.DoesNotContain("role=\"status\"", page);
        Assert.DoesNotContain("role=\"alert\"", page);
        Assert.Contains("Tour Summary\n\nA bright flat.\n\nPossible Discrepancies\n\n- Rent differs", page);
        Assert.Contains("Bright", page);
        Assert.Contains("Loud street", page);
        Assert.Equal("http://model.test/v1/chat/completions", model.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task The_model_settings_are_shown_saved_and_refused_with_what_was_typed_kept()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var fresh = await client.GetStringAsync("/homes/settings");
        Assert.Contains("Tour processing", fresh);
        Assert.Matches("id=\"tour-timeout\"[^>]*value=\"3600\"", fresh);
        Assert.Matches("id=\"tour-url\"[^>]*placeholder=\"http://host.docker.internal:8080/v1\"", fresh);

        var saved = await (await PostAsync(client, "/homes/settings", "home-tour-model", new()
        {
            ["url"] = " http://host.docker.internal:8080/v1/ ",
            ["model"] = "qwen3.6-35b-vision",
            ["transcription"] = "whisper",
            ["timeout"] = "7200",
        })).Content.ReadAsStringAsync();

        Assert.Contains("Saved the model.", saved);
        Assert.Matches("id=\"tour-url\"[^>]*value=\"http://host.docker.internal:8080/v1\"", saved);
        Assert.Matches("id=\"tour-model\"[^>]*value=\"qwen3.6-35b-vision\"", saved);
        Assert.Matches("id=\"tour-transcription\"[^>]*value=\"whisper\"", saved);
        Assert.Matches("id=\"tour-timeout\"[^>]*value=\"7200\"", saved);

        foreach (var (url, timeout, expected) in new[]
        {
            ("ftp://nowhere", "60", "The address has to be a web address"),
            ("http://model.test/v1", "soon", "The timeout has to be a whole number of seconds."),
            ("http://model.test/v1", "0", "The timeout has to be between 1 and 86400 seconds."),
        })
        {
            var refused = await (await PostAsync(client, "/homes/settings", "home-tour-model", new()
            {
                ["url"] = url,
                ["model"] = "typed-model",
                ["transcription"] = "typed-whisper",
                ["timeout"] = timeout,
            })).Content.ReadAsStringAsync();

            Assert.Contains(expected, refused);
            Assert.DoesNotContain("Saved the model.", refused);
            Assert.Matches($"id=\"tour-url\"[^>]*value=\"{Regex.Escape(url)}\"", refused);
            Assert.Matches("id=\"tour-model\"[^>]*value=\"typed-model\"", refused);
            Assert.Matches($"id=\"tour-timeout\"[^>]*value=\"{timeout}\"", refused);
        }

        using var scope = app.Services.CreateScope();
        var held = await scope.ServiceProvider.GetRequiredService<PropertyTourService>().GetSettingsAsync();
        Assert.Equal(new TourSettingsView("http://host.docker.internal:8080/v1", "qwen3.6-35b-vision", "whisper", 7200), held);
    }

    [Fact]
    public async Task The_tour_pages_render_statically_with_one_token_in_each_form()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));

        await SubmitAsync(client, property.Id, "Fails.", [], ("walk.mp4", "video/mp4", "a video"));
        await ProcessAllAsync(app);
        await SubmitAsync(client, property.Id, "Waits.", [], null);

        foreach (var path in new[] { $"/homes/{property.Id}/tour", $"/homes/{property.Id}", "/homes/settings" })
        {
            var html = await client.GetStringAsync(path);
            Assert.DoesNotContain("<!--Blazor:", html);

            var forms = Regex.Matches(html, "<form.*?</form>", RegexOptions.Singleline)
                .Select(m => m.Value)
                .Where(f => f.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(forms);
            Assert.All(forms, form => Assert.Single(Regex.Matches(form, "__RequestVerificationToken")));
        }

        var detail = await client.GetStringAsync($"/homes/{property.Id}");
        Assert.Contains("role=\"status\"", detail);
        Assert.Contains("role=\"alert\"", detail);
        Assert.Contains("attachment-delete-", detail);
    }

    [Fact]
    public async Task The_runner_reads_a_waiting_tour_without_being_asked()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var property = await CreateAsync(app, new CreatePropertyRequest("Maple Court"));
        await SubmitAsync(client, property.Id, "A note.", [], null);

        var runner = new PropertyTourRunner(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PropertyTourRunner>.Instance,
            TimeProvider.System);

        await runner.StartAsync(CancellationToken.None);
        try
        {
            for (var attempt = 0; attempt < 100 && await StatusAsync(app) == PropertyTourStatus.Processing; attempt++)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            await runner.StopAsync(CancellationToken.None);
        }

        Assert.Equal(PropertyTourStatus.Failed, await StatusAsync(app));
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<PropertyTourRunner>().DefaultIfEmpty());
    }

    private static async Task<PropertyTourStatus> StatusAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>()
            .PropertyTours.AsNoTracking().Select(t => t.Status).SingleAsync();
    }

    private static async Task ProcessAllAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<PropertyTourProcessor>();
        while (await processor.ProcessNextAsync())
        {
        }
    }

    private static async Task<PropertyView> CreateAsync(AppFactory app, CreatePropertyRequest request)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PropertyService>().CreateAsync(request);
    }

    private static HttpClient Client(AppFactory app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string handler,
        Dictionary<string, string>? fields = null)
    {
        var form = fields ?? [];
        form["_handler"] = handler;
        form["__RequestVerificationToken"] = FormPost.Hidden(await client.GetStringAsync(path), "__RequestVerificationToken");

        return await client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client,
        int propertyId,
        string? notes,
        (string Name, string Type, string Content)[] pictures,
        (string Name, string Type, string Content)? video,
        int? tokenFrom = null)
    {
        var token = FormPost.Hidden(
            await client.GetStringAsync($"/homes/{tokenFrom ?? propertyId}/tour"), "__RequestVerificationToken");

        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };

        if (notes is not null)
        {
            form.Add(new StringContent(notes), "notes");
        }

        foreach (var (name, type, content) in pictures)
        {
            form.Add(File(type, content), "pictures", name);
        }

        if (video is { } taken)
        {
            form.Add(File(taken.Type, taken.Content), "video", taken.Name);
        }

        return await client.PostAsync($"/homes/{propertyId}/tour", form);
    }

    private static ByteArrayContent File(string type, string content)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return file;
    }
}
