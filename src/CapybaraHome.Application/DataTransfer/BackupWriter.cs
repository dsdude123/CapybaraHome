using Microsoft.Extensions.Options;

namespace CapybaraHome.Application.DataTransfer;

/// <summary>Writes an export to the configured destination and prunes the old files.</summary>
public class BackupWriter(DataTransferService transfer, IOptions<BackupOptions> options, TimeProvider clock)
{
    /// <summary>Writes one backup file and returns its full path.</summary>
    public async Task<string> WriteAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.Destination);

        var json = await transfer.ExportToJsonAsync(cancellationToken);
        var name = $"capybarahome-{clock.GetUtcNow():yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(settings.Destination, name);

        await File.WriteAllTextAsync(path, json, cancellationToken);
        Prune(settings);

        return path;
    }

    private static void Prune(BackupOptions settings)
    {
        if (settings.Keep <= 0)
        {
            return;
        }

        var files = new DirectoryInfo(settings.Destination)
            .GetFiles("capybarahome-*.json")
            .OrderByDescending(f => f.Name)
            .Skip(settings.Keep);

        foreach (var file in files)
        {
            file.Delete();
        }
    }
}
