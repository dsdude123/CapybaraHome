using System.Diagnostics;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// Gets a tour's files into what the model can take, through ffmpeg: the sound of a video as a
/// small WAV file, and a picture as a JPEG no larger than the model needs.
/// </summary>
public class TourMedia
{
    /// <summary>Longest side a picture is sent at, in pixels.</summary>
    private const int LongestSide = 1568;

    /// <summary>
    /// Writes a video's sound to a temporary 16 kHz mono WAV file and returns its path, or null
    /// when the video has no sound. The caller deletes the file.
    /// </summary>
    /// <param name="videoPath">The video on disk.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyTourException">ffmpeg is missing or could not read the video.</exception>
    public virtual async Task<string?> ExtractAudioAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var output = Path.Combine(Path.GetTempPath(), $"capybarahome-tour-{Guid.NewGuid():N}.wav");

        var (code, error) = await RunAsync(
            ["-v", "error", "-y", "-i", videoPath, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output],
            cancellationToken);

        if (code == 0 && File.Exists(output) && new FileInfo(output).Length > 44)
        {
            return output;
        }

        File.Delete(output);

        if (error.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        throw new PropertyTourException($"The sound could not be read from the video: {Tail(error)}");
    }

    /// <summary>
    /// Returns a picture as a JPEG scaled down to what the model needs, or null when ffmpeg could
    /// not convert it.
    /// </summary>
    /// <param name="picturePath">The picture on disk.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public virtual async Task<byte[]?> ShrinkPictureAsync(string picturePath, CancellationToken cancellationToken = default)
    {
        var output = Path.Combine(Path.GetTempPath(), $"capybarahome-tour-{Guid.NewGuid():N}.jpg");

        try
        {
            var scale = $"scale={LongestSide}:{LongestSide}:force_original_aspect_ratio=decrease:force_divisible_by=2";
            var (code, _) = await RunAsync(
                ["-v", "error", "-y", "-i", picturePath, "-vf", scale, "-frames:v", "1", "-q:v", "4", output],
                cancellationToken);

            return code == 0 && File.Exists(output) ? await File.ReadAllBytesAsync(output, cancellationToken) : null;
        }
        catch (PropertyTourException)
        {
            return null;
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static async Task<(int Code, string Error)> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new PropertyTourException("ffmpeg could not be started.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PropertyTourException("ffmpeg is not installed, so the video cannot be read.");
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            await output;
            return (process.ExitCode, await error);
        }
    }

    private static string Tail(string error)
    {
        var text = error.Trim();
        return text.Length <= 300 ? text : text[^300..];
    }
}
