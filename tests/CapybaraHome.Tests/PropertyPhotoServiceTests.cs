using System.Text;
using CapybaraHome.Application.Homes;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Tests;

/// <summary>Covers the pictures: what is accepted, where the bytes go, and what deleting takes.</summary>
public class PropertyPhotoServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"capybarahome-photos-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    public async Task Every_accepted_kind_of_picture_is_stored_under_a_name_the_app_chose(string type, string extension)
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));

        var added = await photos.AddAsync(property.Id, "../kitchen.png", type, Bytes("a picture"), "  The kitchen  ");

        Assert.Equal("kitchen.png", added!.FileName);
        Assert.Equal(type, added.ContentType);
        Assert.Equal("The kitchen", added.Caption);
        Assert.Equal(9, added.ByteCount);
        Assert.Equal(db.Clock.GetUtcNow(), added.CreatedAt);

        var file = Directory.EnumerateFiles(directory).Single();
        Assert.EndsWith(extension, file);
        Assert.DoesNotContain("kitchen", Path.GetFileName(file));
        Assert.Contains(type, PropertyPhotoService.AcceptedTypes);
    }

    [Fact]
    public async Task Anything_that_is_not_a_picture_is_refused_before_a_file_is_written()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));

        await Assert.ThrowsAsync<PropertyPhotoException>(() =>
            photos.AddAsync(property.Id, "notes.pdf", "application/pdf", Bytes("not a picture"), null));

        await Assert.ThrowsAsync<PropertyPhotoException>(() =>
            photos.AddAsync(property.Id, "unknown", null, Bytes("no type at all"), null));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task A_picture_too_large_is_refused_and_the_part_written_is_cleaned_up()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db, maxBytes: 16);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));

        await Assert.ThrowsAsync<PropertyPhotoException>(() =>
            photos.AddAsync(property.Id, "big.png", "image/png", Bytes(new string('x', 200)), null));

        Assert.Empty(Directory.EnumerateFiles(directory));
        Assert.Equal(16, photos.MaxBytes);
    }

    [Fact]
    public async Task An_empty_picture_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));

        await Assert.ThrowsAsync<PropertyPhotoException>(() =>
            photos.AddAsync(property.Id, "empty.png", "image/png", Bytes(string.Empty), null));

        Assert.Empty(Directory.EnumerateFiles(directory));
    }

    [Fact]
    public async Task A_picture_of_a_property_that_is_not_there_is_not_stored()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, _) = Services(db);

        Assert.Null(await photos.AddAsync(404, "kitchen.png", "image/png", Bytes("a picture"), null));
    }

    [Fact]
    public async Task The_pictures_keep_the_order_they_were_uploaded_in_and_read_back_as_they_went_in()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));

        await photos.AddAsync(property.Id, "first.png", "image/png", Bytes("first"), null);
        var second = await photos.AddAsync(property.Id, "second.png", "image/png", Bytes("second"), null);

        Assert.Equal([0, 1], (await properties.GetAsync(property.Id))!.Photos.Select(p => p.SortOrder));

        var opened = await photos.OpenAsync(second!.Id);
        using var reader = new StreamReader(opened!.Value.Content);
        Assert.Equal("second", await reader.ReadToEndAsync());
        Assert.Equal("image/png", opened.Value.ContentType);
        Assert.Equal("second.png", opened.Value.FileName);
    }

    [Fact]
    public async Task A_caption_can_be_written_and_taken_back_off()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));
        var photo = await photos.AddAsync(property.Id, "kitchen.png", "image/png", Bytes("a picture"), null);

        Assert.True(await photos.CaptionAsync(photo!.Id, "  The kitchen  "));
        Assert.Equal("The kitchen", (await properties.GetAsync(property.Id))!.Photos.Single().Caption);

        Assert.True(await photos.CaptionAsync(photo.Id, "   "));
        Assert.Null((await properties.GetAsync(property.Id))!.Photos.Single().Caption);

        Assert.False(await photos.CaptionAsync(404, "Nothing"));
    }

    [Fact]
    public async Task Deleting_a_picture_takes_its_file_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));
        var photo = await photos.AddAsync(property.Id, "kitchen.png", "image/png", Bytes("a picture"), null);

        Assert.True(await photos.DeleteAsync(photo!.Id));
        Assert.False(await photos.DeleteAsync(photo.Id));
        Assert.Empty(Directory.EnumerateFiles(directory));
        Assert.Empty((await properties.GetAsync(property.Id))!.Photos);
    }

    [Fact]
    public async Task A_row_whose_file_has_gone_reads_back_as_nothing_rather_than_throwing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (photos, properties) = Services(db);
        var property = await properties.CreateAsync(new CreatePropertyRequest("Pictured"));
        var photo = await photos.AddAsync(property.Id, "kitchen.png", "image/png", Bytes("a picture"), null);

        File.Delete(Directory.EnumerateFiles(directory).Single());

        Assert.Null(await photos.OpenAsync(photo!.Id));
        Assert.Null(await photos.OpenAsync(404));
    }

    /// <summary>Removes the directory the pictures were written to.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private (PropertyPhotoService Photos, PropertyService Properties) Services(TestDatabase db, long maxBytes = 1024)
    {
        var options = Options.Create(new PropertyPhotoOptions { Path = directory, MaxBytes = maxBytes });
        return (new PropertyPhotoService(db.Context, options, db.Clock), new PropertyService(db.Context, db.Clock));
    }

    private static Stream Bytes(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
}
