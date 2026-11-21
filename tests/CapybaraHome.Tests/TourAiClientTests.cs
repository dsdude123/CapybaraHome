using System.Net;
using CapybaraHome.Application.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the calls to the model service and how each way of failing is worded.</summary>
public class TourAiClientTests : IDisposable
{
    private static readonly TourSettingsView Settings = new("http://model.test/v1", "vision", "whisper", 60);
    private readonly string audio = Path.Combine(Path.GetTempPath(), $"capybarahome-audio-{Guid.NewGuid():N}.wav");

    public TourAiClientTests() => File.WriteAllText(audio, "RIFF");

    [Fact]
    public async Task A_question_without_a_system_prompt_or_a_picture_is_one_plain_message()
    {
        var http = new FakeHttp().Reply(TourHarness.Chat("<think>hm</think> The answer. "));

        var answer = await new TourAiClient(http.Client()).AskAsync(Settings, null, "A question", null, think: true, "asking");

        Assert.Equal("The answer.", answer);
        var body = System.Text.Json.JsonDocument.Parse(http.Bodies[0]).RootElement;
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("A question", message.GetProperty("content").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":null}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":"   "}}]}""")]
    [InlineData("""{"choices":[{"message":{}}]}""")]
    [InlineData("""{"choices":[{}]}""")]
    [InlineData("""{}""")]
    public async Task An_answer_with_no_text_in_it_is_a_failure(string reply)
    {
        var http = new FakeHttp().Reply(reply);

        var failed = await Assert.ThrowsAsync<PropertyTourException>(() =>
            new TourAiClient(http.Client()).AskAsync(Settings, "system", "A question", null, think: false, "asking"));

        Assert.Contains("answered with nothing while asking", failed.Message);
    }

    [Fact]
    public async Task A_transcript_is_read_and_one_that_is_missing_is_a_failure()
    {
        var http = new FakeHttp().Reply("""{"text":" Spoken words. "}""").Reply("""{"result":"none"}""");
        var client = new TourAiClient(http.Client());

        Assert.Equal("Spoken words.", await client.TranscribeAsync(Settings, audio));

        var failed = await Assert.ThrowsAsync<PropertyTourException>(() => client.TranscribeAsync(Settings, audio));
        Assert.Contains("without a transcript", failed.Message);
    }

    [Fact]
    public async Task A_refusal_an_unreadable_answer_and_an_unreachable_service_each_say_which()
    {
        var refused = await Assert.ThrowsAsync<PropertyTourException>(() =>
            new TourAiClient(new FakeHttp().Reply(new string('e', 500), HttpStatusCode.BadGateway).Client())
                .TranscribeAsync(Settings, audio));
        Assert.Contains("answered 502 while transcribing the video: " + new string('e', 300), refused.Message);
        Assert.DoesNotContain(new string('e', 301), refused.Message);

        var unreadable = await Assert.ThrowsAsync<PropertyTourException>(() =>
            new TourAiClient(new FakeHttp().Reply("<html>").Client()).TranscribeAsync(Settings, audio));
        Assert.Contains("something unreadable", unreadable.Message);

        var unreachable = await Assert.ThrowsAsync<PropertyTourException>(() =>
            new TourAiClient(new HttpClient(new Failing(new HttpRequestException("Connection refused"))))
                .TranscribeAsync(Settings, audio));
        Assert.Contains("could not be reached while transcribing the video: Connection refused", unreachable.Message);
    }

    [Fact]
    public async Task A_call_that_outlasts_the_timeout_says_so_and_a_cancelled_one_is_just_cancelled()
    {
        var slow = new TourAiClient(new HttpClient(new Failing(null)));

        var timedOut = await Assert.ThrowsAsync<PropertyTourException>(() =>
            slow.AskAsync(Settings with { TimeoutSeconds = 1 }, null, "A question", null, think: true, "asking"));
        Assert.Contains("did not answer within 1 seconds while asking", timedOut.Message);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            slow.AskAsync(Settings, null, "A question", null, think: true, "asking", cancelled.Token));
    }

    public void Dispose() => File.Delete(audio);

    /// <summary>Throws what it was given, or waits until it is cancelled.</summary>
    private sealed class Failing(Exception? error) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (error is not null)
            {
                throw error;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
