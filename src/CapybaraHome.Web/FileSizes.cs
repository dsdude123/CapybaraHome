namespace CapybaraHome.Web;

/// <summary>Writes a file's size the way the pages show it.</summary>
public static class FileSizes
{
    /// <summary>Returns <paramref name="bytes"/> in the largest unit that keeps it above one.</summary>
    public static string Label(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} bytes",
    };
}
