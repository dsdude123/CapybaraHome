using System.Diagnostics;
using CapybaraHome.Application.Homes;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the calls to ffmpeg. Where ffmpeg is installed they are made for real; where it is not,
/// the same calls have to say so rather than throw something unreadable.
/// </summary>
public class TourMediaTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("capybarahome-media-").FullName;

    [Fact]
    public async Task The_sound_of_a_video_comes_out_as_a_wav_file_or_ffmpeg_is_named_as_missing()
    {
        var media = new TourMedia();

        if (!Installed())
        {
            var missing = await Assert.ThrowsAsync<PropertyTourException>(() => media.ExtractAudioAsync("no-such.mp4"));
            Assert.Contains("ffmpeg is not installed", missing.Message);
            Assert.Null(await media.ShrinkPictureAsync("no-such.jpg"));
            return;
        }

        var video = Path.Combine(directory, "spoken.mp4");
        var silent = Path.Combine(directory, "silent.mp4");
        var picture = Path.Combine(directory, "large.png");
        Make("-f", "lavfi", "-i", "testsrc=size=320x240:rate=5", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "1", "-c:v", "mpeg4", "-c:a", "aac", video);
        Make("-f", "lavfi", "-i", "testsrc=size=320x240:rate=5", "-t", "1", "-c:v", "mpeg4", silent);
        Make("-f", "lavfi", "-i", "testsrc=size=3000x2000:rate=1", "-frames:v", "1", picture);

        var audio = await media.ExtractAudioAsync(video);
        Assert.NotNull(audio);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString((await File.ReadAllBytesAsync(audio))[..4]));
        File.Delete(audio);

        Assert.Null(await media.ExtractAudioAsync(silent));

        var broken = Path.Combine(directory, "broken.mp4");
        await File.WriteAllTextAsync(broken, "not a video");
        var unreadable = await Assert.ThrowsAsync<PropertyTourException>(() => media.ExtractAudioAsync(broken));
        Assert.Contains("could not be read from the video", unreadable.Message);

        var small = await media.ShrinkPictureAsync(picture);
        Assert.NotNull(small);
        Assert.Equal([0xFF, 0xD8], small[..2]);
        var shrunk = Path.Combine(directory, "small.jpg");
        await File.WriteAllBytesAsync(shrunk, small);
        Assert.StartsWith("1568,104", Size(shrunk));

        Assert.Null(await media.ShrinkPictureAsync(broken));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => media.ExtractAudioAsync(video, cancelled.Token));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static bool Installed()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            probe!.WaitForExit();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string Size(string picture)
    {
        var start = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", picture })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var size = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return size;
    }

    private static void Make(params string[] arguments)
    {
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-y" }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
