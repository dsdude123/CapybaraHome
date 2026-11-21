using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers taking a tour: what is kept, what is refused, and the settings it is read with.</summary>
public class PropertyTourServiceTests
{
    [Fact]
    public async Task The_model_settings_start_empty_with_the_hour_timeout_and_save_trimmed()
    {
        await using var tour = await TourHarness.CreateAsync();

        var fresh = await tour.Tours.GetSettingsAsync();
        Assert.Null(fresh.BaseUrl);
        Assert.Null(fresh.Model);
        Assert.Null(fresh.TranscriptionModel);
        Assert.Equal(3600, fresh.TimeoutSeconds);
        Assert.False(fresh.CanProcess);

        var saved = await tour.Tours.SaveSettingsAsync(" http://host.docker.internal:8080/v1/ ", " vision ", " whisper ", 7200);
        Assert.Equal("http://host.docker.internal:8080/v1", saved.BaseUrl);
        Assert.Equal("vision", saved.Model);
        Assert.Equal("whisper", saved.TranscriptionModel);
        Assert.Equal(7200, saved.TimeoutSeconds);
        Assert.True(saved.CanProcess);

        var cleared = await tour.Tours.SaveSettingsAsync(" ", null, "", 3600);
        Assert.Null(cleared.BaseUrl);
        Assert.Null(cleared.Model);
        Assert.Null(cleared.TranscriptionModel);
        Assert.False(cleared.CanProcess);
        Assert.False((cleared with { BaseUrl = "http://x" }).CanProcess);
    }

    [Theory]
    [InlineData("not an address", 60, "web address")]
    [InlineData("ftp://files.test/v1", 60, "web address")]
    [InlineData("javascript:alert(1)", 60, "web address")]
    [InlineData("http://model.test/v1", 0, "between 1 and")]
    [InlineData("http://model.test/v1", 86401, "between 1 and")]
    public async Task Settings_that_cannot_work_are_refused(string url, int timeout, string expected)
    {
        await using var tour = await TourHarness.CreateAsync();

        var refused = await Assert.ThrowsAsync<PropertyTourException>(() =>
            tour.Tours.SaveSettingsAsync(url, "vision", null, timeout));

        Assert.Contains(expected, refused.Message);
        Assert.Null((await tour.Tours.GetSettingsAsync()).BaseUrl);
    }

    [Fact]
    public async Task Only_the_priorities_a_property_is_not_marked_against_are_listed()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var other = await tour.Properties.CreateAsync(new CreatePropertyRequest("Other"));
        var met = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Dishwasher"));
        var missed = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Balcony"));
        var open = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Laundry"));
        var elsewhere = await tour.Search.CreatePriorityAsync(new SearchPriorityRequest("Parking"));

        await tour.Properties.MarkPriorityAsync(property.Id, met.Id, true);
        await tour.Properties.MarkPriorityAsync(property.Id, missed.Id, false);
        await tour.Properties.MarkPriorityAsync(other.Id, elsewhere.Id, true);

        var unmarked = await tour.Tours.UnmarkedPrioritiesAsync(property.Id);

        Assert.Equal([open.Id, elsewhere.Id], unmarked.Select(p => p.Id));
        Assert.Equal(["Laundry", "Parking"], unmarked.Select(p => p.Name));
    }

    [Fact]
    public async Task A_tour_keeps_its_notes_pictures_and_video_with_the_property_and_waits_for_the_model()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));

        var taken = await tour.Tours.SubmitAsync(
            property.Id,
            "  Bright and quiet.  ",
            [TourHarness.Upload("kitchen.jpg", "image/jpeg"), TourHarness.Upload("bath.png", "image/png")],
            TourHarness.Upload("walk.MOV", "video/quicktime", "a video"));

        Assert.Equal(PropertyTourStatus.Processing, taken!.Status);
        Assert.Equal(tour.Db.Clock.GetUtcNow(), taken.SubmittedAt);

        var attachments = await tour.Attachments.ListAsync(property.Id);
        Assert.Equal(2, attachments.Count);
        Assert.Matches(@"^tour-notes-\d{4}-\d{2}-\d{2}-\d{4}\.txt$", attachments[0].FileName);
        Assert.Equal("text/plain; charset=utf-8", attachments[0].ContentType);
        Assert.Equal("walk.MOV", attachments[1].FileName);
        Assert.Equal("video/quicktime", attachments[1].ContentType);

        var notes = await tour.Attachments.FindAsync(attachments[0].Id);
        Assert.Equal("Bright and quiet.", await File.ReadAllTextAsync(notes!.Value.Path));
        Assert.EndsWith(".mov", (await tour.Attachments.FindAsync(attachments[1].Id))!.Value.Path);

        var saved = await tour.Properties.GetAsync(property.Id);
        Assert.Equal(["kitchen.jpg", "bath.png"], saved!.Photos.Select(p => p.FileName));

        var row = await tour.Db.Context.PropertyTours.AsNoTracking().SingleAsync();
        Assert.Equal("Bright and quiet.", row.Notes);
        Assert.Equal(saved.Photos.Select(p => p.Id), row.PhotoIds);
        Assert.Equal(attachments[1].Id, row.VideoAttachmentId);

        var open = Assert.Single(await tour.Tours.ListOpenAsync(property.Id));
        Assert.Equal(taken.Id, open.Id);
        Assert.Empty(await tour.Tours.ListOpenAsync(404));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Any_one_of_the_three_is_enough_for_a_tour(bool notes, bool picture, bool video)
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));

        var taken = await tour.Tours.SubmitAsync(
            property.Id,
            notes ? "A note" : "   ",
            picture ? [TourHarness.Upload("a.jpg", "image/jpeg")] : [],
            video ? TourHarness.Upload("a.mp4", "video/mp4") : null);

        Assert.NotNull(taken);
        Assert.Equal(notes || video ? 1 : 0, (await tour.Attachments.ListAsync(property.Id)).Count);
        Assert.Equal(picture ? 1 : 0, (await tour.Properties.GetAsync(property.Id))!.Photos.Count);
    }

    [Fact]
    public async Task A_tour_of_nothing_of_the_home_lived_in_or_of_no_property_is_not_taken()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var current = await tour.Properties.CreateAsync(new CreatePropertyRequest("Home", IsCurrent: true));

        var empty = await Assert.ThrowsAsync<PropertyTourException>(() =>
            tour.Tours.SubmitAsync(property.Id, " ", [], null));
        Assert.Contains("nothing to submit", empty.Message);

        var lived = await Assert.ThrowsAsync<PropertyTourException>(() =>
            tour.Tours.SubmitAsync(current.Id, "A note", [], null));
        Assert.Contains("not toured", lived.Message);

        Assert.Null(await tour.Tours.SubmitAsync(404, "A note", [], null));
        Assert.Empty(await tour.Db.Context.PropertyTours.ToListAsync());
    }

    [Fact]
    public async Task A_file_of_the_wrong_kind_is_refused_before_anything_is_kept()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));

        var picture = await Assert.ThrowsAsync<PropertyTourException>(() => tour.Tours.SubmitAsync(
            property.Id, "A note", [TourHarness.Upload("a.jpg", "image/jpeg"), TourHarness.Upload("b.heic", "image/heic")], null));
        Assert.Contains("b.heic is not a JPEG", picture.Message);

        var untyped = await Assert.ThrowsAsync<PropertyTourException>(() => tour.Tours.SubmitAsync(
            property.Id, "A note", [TourHarness.Upload("c", null)], null));
        Assert.Contains("c is not a JPEG", untyped.Message);

        var video = await Assert.ThrowsAsync<PropertyTourException>(() => tour.Tours.SubmitAsync(
            property.Id, "A note", [], TourHarness.Upload("walk.pdf", "application/pdf")));
        Assert.Contains("walk.pdf is not a video", video.Message);

        var typeless = await Assert.ThrowsAsync<PropertyTourException>(() => tour.Tours.SubmitAsync(
            property.Id, "A note", [], TourHarness.Upload("walk", null)));
        Assert.Contains("walk is not a video", typeless.Message);

        Assert.Empty(await tour.Attachments.ListAsync(property.Id));
        Assert.Empty((await tour.Properties.GetAsync(property.Id))!.Photos);
    }

    [Fact]
    public async Task A_file_refused_part_way_takes_what_was_already_kept_with_it()
    {
        await using var tour = await TourHarness.CreateAsync(maxAttachmentBytes: 64, maxPhotoBytes: 16);
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));

        var large = await Assert.ThrowsAsync<PropertyTourException>(() => tour.Tours.SubmitAsync(
            property.Id,
            "A note",
            [TourHarness.Upload("a.jpg", "image/jpeg", "small")],
            TourHarness.Upload("walk.mp4", "video/mp4", new string('x', 200))));
        Assert.Contains("walk.mp4 is larger than", large.Message);

        var picture = await Assert.ThrowsAsync<PropertyTourException>(() => tour.Tours.SubmitAsync(
            property.Id,
            "A note",
            [TourHarness.Upload("a.jpg", "image/jpeg", "small"), TourHarness.Upload("b.jpg", "image/jpeg", new string('x', 200))],
            null));
        Assert.Contains("larger than", picture.Message);

        Assert.Empty(await tour.Attachments.ListAsync(property.Id));
        Assert.Empty((await tour.Properties.GetAsync(property.Id))!.Photos);
        Assert.Empty(await tour.Db.Context.PropertyTours.ToListAsync());
        Assert.Empty(Directory.EnumerateFiles(tour.AttachmentDirectory));
        Assert.Empty(Directory.EnumerateFiles(tour.PhotoOptions.Value.Path));
    }

    [Fact]
    public async Task Only_a_failed_tour_can_be_retried_or_dismissed()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Toured"));
        var first = await tour.Tours.SubmitAsync(property.Id, "First", [], null);
        var second = await tour.Tours.SubmitAsync(property.Id, "Second", [], null);

        Assert.False(await tour.Tours.RetryAsync(first!.Id));
        Assert.False(await tour.Tours.DismissAsync(first.Id));
        Assert.False(await tour.Tours.RetryAsync(404));

        await tour.Processor.ProcessAsync(first.Id);
        await tour.Processor.ProcessAsync(second!.Id);

        var failed = await tour.Tours.ListOpenAsync(property.Id);
        Assert.All(failed, t => Assert.Equal(PropertyTourStatus.Failed, t.Status));
        Assert.All(failed, t => Assert.Contains("No model is set up", t.Error));

        Assert.True(await tour.Tours.RetryAsync(first.Id));
        Assert.True(await tour.Tours.DismissAsync(second.Id));

        var open = Assert.Single(await tour.Tours.ListOpenAsync(property.Id));
        Assert.Equal(first.Id, open.Id);
        Assert.Equal(PropertyTourStatus.Processing, open.Status);
        Assert.Null(open.Error);

        var dismissed = await tour.Db.Context.PropertyTours.AsNoTracking().SingleAsync(t => t.Id == second.Id);
        Assert.Equal(PropertyTourStatus.Dismissed, dismissed.Status);
        Assert.Contains("No model is set up", dismissed.Error);
    }
}
