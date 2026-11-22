using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// Calls the OpenAI-compatible service that reads a tour: one call to transcribe sound, and chat
/// calls that may carry a picture. The service is slow, so each call is given the timeout from the
/// settings rather than the client's own.
/// </summary>
public class TourAiClient(HttpClient http)
{
    /// <summary>Returns what is said in a sound file.</summary>
    /// <param name="settings">The service to call.</param>
    /// <param name="audioPath">A WAV file on disk.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyTourException">The service did not answer with a transcript.</exception>
    public virtual async Task<string> TranscribeAsync(
        TourSettingsView settings,
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        await using var audio = File.OpenRead(audioPath);
        using var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        using var form = new MultipartFormDataContent
        {
            { file, "file", "audio.wav" },
            { new StringContent(settings.TranscriptionModel ?? string.Empty), "model" },
            { new StringContent("json"), "response_format" },
        };

        using var document = await SendAsync(settings, "audio/transcriptions", form, "transcribing the video", cancellationToken);

        return document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()!.Trim()
            : throw new PropertyTourException("The transcription service answered without a transcript.");
    }

    /// <summary>Asks the model a question, with a picture when one is given, and returns its answer.</summary>
    /// <param name="settings">The service to call.</param>
    /// <param name="system">Who the model is, or null for none.</param>
    /// <param name="prompt">What it is asked.</param>
    /// <param name="jpeg">A picture to look at, or null.</param>
    /// <param name="think">Whether the model may reason before answering.</param>
    /// <param name="doing">What the call is for, named when it fails.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="PropertyTourException">The service did not answer with any text.</exception>
    public virtual async Task<string> AskAsync(
        TourSettingsView settings,
        string? system,
        string prompt,
        byte[]? jpeg,
        bool think,
        string doing,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<object>();
        if (system is not null)
        {
            messages.Add(new { role = "system", content = system });
        }

        messages.Add(jpeg is null
            ? new { role = "user", content = (object)prompt }
            : new
            {
                role = "user",
                content = (object)new object[]
                {
                    new { type = "text", text = prompt },
                    new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg) } },
                },
            });

        var body = new Dictionary<string, object>
        {
            ["model"] = settings.Model ?? string.Empty,
            ["messages"] = messages,
            ["temperature"] = 0.2,
            ["stream"] = false,
        };

        if (!think)
        {
            body["chat_template_kwargs"] = new { enable_thinking = false };
        }

        using var content = JsonContent.Create(body);
        using var document = await SendAsync(settings, "chat/completions", content, doing, cancellationToken);

        if (document.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var answer)
            && answer.ValueKind == JsonValueKind.String
            && TourAnalysis.Clean(answer.GetString()) is { Length: > 0 } text)
        {
            return text;
        }

        throw new PropertyTourException($"The model answered with nothing while {doing}.");
    }

    private async Task<JsonDocument> SendAsync(
        TourSettingsView settings,
        string path,
        HttpContent content,
        string doing,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            using var response = await http.PostAsync($"{settings.BaseUrl}/{path}", content, limit.Token);
            var body = await response.Content.ReadAsStringAsync(limit.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new PropertyTourException(
                    $"The model service answered {(int)response.StatusCode} while {doing}: {Short(body)}");
            }

            return JsonDocument.Parse(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PropertyTourException(
                $"The model service did not answer within {settings.TimeoutSeconds} seconds while {doing}.");
        }
        catch (HttpRequestException ex)
        {
            throw new PropertyTourException($"The model service could not be reached while {doing}: {ex.Message}");
        }
        catch (JsonException)
        {
            throw new PropertyTourException($"The model service answered with something unreadable while {doing}.");
        }
    }

    private static string Short(string body)
    {
        var text = body.Trim();
        return text.Length <= 300 ? text : text[..300];
    }
}
