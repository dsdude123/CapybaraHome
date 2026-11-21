using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Application.Homes;

/// <summary>A file kept with a property, as the pages list it.</summary>
/// <param name="Id">The attachment's id.</param>
/// <param name="FileName">Name it is downloaded under.</param>
/// <param name="ContentType">Media type it is served as.</param>
/// <param name="ByteCount">How large it is, in bytes.</param>
/// <param name="CreatedAt">When it was attached.</param>
public record PropertyAttachmentView(int Id, string FileName, string ContentType, long ByteCount, DateTimeOffset CreatedAt);

/// <summary>
/// Keeps the files attached to a property. Like the pictures, the bytes go in a directory beside
/// the database and the row is the name they are downloaded under.
/// </summary>
public class PropertyAttachmentService(
    ICapybaraHomeDbContext context,
    IOptions<PropertyAttachmentOptions> options,
    TimeProvider clock)
{
    /// <summary>Largest file that will be accepted, in bytes.</summary>
    public long MaxBytes => options.Value.MaxBytes;

    /// <summary>Returns a property's attachments, oldest first.</summary>
    public async Task<IReadOnlyList<PropertyAttachmentView>> ListAsync(int propertyId, CancellationToken cancellationToken = default)
    {
        return await context.PropertyAttachments.AsNoTracking()
            .Where(a => a.PropertyId == propertyId)
            .OrderBy(a => a.Id)
            .Select(a => new PropertyAttachmentView(a.Id, a.FileName, a.ContentType, a.ByteCount, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Stores a file against a property and returns the row. The content is copied from
    /// <paramref name="content"/> rather than held in memory.
    /// </summary>
    /// <param name="propertyId">The property the file is kept with.</param>
    /// <param name="fileName">Name the file is downloaded under.</param>
    /// <param name="contentType">Media type it is served as.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyTourException">The file is empty or too large.</exception>
    public async Task<PropertyAttachmentView> AddAsync(
        int propertyId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(fileName);
        var storedName = $"{Guid.NewGuid():N}{Extension(name)}";
        var path = Path.Combine(Directory.CreateDirectory(options.Value.Path).FullName, storedName);

        long written;
        try
        {
            written = await WriteAsync(content, path, name, cancellationToken);
        }
        catch
        {
            Remove(path);
            throw;
        }

        if (written == 0)
        {
            Remove(path);
            throw new PropertyTourException($"{name} is empty.");
        }

        var attachment = new PropertyAttachment
        {
            PropertyId = propertyId,
            StoredName = storedName,
            FileName = name,
            ContentType = contentType,
            ByteCount = written,
            CreatedAt = clock.GetUtcNow(),
        };

        context.PropertyAttachments.Add(attachment);
        await context.SaveChangesAsync(cancellationToken);

        return new PropertyAttachmentView(
            attachment.Id, attachment.FileName, attachment.ContentType, attachment.ByteCount, attachment.CreatedAt);
    }

    /// <summary>Returns where an attachment's file is on disk, or null when it is not there.</summary>
    public async Task<(string Path, string ContentType, string FileName)?> FindAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await context.PropertyAttachments.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(options.Value.Path, attachment.StoredName));
        return File.Exists(path) ? (path, attachment.ContentType, attachment.FileName) : null;
    }

    /// <summary>Deletes an attachment and its file. Returns false when no attachment has that id.</summary>
    public async Task<bool> DeleteAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await context.PropertyAttachments.SingleOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return false;
        }

        context.PropertyAttachments.Remove(attachment);
        await context.SaveChangesAsync(cancellationToken);

        Remove(Path.Combine(options.Value.Path, attachment.StoredName));
        return true;
    }

    /// <summary>Returns the extension a file is stored under: its own when that is plain, and none otherwise.</summary>
    public static string Extension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension.Length is > 1 and <= 6 && extension.Skip(1).All(char.IsAsciiLetterOrDigit)
            ? extension
            : string.Empty;
    }

    // The size is checked while copying rather than from the header, which a client sets.
    private async Task<long> WriteAsync(Stream content, string path, string name, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        long total = 0;

        await using var file = File.Create(path);

        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }

            total += read;
            if (total > options.Value.MaxBytes)
            {
                throw new PropertyTourException(
                    $"{name} is larger than {options.Value.MaxBytes / (1024 * 1024)} MB.");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void Remove(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
