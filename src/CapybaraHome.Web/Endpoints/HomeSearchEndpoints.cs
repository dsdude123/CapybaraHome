using CapybaraHome.Application.Homes;
using Microsoft.AspNetCore.Mvc;

namespace CapybaraHome.Web.Endpoints;

/// <summary>
/// The picture posts and reads the property page cannot do itself: Blazor's static form binding
/// does not carry files, so the upload posts here and comes back to the page with the outcome.
/// </summary>
public static class HomeSearchEndpoints
{
    /// <summary>Maps the picture upload and the picture itself.</summary>
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
    }

    private static IResult Back(int id, string? message = null, string? error = null)
    {
        var query = message is not null
            ? $"?photo={Uri.EscapeDataString(message)}"
            : $"?photoError={Uri.EscapeDataString(error ?? string.Empty)}";

        return Results.Redirect($"/homes/{id}{query}");
    }
}
