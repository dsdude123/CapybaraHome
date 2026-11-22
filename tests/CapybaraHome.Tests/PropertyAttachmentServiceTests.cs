using System.Text;
using CapybaraHome.Application.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the attachments: where the bytes go, what is refused, and what deleting takes.</summary>
public class PropertyAttachmentServiceTests
{
    [Fact]
    public async Task A_file_is_stored_under_a_name_the_app_chose_and_found_again()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Filed"));

        var added = await tour.Attachments.AddAsync(property.Id, "../../walk.MP4", "video/mp4", Bytes("a video"));

        Assert.Equal("walk.MP4", added.FileName);
        Assert.Equal(7, added.ByteCount);
        Assert.Equal(tour.Db.Clock.GetUtcNow(), added.CreatedAt);
        Assert.Equal(1024 * 1024, tour.Attachments.MaxBytes);

        var found = await tour.Attachments.FindAsync(added.Id);
        Assert.Equal("video/mp4", found!.Value.ContentType);
        Assert.Equal("walk.MP4", found.Value.FileName);
        Assert.EndsWith(".mp4", found.Value.Path);
        Assert.DoesNotContain("walk", Path.GetFileName(found.Value.Path));
        Assert.Equal("a video", await File.ReadAllTextAsync(found.Value.Path));

        Assert.Equal(added, Assert.Single(await tour.Attachments.ListAsync(property.Id)));
    }

    [Fact]
    public async Task An_empty_or_oversized_file_is_refused_and_leaves_nothing_behind()
    {
        await using var tour = await TourHarness.CreateAsync(maxAttachmentBytes: 16);
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Filed"));

        var empty = await Assert.ThrowsAsync<PropertyTourException>(() =>
            tour.Attachments.AddAsync(property.Id, "nothing.mp4", "video/mp4", Bytes(string.Empty)));
        Assert.Contains("nothing.mp4 is empty", empty.Message);

        var large = await Assert.ThrowsAsync<PropertyTourException>(() =>
            tour.Attachments.AddAsync(property.Id, "big.mp4", "video/mp4", Bytes(new string('x', 200))));
        Assert.Contains("big.mp4 is larger than", large.Message);

        Assert.Empty(await tour.Attachments.ListAsync(property.Id));
        Assert.Empty(Directory.EnumerateFiles(tour.AttachmentDirectory));
    }

    [Fact]
    public async Task Deleting_takes_the_file_with_the_row_and_a_lost_file_is_not_found()
    {
        await using var tour = await TourHarness.CreateAsync();
        var property = await tour.Properties.CreateAsync(new CreatePropertyRequest("Filed"));
        var kept = await tour.Attachments.AddAsync(property.Id, "kept.txt", "text/plain", Bytes("kept"));
        var lost = await tour.Attachments.AddAsync(property.Id, "lost.txt", "text/plain", Bytes("lost"));

        File.Delete((await tour.Attachments.FindAsync(lost.Id))!.Value.Path);
        Assert.Null(await tour.Attachments.FindAsync(lost.Id));
        Assert.Null(await tour.Attachments.FindAsync(404));

        Assert.True(await tour.Attachments.DeleteAsync(lost.Id));
        Assert.True(await tour.Attachments.DeleteAsync(kept.Id));
        Assert.False(await tour.Attachments.DeleteAsync(kept.Id));

        Assert.Empty(await tour.Attachments.ListAsync(property.Id));
        Assert.Empty(Directory.EnumerateFiles(tour.AttachmentDirectory));
    }

    [Theory]
    [InlineData("walk.MOV", ".mov")]
    [InlineData("notes.txt", ".txt")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("no-extension", "")]
    [InlineData("trailing.", "")]
    [InlineData("odd.na me", "")]
    [InlineData("long.extension", "")]
    [InlineData("script.p/h", "")]
    public void Only_a_plain_extension_is_carried_onto_the_stored_name(string fileName, string expected)
    {
        Assert.Equal(expected, PropertyAttachmentService.Extension(fileName));
    }

    private static Stream Bytes(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
}
