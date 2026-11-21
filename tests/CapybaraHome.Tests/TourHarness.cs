using System.Text;
using CapybaraHome.Application.Homes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Tests;

/// <summary>
/// The tour services over a throwaway database and directories, with the model answering from
/// canned replies and ffmpeg stood in for, so a tour can be submitted and read without either.
/// </summary>
public sealed class TourHarness : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"capybarahome-tour-{Guid.NewGuid():N}");

    private TourHarness(TestDatabase db, long maxAttachmentBytes, long maxPhotoBytes)
    {
        Db = db;
        PhotoOptions = Options.Create(new PropertyPhotoOptions { Path = Path.Combine(root, "photos"), MaxBytes = maxPhotoBytes });
        var attachmentOptions = Options.Create(new PropertyAttachmentOptions
        {
            Path = Path.Combine(root, "attachments"),
            MaxBytes = maxAttachmentBytes,
        });

        Properties = new PropertyService(db.Context, db.Clock);
        Search = new HomeSearchService(db.Context, db.Clock);
        Photos = new PropertyPhotoService(db.Context, PhotoOptions, db.Clock);
        Attachments = new PropertyAttachmentService(db.Context, attachmentOptions, db.Clock);
        Tours = new PropertyTourService(db.Context, Photos, Attachments, db.Clock);
        Processor = new PropertyTourProcessor(
            db.Context, Properties, Tours, Attachments, new TourAiClient(Http.Client()), Media, PhotoOptions, db.Clock,
            NullLogger<PropertyTourProcessor>.Instance);
    }

    /// <summary>The database underneath.</summary>
    public TestDatabase Db { get; }

    /// <summary>Where the pictures are written.</summary>
    public IOptions<PropertyPhotoOptions> PhotoOptions { get; }

    /// <summary>The model's canned replies and what it was asked.</summary>
    public FakeHttp Http { get; } = new();

    /// <summary>The stand-in for ffmpeg.</summary>
    public FakeMedia Media { get; } = new();

    /// <summary>The property service.</summary>
    public PropertyService Properties { get; }

    /// <summary>The search settings service, for adding priorities.</summary>
    public HomeSearchService Search { get; }

    /// <summary>The picture service.</summary>
    public PropertyPhotoService Photos { get; }

    /// <summary>The attachment service.</summary>
    public PropertyAttachmentService Attachments { get; }

    /// <summary>The tour service.</summary>
    public PropertyTourService Tours { get; }

    /// <summary>The processor under test.</summary>
    public PropertyTourProcessor Processor { get; }

    /// <summary>Directory the attachments are written to.</summary>
    public string AttachmentDirectory => Path.Combine(root, "attachments");

    /// <summary>Builds the harness over a fresh database.</summary>
    public static async Task<TourHarness> CreateAsync(long maxAttachmentBytes = 1024 * 1024, long maxPhotoBytes = 1024) =>
        new(await TestDatabase.CreateAsync(), maxAttachmentBytes, maxPhotoBytes);

    /// <summary>Points the tours at a model, which is what lets one be read.</summary>
    public Task ConfigureAsync(string? transcriptionModel = "whisper") =>
        Tours.SaveSettingsAsync("http://model.test/v1/", "vision", transcriptionModel, 60);

    /// <summary>An upload holding <paramref name="content"/>.</summary>
    public static TourUpload Upload(string fileName, string? contentType, string content = "bytes") =>
        new(fileName, contentType, () => new MemoryStream(Encoding.UTF8.GetBytes(content)));

    /// <summary>A chat reply carrying <paramref name="content"/> as the model's answer.</summary>
    public static string Chat(string content) =>
        System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    /// <summary>Drops the database and the files.</summary>
    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Stands in for ffmpeg: hands back a small sound file and the picture as it is.</summary>
    public sealed class FakeMedia : TourMedia
    {
        /// <summary>Whether the video is treated as having sound.</summary>
        public bool HasSound { get; set; } = true;

        /// <summary>What to fail the sound extraction with, or null to succeed.</summary>
        public string? AudioError { get; set; }

        /// <summary>Whether pictures can be scaled down; when not, the original is sent.</summary>
        public bool CanShrink { get; set; } = true;

        /// <summary>How many videos had their sound taken out.</summary>
        public int Extractions { get; private set; }

        /// <inheritdoc />
        public override async Task<string?> ExtractAudioAsync(string videoPath, CancellationToken cancellationToken = default)
        {
            Extractions++;

            if (AudioError is not null)
            {
                throw new PropertyTourException(AudioError);
            }

            if (!HasSound)
            {
                return null;
            }

            var path = Path.Combine(Path.GetTempPath(), $"capybarahome-fake-{Guid.NewGuid():N}.wav");
            await File.WriteAllTextAsync(path, "RIFF sound", cancellationToken);
            return path;
        }

        /// <inheritdoc />
        public override Task<byte[]?> ShrinkPictureAsync(string picturePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(CanShrink ? Encoding.UTF8.GetBytes("small jpeg") : null);
    }
}
