using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// Reads a submitted tour with the model and updates the property from what it finds: a
/// transcript of the video, a caption on each picture, a summary note, the priorities it could
/// settle, and any new pros and cons. A failure is kept on the tour so it can be tried again.
/// </summary>
public class PropertyTourProcessor(
    ICapybaraHomeDbContext context,
    PropertyService properties,
    PropertyTourService tours,
    PropertyAttachmentService attachments,
    TourAiClient model,
    TourMedia media,
    IOptions<PropertyPhotoOptions> photoOptions,
    TimeProvider clock,
    ILogger<PropertyTourProcessor> logger)
{
    /// <summary>What stands in for a transcript when the video recorded no sound.</summary>
    public const string NoSound = "(The video has no sound.)";

    /// <summary>Reads the oldest tour that is waiting. Returns false when none is.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var next = await context.PropertyTours.AsNoTracking()
            .Where(t => t.Status == PropertyTourStatus.Processing)
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (next is null)
        {
            return false;
        }

        await ProcessAsync(next.Value, cancellationToken);
        return true;
    }

    /// <summary>Reads one tour. A tour that is not waiting is left alone.</summary>
    /// <param name="tourId">The tour to read.</param>
    /// <param name="cancellationToken">Cancels the work, which leaves the tour waiting.</param>
    public async Task ProcessAsync(int tourId, CancellationToken cancellationToken = default)
    {
        var tour = await context.PropertyTours
            .SingleOrDefaultAsync(t => t.Id == tourId && t.Status == PropertyTourStatus.Processing, cancellationToken);

        if (tour is null)
        {
            return;
        }

        try
        {
            var settings = await tours.GetSettingsAsync(cancellationToken);
            if (!settings.CanProcess)
            {
                throw new PropertyTourException(
                    "No model is set up to read tours. Set one under the search settings, then retry.");
            }

            await TranscribeAsync(tour, settings, cancellationToken);
            var captions = await CaptionAsync(tour, settings, cancellationToken);
            var findings = await AnalyseAsync(tour, settings, captions, cancellationToken);
            await ApplyAsync(tour, findings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PropertyTourException ex)
        {
            await FailAsync(tour, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading tour {TourId} failed.", tourId);
            await FailAsync(tour, $"Something went wrong reading the tour: {ex.Message}");
        }
    }

    private async Task TranscribeAsync(PropertyTour tour, TourSettingsView settings, CancellationToken cancellationToken)
    {
        if (tour.VideoAttachmentId is not { } videoId || tour.Transcript is not null)
        {
            return;
        }

        if (await attachments.FindAsync(videoId, cancellationToken) is not { } video)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.TranscriptionModel))
        {
            throw new PropertyTourException(
                "No transcription model is set, so the video cannot be read. Set one under the search settings, then retry.");
        }

        var audio = await media.ExtractAudioAsync(video.Path, cancellationToken);
        if (audio is null)
        {
            tour.Transcript = NoSound;
        }
        else
        {
            try
            {
                tour.Transcript = await model.TranscribeAsync(settings, audio, cancellationToken);
            }
            finally
            {
                File.Delete(audio);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<string>> CaptionAsync(PropertyTour tour, TourSettingsView settings, CancellationToken cancellationToken)
    {
        var captions = new List<string>();

        foreach (var photoId in tour.PhotoIds)
        {
            var photo = await context.PropertyPhotos.SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
            if (photo is null)
            {
                continue;
            }

            if (photo.Caption is null)
            {
                var path = Path.Combine(photoOptions.Value.Path, photo.StoredName);
                if (!File.Exists(path))
                {
                    continue;
                }

                var picture = await media.ShrinkPictureAsync(path, cancellationToken)
                    ?? await File.ReadAllBytesAsync(path, cancellationToken);

                var caption = await model.AskAsync(
                    settings, null, TourAnalysis.CaptionPrompt, picture, think: false,
                    $"describing {photo.FileName}", cancellationToken);

                photo.Caption = caption.Length <= 500 ? caption : caption[..500];
                await context.SaveChangesAsync(cancellationToken);
            }

            captions.Add(photo.Caption);
        }

        return captions;
    }

    private async Task<TourFindings> AnalyseAsync(
        PropertyTour tour,
        TourSettingsView settings,
        List<string> captions,
        CancellationToken cancellationToken)
    {
        var property = await properties.GetAsync(tour.PropertyId, cancellationToken)
            ?? throw new PropertyTourException("The property is no longer here.");

        var unmarked = await tours.UnmarkedPrioritiesAsync(tour.PropertyId, cancellationToken);
        var ids = unmarked.Select(p => p.Id).ToHashSet();

        var material = TourAnalysis.Material(
            property,
            unmarked,
            tour.Notes,
            tour.Transcript,
            captions,
            property.Notes.Select(n => n.Body).ToList(),
            property.Photos
                .Where(p => p.Caption is not null && !tour.PhotoIds.Contains(p.Id))
                .Select(p => p.Caption!)
                .ToList());

        var spoken = string.Join('\n', captions.Prepend(tour.Transcript).Prepend(tour.Notes));

        var answer = await model.AskAsync(
            settings, TourAnalysis.SystemPrompt, material, null, think: true, "analysing the tour", cancellationToken);

        try
        {
            return TourAnalysis.Parse(answer, ids, spoken);
        }
        catch (PropertyTourException)
        {
            // A model that wraps its answer in prose usually gets it right when told so once.
            answer = await model.AskAsync(
                settings,
                TourAnalysis.SystemPrompt,
                material + "\nAnswer with the JSON object only, starting with { and ending with }.",
                null,
                think: false,
                "analysing the tour",
                cancellationToken);

            return TourAnalysis.Parse(answer, ids, spoken);
        }
    }

    private async Task ApplyAsync(PropertyTour tour, TourFindings findings, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        context.PropertyNotes.Add(new PropertyNote
        {
            PropertyId = tour.PropertyId,
            Body = TourAnalysis.Note(findings),
            CreatedAt = now,
        });

        // Marked again here rather than trusted from when the model was asked: the model can take
        // an hour, and a mark made by hand in that time is not overwritten.
        var marked = await context.PropertyPriorities
            .Where(m => m.PropertyId == tour.PropertyId)
            .Select(m => m.PriorityId)
            .ToListAsync(cancellationToken);

        var known = await context.SearchPriorities.Select(p => p.Id).ToListAsync(cancellationToken);

        foreach (var (priorityId, meets) in findings.Priorities)
        {
            if (known.Contains(priorityId) && !marked.Contains(priorityId))
            {
                context.PropertyPriorities.Add(new PropertyPriority
                {
                    PropertyId = tour.PropertyId,
                    PriorityId = priorityId,
                    Meets = meets,
                });
            }
        }

        var points = await context.PropertyPoints
            .Where(p => p.PropertyId == tour.PropertyId)
            .ToListAsync(cancellationToken);

        AddPoints(tour.PropertyId, PropertyPointKind.Pro, findings.Pros, points);
        AddPoints(tour.PropertyId, PropertyPointKind.Con, findings.Cons, points);

        tour.Status = PropertyTourStatus.Done;
        tour.Error = null;
        tour.FinishedAt = now;

        await context.SaveChangesAsync(cancellationToken);
    }

    private void AddPoints(int propertyId, PropertyPointKind kind, IReadOnlyList<string> texts, List<PropertyPoint> held)
    {
        var next = held.Where(p => p.Kind == kind).Select(p => p.SortOrder).DefaultIfEmpty(-1).Max() + 1;

        foreach (var text in texts)
        {
            var point = text.Length <= 1000 ? text : text[..1000];
            if (held.Any(p => p.Kind == kind && string.Equals(p.Text, point, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var added = new PropertyPoint { PropertyId = propertyId, Kind = kind, Text = point, SortOrder = next++ };
            held.Add(added);
            context.PropertyPoints.Add(added);
        }
    }

    private async Task FailAsync(PropertyTour tour, string error)
    {
        tour.Status = PropertyTourStatus.Failed;
        tour.Error = error.Length <= 2000 ? error : error[..2000];
        tour.FinishedAt = clock.GetUtcNow();

        try
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tour {TourId} failed and the failure could not be saved: {Error}", tour.Id, error);
        }
    }
}
