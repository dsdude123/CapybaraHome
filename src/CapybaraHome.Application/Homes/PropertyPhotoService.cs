using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Application.Homes;

/// <summary>A picture that will not be accepted, with the reason to show.</summary>
/// <param name="message">What to tell whoever uploaded it.</param>
public class PropertyPhotoException(string message) : Exception(message);

/// <summary>
/// Keeps the property pictures. The bytes go in a directory beside the database rather than in a
/// column, so the nightly export stays a readable file; the row is the name they are served under.
/// </summary>
public class PropertyPhotoService(
    ICapybaraHomeDbContext context,
    IOptions<PropertyPhotoOptions> options,
    TimeProvider clock)
{
    /// <summary>The media types a browser can be trusted to render, which is all that is accepted.</summary>
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    /// <summary>What a picture may be called, which is what the type decides.</summary>
    public static IReadOnlyCollection<string> AcceptedTypes => Extensions.Keys;

    /// <summary>Largest picture that will be accepted, in bytes.</summary>
    public long MaxBytes => options.Value.MaxBytes;

    /// <summary>
    /// Stores a picture against a property and returns the row, or null when no property has that
    /// id. The content is read from <paramref name="content"/> rather than held in memory.
    /// </summary>
    /// <param name="propertyId">The property the picture is of.</param>
    /// <param name="fileName">Name the file was uploaded under.</param>
    /// <param name="contentType">Media type the browser gave.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="caption">What the picture shows.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyPhotoException">The file is empty, too large, or not a picture.</exception>
    public async Task<PropertyPhotoView?> AddAsync(
        int propertyId,
        string fileName,
        string? contentType,
        Stream content,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        if (!Extensions.TryGetValue(contentType ?? string.Empty, out var extension))
        {
            throw new PropertyPhotoException($"{fileName} is not a JPEG, PNG, GIF or WebP picture.");
        }

        if (!await context.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken))
        {
            return null;
        }

        var last = await context.PropertyPhotos.AsNoTracking()
            .Where(p => p.PropertyId == propertyId)
            .OrderByDescending(p => p.SortOrder)
            .Select(p => (int?)p.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var directory = Directory.CreateDirectory(options.Value.Path);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory.FullName, storedName);

        long written;
        try
        {
            written = await WriteAsync(content, path, cancellationToken);
        }
        catch (PropertyPhotoException)
        {
            Delete(path);
            throw;
        }

        if (written == 0)
        {
            Delete(path);
            throw new PropertyPhotoException($"{fileName} is empty.");
        }

        var photo = new PropertyPhoto
        {
            PropertyId = propertyId,
            StoredName = storedName,
            FileName = Path.GetFileName(fileName),
            ContentType = contentType!,
            ByteCount = written,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            SortOrder = (last ?? -1) + 1,
            CreatedAt = clock.GetUtcNow(),
        };

        context.PropertyPhotos.Add(photo);
        await context.SaveChangesAsync(cancellationToken);

        return new PropertyPhotoView(
            photo.Id, photo.FileName, photo.ContentType, photo.ByteCount, photo.Caption, photo.SortOrder, photo.CreatedAt);
    }

    /// <summary>Returns a picture's bytes and media type, or null when no picture has that id.</summary>
    public async Task<(Stream Content, string ContentType, string FileName)?> OpenAsync(
        int photoId,
        CancellationToken cancellationToken = default)
    {
        var photo = await context.PropertyPhotos.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);

        if (photo is null)
        {
            return null;
        }

        var path = Path.Combine(options.Value.Path, photo.StoredName);
        if (!File.Exists(path))
        {
            return null;
        }

        return (File.OpenRead(path), photo.ContentType, photo.FileName);
    }

    /// <summary>Changes what a picture shows. Returns false when no picture has that id.</summary>
    public async Task<bool> CaptionAsync(int photoId, string? caption, CancellationToken cancellationToken = default)
    {
        var photo = await context.PropertyPhotos.SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
        if (photo is null)
        {
            return false;
        }

        photo.Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Deletes a picture and its file. Returns false when no picture has that id.</summary>
    public async Task<bool> DeleteAsync(int photoId, CancellationToken cancellationToken = default)
    {
        var photo = await context.PropertyPhotos.SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
        if (photo is null)
        {
            return false;
        }

        context.PropertyPhotos.Remove(photo);
        await context.SaveChangesAsync(cancellationToken);

        Delete(Path.Combine(options.Value.Path, photo.StoredName));
        return true;
    }

    // The size is checked while copying rather than from the header, which a client sets.
    private async Task<long> WriteAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;

        await using var file = File.Create(path);

        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > options.Value.MaxBytes)
            {
                throw new PropertyPhotoException(
                    $"That picture is larger than {options.Value.MaxBytes / (1024 * 1024)} MB.");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
