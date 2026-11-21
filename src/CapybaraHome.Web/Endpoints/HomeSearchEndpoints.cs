using CapybaraHome.Application.Homes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Web.Endpoints;

/// <summary>
/// The file posts and reads the property pages cannot do themselves: Blazor's static form binding
/// does not carry files, so an upload posts here and comes back to the page with the outcome.
/// </summary>
public static class HomeSearchEndpoints
{
    /// <summary>Maps the picture upload, the tour submission and the files themselves.</summary>
    public static void MapHomeSearch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/homes/{id:int}/photos", async (
            int id,
            IFormFile file,
            [FromForm] string? caption,
            PropertyPhotoService photos,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var added = await photos.AddAsync(
                    id, file.FileName, file.ContentType, file.OpenReadStream(), caption, cancellationToken);

                return added is null
                    ? Results.NotFound()
                    : Back(id, message: $"Added {added.FileName}.");
            }
            catch (PropertyPhotoException ex)
            {
                return Back(id, error: ex.Message);
            }
        });

        app.MapGet("/homes/photos/{photoId:int}", async (
            int photoId,
            PropertyPhotoService photos,
            CancellationToken cancellationToken) =>
        {
            var found = await photos.OpenAsync(photoId, cancellationToken);
            return found is null
                ? Results.NotFound()
                : Results.File(found.Value.Content, found.Value.ContentType, found.Value.FileName);
        });

        // A tour carries a video, so this one post is allowed the attachment limit rather than
        // the picture limit every other request is held to.
        var tourLimit = app.ServiceProvider.GetRequiredService<IOptions<PropertyAttachmentOptions>>().Value.MaxBytes
            + (app.ServiceProvider.GetRequiredService<IOptions<PropertyPhotoOptions>>().Value.MaxBytes * MaxTourPictures);

        app.MapPost("/homes/{id:int}/tour", async (
            int id,
            [FromForm] string? notes,
            IFormFileCollection files,
            PropertyTourService tours,
            TourDraftStore drafts,
            CancellationToken cancellationToken) =>
        {
            var pictures = files
                .Where(f => f.Name == "pictures" && f.Length > 0)
                .Select(f => new TourUpload(f.FileName, f.ContentType, f.OpenReadStream))
                .ToList();

            var video = files
                .Where(f => f.Name == "video" && f.Length > 0)
                .Select(f => new TourUpload(f.FileName, f.ContentType, f.OpenReadStream))
                .FirstOrDefault();

            try
            {
                if (pictures.Count > MaxTourPictures)
                {
                    throw new PropertyTourException($"A tour takes at most {MaxTourPictures} pictures at a time.");
                }

                var tour = await tours.SubmitAsync(id, notes, pictures, video, cancellationToken);

                return tour is null
                    ? Results.NotFound()
                    : Results.Redirect($"/homes/{id}");
            }
            catch (PropertyTourException ex)
            {
                var draft = drafts.Keep(notes);
                var query = $"?error={Uri.EscapeDataString(ex.Message)}" + (draft is null ? string.Empty : $"&draft={draft}");
                return Results.Redirect($"/homes/{id}/tour{query}");
            }
        }).WithMetadata(new RequestSizeLimitAttribute(tourLimit));

        app.MapGet("/homes/attachments/{attachmentId:int}", async (
            int attachmentId,
            PropertyAttachmentService attachments,
            CancellationToken cancellationToken) =>
        {
            var found = await attachments.FindAsync(attachmentId, cancellationToken);
            return found is null
                ? Results.NotFound()
                : Results.File(
                    found.Value.Path, found.Value.ContentType, found.Value.FileName, enableRangeProcessing: true);
        });
    }

    /// <summary>Most pictures one tour may carry.</summary>
    public const int MaxTourPictures = 100;

    private static IResult Back(int id, string? message = null, string? error = null)
    {
        var query = message is not null
            ? $"?photo={Uri.EscapeDataString(message)}"
            : $"?photoError={Uri.EscapeDataString(error ?? string.Empty)}";

        return Results.Redirect($"/homes/{id}{query}");
    }
}
