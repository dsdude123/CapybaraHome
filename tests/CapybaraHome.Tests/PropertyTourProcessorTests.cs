using System.Net;
using System.Text;
using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers reading a tour: what the model is sent, what its answer changes on the property, and
/// that a failure leaves the tour able to be tried again without redoing what already worked.
/// </summary>
public class PropertyTourProcessorTests
{
    [Fact]
    public async Task A_tour_is_transcribed_captioned_summarised_and_the_property_updated()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();

        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest(
            "Toured", Rent: 2100m, Points: [new PropertyPointInput(PropertyPointKind.Pro, "Quiet street")]));
        var laundry = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("In-unit laundry"));
        var balcony = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Balcony"));
        var parking = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Parking"));
        var dishwasher = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Dishwasher"));
        await tour.Properties.MarkPriorityAsync(property.Id, dishwasher.Id, true);
        await tour.Properties.AddNoteAsync(property.Id, "Called the office.");

        var taken = await tour.Tours.SubmitAsync(
            property.Id,
            "Rent quoted at $2,300.",
            [TourHarness.Upload("kitchen.jpg", "image/jpeg"), TourHarness.Upload("bath.jpg", "image/jpeg")],
            TourHarness.Upload("walk.mp4", "video/mp4"));

        tour.Http
            .ReplyTo("audio/transcriptions", """{"text":" There is a washer and dryer in the unit. No balcony. "}""")
            .Reply(TourHarness.Chat("A bright kitchen."))
            .Reply(TourHarness.Chat("A small bathroom."))
            .Reply(TourHarness.Chat($$"""
                {
                  "summary": "A bright, quiet flat.",
                  "priorities": [
                    { "id": {{laundry.Id}}, "decision": "yes", "evidence": "washer and dryer in the unit" },
                    { "id": {{balcony.Id}}, "decision": "no", "evidence": "No balcony." },
                    { "id": {{parking.Id}}, "decision": "unclear" },
                    { "id": {{dishwasher.Id}}, "decision": "no", "evidence": "No balcony" }
                  ],
                  "pros": ["Bright kitchen", "quiet street"],
                  "cons": ["Small bathroom"],
                  "discrepancies": ["Rent was quoted at $2,300 but $2,100 is recorded"]
                }
                """));

        Assert.True(await tour.Processor.ProcessNextAsync());
        Assert.False(await tour.Processor.ProcessNextAsync());

        var row = await tour.Db.Context.PropertyTours.AsNoTracking().SingleAsync();
        Assert.Equal(PropertyTourStatus.Done, row.Status);
        Assert.Null(row.Error);
        Assert.Equal("There is a washer and dryer in the unit. No balcony.", row.Transcript);
        Assert.Equal(tour.Db.Clock.GetUtcNow(), row.FinishedAt);
        Assert.Empty(await tour.Tours.ListOpenAsync(property.Id));

        var saved = await tour.Properties.GetAsync(property.Id);
        Assert.Equal(["A bright kitchen.", "A small bathroom."], saved!.Photos.Select(p => p.Caption));

        var note = saved.Notes.First().Body;
        Assert.Equal(
            "Tour Summary\n\nA bright, quiet flat.\n\nPossible Discrepancies\n\n- Rent was quoted at $2,300 but $2,100 is recorded",
            note);

        Assert.True(saved.Priorities.Single(p => p.PriorityId == laundry.Id).Meets);
        Assert.False(saved.Priorities.Single(p => p.PriorityId == balcony.Id).Meets);
        Assert.DoesNotContain(saved.Priorities, p => p.PriorityId == parking.Id);
        Assert.True(saved.Priorities.Single(p => p.PriorityId == dishwasher.Id).Meets);

        Assert.Equal(["Quiet street", "Bright kitchen"], saved.Pros.Select(p => p.Text));
        Assert.Equal(["Small bathroom"], saved.Cons.Select(p => p.Text));

        Assert.Equal(4, tour.Http.Requests.Count);
        Assert.Equal("http://model.test/v1/audio/transcriptions", tour.Http.Requests[0].RequestUri!.ToString());
        Assert.All(tour.Http.Requests.Skip(1), r => Assert.Equal("http://model.test/v1/chat/completions", r.RequestUri!.ToString()));

        var transcription = Encoding.UTF8.GetString(tour.Http.Bodies[0]);
        Assert.Contains("name=model", transcription);
        Assert.Contains("whisper", transcription);
        Assert.Contains("RIFF sound", transcription);

        var caption = Encoding.UTF8.GetString(tour.Http.Bodies[1]);
        Assert.Contains("\"model\":\"vision\"", caption);
        Assert.Contains("data:image/jpeg;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("small jpeg")), caption);
        Assert.Contains("\"enable_thinking\":false", caption);

        var analysis = System.Text.Json.JsonDocument.Parse(tour.Http.Bodies[3]).RootElement;
        var messages = analysis.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var material = messages[1].GetProperty("content").GetString();
        Assert.Contains("Rent a month: $2100", material);
        Assert.Contains("- Called the office.", material);
        Assert.Contains("- Dishwasher: meets it", material);
        Assert.Contains($"- id {laundry.Id}: In-unit laundry", material);
        Assert.DoesNotContain($"- id {dishwasher.Id}:", material);
        Assert.Contains("Rent quoted at $2,300.", material);
        Assert.Contains("washer and dryer in the unit", material);
        Assert.Contains("1. A bright kitchen.", material);
        Assert.False(analysis.TryGetProperty("chat_template_kwargs", out _));
    }

    [Fact]
    public async Task A_tour_of_notes_alone_makes_one_call_and_leaves_undecided_priorities_alone()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync(transcriptionModel: null);
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var laundry = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("In-unit laundry"));
        var taken = await tour.Tours.SubmitAsync(property.Id, "Nice enough.", [], null);

        tour.Http.Reply(TourHarness.Chat("""{"summary":"Nice enough.","priorities":[],"pros":[],"cons":[],"discrepancies":[]}"""));

        await tour.Processor.ProcessAsync(taken!.Id);

        var saved = await tour.Properties.GetAsync(property.Id);
        Assert.Equal("Tour Summary\n\nNice enough.", saved!.Notes.Single().Body);
        Assert.Empty(saved.Priorities);
        Assert.Empty(saved.Points);
        Assert.Single(tour.Http.Requests);
        Assert.Equal(0, tour.Media.Extractions);
        Assert.Contains($"- id {laundry.Id}: In-unit laundry", Encoding.UTF8.GetString(tour.Http.Bodies[0]));
    }

    [Fact]
    public async Task A_mark_made_by_hand_while_the_model_was_reading_is_not_overwritten()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var laundry = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("In-unit laundry"));
        var taken = await tour.Tours.SubmitAsync(property.Id, "There is laundry.", [TourHarness.Upload("a.jpg", "image/jpeg")], null);

        // The picture is captioned first; the hand-made mark lands before the analysis is applied.
        tour.Http.Reply(TourHarness.Chat("A hallway.")).Reply(TourHarness.Chat(
            $$"""{"summary":"Fine.","priorities":[{"id":{{laundry.Id}},"decision":"yes","evidence":"There is laundry"},{"id":404,"decision":"yes","evidence":"There is laundry"}]}"""));

        await tour.Properties.MarkPriorityAsync(property.Id, laundry.Id, false);
        await tour.Processor.ProcessAsync(taken!.Id);

        var saved = await tour.Properties.GetAsync(property.Id);
        Assert.False(saved!.Priorities.Single().Meets);
        Assert.Equal(PropertyTourStatus.Done, (await tour.Db.Context.PropertyTours.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_model_that_wraps_its_answer_is_asked_once_more()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var first = await tour.Tours.SubmitAsync(property.Id, "First.", [], null);

        tour.Http.Reply(TourHarness.Chat("I think it is a nice place.")).Reply(TourHarness.Chat("""{"summary":"Nice."}"""));
        await tour.Processor.ProcessAsync(first!.Id);

        Assert.Equal("Tour Summary\n\nNice.", (await tour.Properties.GetAsync(property.Id))!.Notes.Single().Body);
        Assert.Equal(2, tour.Http.Requests.Count);
        Assert.Contains("JSON object only", Encoding.UTF8.GetString(tour.Http.Bodies[1]));

    }

    [Fact]
    public async Task A_model_that_never_answers_in_json_fails_the_tour_after_the_second_ask()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var taken = await tour.Tours.SubmitAsync(property.Id, "A note.", [], null);

        tour.Http.Reply(TourHarness.Chat("Still prose."));
        await tour.Processor.ProcessAsync(taken!.Id);

        var failed = Assert.Single(await tour.Tours.ListOpenAsync(property.Id));
        Assert.Equal(PropertyTourStatus.Failed, failed.Status);
        Assert.Contains("did not answer with the JSON", failed.Error);
        Assert.Equal(2, tour.Http.Requests.Count);
        Assert.Empty((await tour.Properties.GetAsync(property.Id))!.Notes);
    }

    [Fact]
    public async Task A_retry_does_not_transcribe_or_caption_again()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var taken = await tour.Tours.SubmitAsync(
            property.Id, null, [TourHarness.Upload("a.jpg", "image/jpeg")], TourHarness.Upload("walk.mp4", "video/mp4"));

        tour.Http
            .ReplyTo("audio/transcriptions", """{"text":"A walk through."}""")
            .Reply(TourHarness.Chat("A hallway."))
            .Reply("the model fell over", HttpStatusCode.InternalServerError)
            .Reply(TourHarness.Chat("""{"summary":"Fine."}"""));

        await tour.Processor.ProcessAsync(taken!.Id);

        var failed = Assert.Single(await tour.Tours.ListOpenAsync(property.Id));
        Assert.Contains("answered 500 while analysing the tour: the model fell over", failed.Error);
        Assert.Equal(3, tour.Http.Requests.Count);

        await tour.Processor.ProcessAsync(taken.Id);
        Assert.Equal(3, tour.Http.Requests.Count);

        Assert.True(await tour.Tours.RetryAsync(taken.Id));
        Assert.True(await tour.Processor.ProcessNextAsync());

        Assert.Equal(4, tour.Http.Requests.Count);
        Assert.Equal(1, tour.Media.Extractions);
        Assert.Empty(await tour.Tours.ListOpenAsync(property.Id));
        var material = Encoding.UTF8.GetString(tour.Http.Bodies[3]);
        Assert.Contains("A walk through.", material);
        Assert.Contains("1. A hallway.", material);
    }

    [Fact]
    public async Task What_is_missing_to_read_a_tour_is_said_on_the_tour()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var taken = await tour.Tours.SubmitAsync(property.Id, "A note", [], TourHarness.Upload("walk.mp4", "video/mp4"));

        await tour.Processor.ProcessAsync(taken!.Id);
        Assert.Contains("No model is set up", (await tour.Tours.ListOpenAsync(property.Id)).Single().Error);

        await tour.ConfigureAsync(transcriptionModel: null);
        await tour.Tours.RetryAsync(taken.Id);
        await tour.Processor.ProcessAsync(taken.Id);
        Assert.Contains("No transcription model is set", (await tour.Tours.ListOpenAsync(property.Id)).Single().Error);

        await tour.ConfigureAsync();
        tour.Media.AudioError = "ffmpeg is not installed, so the video cannot be read.";
        await tour.Tours.RetryAsync(taken.Id);
        await tour.Processor.ProcessAsync(taken.Id);
        Assert.Contains("ffmpeg is not installed", (await tour.Tours.ListOpenAsync(property.Id)).Single().Error);

        Assert.Empty(tour.Http.Requests);
        Assert.Empty((await tour.Properties.GetAsync(property.Id))!.Notes);
    }

    [Fact]
    public async Task A_silent_video_a_picture_that_will_not_shrink_and_files_since_deleted_do_not_stop_a_tour()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var taken = await tour.Tours.SubmitAsync(
            property.Id,
            "A note",
            [
                TourHarness.Upload("kept.jpg", "image/jpeg", "the original"),
                TourHarness.Upload("deleted.jpg", "image/jpeg"),
                TourHarness.Upload("lost.jpg", "image/jpeg"),
                TourHarness.Upload("long.jpg", "image/jpeg"),
            ],
            TourHarness.Upload("walk.mp4", "video/mp4"));

        var photos = (await tour.Properties.GetAsync(property.Id))!.Photos;
        await tour.Photos.DeleteAsync(photos[1].Id);
        var lost = await tour.Db.Context.PropertyPhotos.AsNoTracking().SingleAsync(p => p.Id == photos[2].Id);
        File.Delete(Path.Combine(tour.PhotoOptions.Value.Path, lost.StoredName));

        tour.Media.HasSound = false;
        tour.Media.CanShrink = false;
        tour.Http
            .Reply(TourHarness.Chat("A hallway."))
            .Reply(TourHarness.Chat(new string('a', 600)))
            .Reply(TourHarness.Chat("""{"summary":"Fine.","pros":["{{long}}"]}""".Replace("{{long}}", new string('p', 1200))));

        await tour.Processor.ProcessAsync(taken!.Id);

        var row = await tour.Db.Context.PropertyTours.AsNoTracking().SingleAsync();
        Assert.Equal(PropertyTourStatus.Done, row.Status);
        Assert.Equal(PropertyTourProcessor.NoSound, row.Transcript);
        Assert.Equal(3, tour.Http.Requests.Count);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("the original")), Encoding.UTF8.GetString(tour.Http.Bodies[0]));

        var saved = await tour.Properties.GetAsync(property.Id);
        Assert.Equal(["A hallway.", null, new string('a', 500)], saved!.Photos.Select(p => p.Caption));
        Assert.Equal(1000, saved.Pros.Single().Text.Length);
    }

    [Fact]
    public async Task A_video_deleted_before_the_model_got_to_it_is_passed_over()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var taken = await tour.Tours.SubmitAsync(property.Id, "A note", [], TourHarness.Upload("walk.mp4", "video/mp4"));

        var video = (await tour.Attachments.ListAsync(property.Id)).Single(a => a.FileName == "walk.mp4");
        await tour.Attachments.DeleteAsync(video.Id);
        tour.Http.Reply(TourHarness.Chat("""{"summary":"Fine."}"""));

        await tour.Processor.ProcessAsync(taken!.Id);

        Assert.Empty(await tour.Tours.ListOpenAsync(property.Id));
        Assert.Equal(0, tour.Media.Extractions);
        Assert.Single(tour.Http.Requests);
    }

    [Fact]
    public async Task A_property_deleted_under_a_tour_and_an_unexpected_fault_both_fail_it_cleanly()
    {
        await using var tour = await TourHarness.CreateAsync();
        await tour.ConfigureAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var taken = await tour.Tours.SubmitAsync(property.Id, "A note", [], null);

        tour.Http.Reply("""{"choices":"not a list"}""");
        await tour.Processor.ProcessAsync(taken!.Id);
        Assert.Contains("answered with nothing while analysing the tour", (await tour.Tours.ListOpenAsync(property.Id)).Single().Error);

        await tour.Processor.ProcessAsync(404);
        Assert.False(await tour.Processor.ProcessNextAsync());
    }
}
