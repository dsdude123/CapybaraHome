using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// Takes a tour of a property: keeps what was written and uploaded, and queues it for the model.
/// The reading itself is <see cref="PropertyTourProcessor"/>'s, which runs after the page has answered.
/// </summary>
public class PropertyTourService(
    ICapybaraHomeDbContext context,
    PropertyPhotoService photos,
    PropertyAttachmentService attachments,
    TimeProvider clock)
{
    /// <summary>Longest a single call to the model may be allowed, in seconds.</summary>
    public const int MaxTimeoutSeconds = 86400;

    /// <summary>Returns the service the tours are read by.</summary>
    public async Task<TourSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var held = await context.HomeSearchSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? new HomeSearchSettings();

        return new TourSettingsView(
            held.TourAiBaseUrl, held.TourAiModel, held.TourTranscriptionModel, held.TourAiTimeoutSeconds);
    }

    /// <summary>Saves the service the tours are read by. A blank address or model clears it.</summary>
    /// <param name="baseUrl">Base address of the OpenAI-compatible service.</param>
    /// <param name="model">Model that captions the pictures and analyses the tour.</param>
    /// <param name="transcriptionModel">Model that transcribes the video.</param>
    /// <param name="timeoutSeconds">How long one call may take, in seconds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyTourException">The address is not a web address, or the timeout is out of range.</exception>
    public async Task<TourSettingsView> SaveSettingsAsync(
        string? baseUrl,
        string? model,
        string? transcriptionModel,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var address = Blank(baseUrl)?.TrimEnd('/');

        if (address is not null
            && !(Uri.TryCreate(address, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && uri.Host.Length > 0))
        {
            throw new PropertyTourException("The address has to be a web address, such as http://host.docker.internal:8080/v1.");
        }

        if (timeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            throw new PropertyTourException($"The timeout has to be between 1 and {MaxTimeoutSeconds} seconds.");
        }

        var settings = await context.HomeSearchSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new HomeSearchSettings();
            context.HomeSearchSettings.Add(settings);
        }

        settings.TourAiBaseUrl = address;
        settings.TourAiModel = Blank(model);
        settings.TourTranscriptionModel = Blank(transcriptionModel);
        settings.TourAiTimeoutSeconds = timeoutSeconds;
        settings.UpdatedAt = clock.GetUtcNow();

        await context.SaveChangesAsync(cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    /// <summary>Returns the priorities a property has not been marked against, in list order.</summary>
    public async Task<IReadOnlyList<SearchPriorityView>> UnmarkedPrioritiesAsync(
        int propertyId,
        CancellationToken cancellationToken = default)
    {
        return await context.SearchPriorities.AsNoTracking()
            .Where(p => !p.Properties.Any(m => m.PropertyId == propertyId))
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Select(p => new SearchPriorityView(p.Id, p.Name, p.Points, p.SortOrder, 0, p.Penalty))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns a property's tours that are still being read or that failed, oldest first.</summary>
    public async Task<IReadOnlyList<PropertyTourView>> ListOpenAsync(int propertyId, CancellationToken cancellationToken = default)
    {
        return await context.PropertyTours.AsNoTracking()
            .Where(t => t.PropertyId == propertyId
                && (t.Status == PropertyTourStatus.Processing || t.Status == PropertyTourStatus.Failed))
            .OrderBy(t => t.Id)
            .Select(t => new PropertyTourView(t.Id, t.PropertyId, t.Status, t.Error, t.SubmittedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Takes a tour: attaches the notes as a text file, the pictures and the video to the property,
    /// and queues the tour for the model. Returns null when no property has that id.
    /// </summary>
    /// <param name="propertyId">The property that was toured.</param>
    /// <param name="notes">What was typed on the tour.</param>
    /// <param name="pictures">The pictures taken.</param>
    /// <param name="video">The video taken.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyTourException">
    /// Nothing was submitted, the property is the one lived in, or a file is not what it should be.
    /// </exception>
    public async Task<PropertyTourView?> SubmitAsync(
        int propertyId,
        string? notes,
        IReadOnlyList<TourUpload> pictures,
        TourUpload? video,
        CancellationToken cancellationToken = default)
    {
        var property = await context.Properties.AsNoTracking()
            .Where(p => p.Id == propertyId)
            .Select(p => new { p.IsCurrent })
            .SingleOrDefaultAsync(cancellationToken);

        if (property is null)
        {
            return null;
        }

        if (property.IsCurrent)
        {
            throw new PropertyTourException("The home you live in is not toured.");
        }

        var text = Blank(notes);
        if (text is null && pictures.Count == 0 && video is null)
        {
            throw new PropertyTourException("There is nothing to submit: write a note, or add a picture or a video.");
        }

        foreach (var picture in pictures)
        {
            if (!PropertyPhotoService.AcceptedTypes.Contains(picture.ContentType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                throw new PropertyTourException($"{picture.FileName} is not a JPEG, PNG, GIF or WebP picture.");
            }
        }

        if (video is not null && !(video.ContentType ?? string.Empty).StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new PropertyTourException($"{video.FileName} is not a video.");
        }

        var now = clock.GetUtcNow();
        var addedPhotos = new List<int>();
        var addedAttachments = new List<int>();

        try
        {
            if (text is not null)
            {
                var name = $"tour-notes-{clock.GetLocalNow():yyyy-MM-dd-HHmm}.txt";
                using var body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                addedAttachments.Add((await attachments.AddAsync(
                    propertyId, name, "text/plain; charset=utf-8", body, cancellationToken)).Id);
            }

            foreach (var picture in pictures)
            {
                await using var content = picture.Open();
                var added = await photos.AddAsync(
                    propertyId, picture.FileName, picture.ContentType, content, null, cancellationToken);
                addedPhotos.Add(added!.Id);
            }

            int? videoId = null;
            if (video is not null)
            {
                await using var content = video.Open();
                videoId = (await attachments.AddAsync(
                    propertyId, video.FileName, video.ContentType!, content, cancellationToken)).Id;
                addedAttachments.Add(videoId.Value);
            }

            var tour = new PropertyTour
            {
                PropertyId = propertyId,
                Status = PropertyTourStatus.Processing,
                Notes = text,
                PhotoIds = addedPhotos,
                VideoAttachmentId = videoId,
                SubmittedAt = now,
            };

            context.PropertyTours.Add(tour);
            await context.SaveChangesAsync(cancellationToken);

            return new PropertyTourView(tour.Id, tour.PropertyId, tour.Status, tour.Error, tour.SubmittedAt);
        }
        catch (Exception ex) when (ex is PropertyTourException or PropertyPhotoException)
        {
            // A tour is taken whole or not at all, so what was kept before the refused file goes too.
            foreach (var id in addedPhotos)
            {
                await photos.DeleteAsync(id, CancellationToken.None);
            }

            foreach (var id in addedAttachments)
            {
                await attachments.DeleteAsync(id, CancellationToken.None);
            }

            throw new PropertyTourException(ex.Message);
        }
    }

    /// <summary>Queues a failed tour for the model again. Returns false when no failed tour has that id.</summary>
    public Task<bool> RetryAsync(int tourId, CancellationToken cancellationToken = default) =>
        MoveFailedAsync(tourId, PropertyTourStatus.Processing, cancellationToken);

    /// <summary>Sets a failed tour aside, which takes its alert off the page. Returns false when no failed tour has that id.</summary>
    public Task<bool> DismissAsync(int tourId, CancellationToken cancellationToken = default) =>
        MoveFailedAsync(tourId, PropertyTourStatus.Dismissed, cancellationToken);

    private async Task<bool> MoveFailedAsync(int tourId, PropertyTourStatus status, CancellationToken cancellationToken)
    {
        var tour = await context.PropertyTours
            .SingleOrDefaultAsync(t => t.Id == tourId && t.Status == PropertyTourStatus.Failed, cancellationToken);

        if (tour is null)
        {
            return false;
        }

        tour.Status = status;
        tour.Error = status == PropertyTourStatus.Processing ? null : tour.Error;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
