using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Calls the URL a webhook operation names. The response body is not kept: only the status code
/// is recorded, so a target's answer cannot drag its own content into the database.
/// </summary>
public class TaskWebhookSender(HttpClient client)
{
    /// <summary>How long a call is given before it counts as failed.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Resolves a host and connects only to an address <see cref="WebhookAddress"/> allows, which
    /// is checked here rather than on the typed URL alone so a name cannot be re-pointed at a
    /// refused address between being saved and being called.
    /// </summary>
    /// <param name="context">The endpoint the handler wants a socket to.</param>
    /// <param name="cancellationToken">Token that cancels the connection attempt.</param>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);

        var allowed = addresses.Where(WebhookAddress.IsAllowed).ToArray();
        if (allowed.Length == 0)
        {
            throw new HttpRequestException($"{endPoint.Host} resolves to an address that cannot be called.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(allowed, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Makes the call and returns whether the target accepted it, what to record about it, and
    /// whether trying again could ever help. A refusal the target meant is not retried; a
    /// timeout, a connection failure or a 5xx is.
    /// </summary>
    /// <param name="url">URL to call.</param>
    /// <param name="method">Method to send it with.</param>
    /// <param name="payload">What a POST sends as its JSON body.</param>
    /// <param name="cancellationToken">Token that cancels the call.</param>
    public async Task<WebhookResult> SendAsync(
        string url,
        Domain.Tasks.TaskWebhookMethod method,
        WebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (WebhookAddress.Validate(url, out var uri) is { } problem)
        {
            return new WebhookResult(false, problem, Retryable: false);
        }

        using var request = new HttpRequestMessage(
            method == Domain.Tasks.TaskWebhookMethod.Get ? HttpMethod.Get : HttpMethod.Post,
            uri);

        if (method == Domain.Tasks.TaskWebhookMethod.Post)
        {
            var json = JsonSerializer.Serialize(payload, PayloadOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return new WebhookResult(true, $"Called {uri!.Host} and it answered {code}.", Retryable: false);
            }

            // A redirect is not followed: the whole point of checking the address is lost if the
            // target can send the call somewhere else.
            var retryable = code >= 500 || code == 408 || code == 429;
            return new WebhookResult(false, $"{uri!.Host} answered {code} {response.ReasonPhrase}.", retryable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookResult(false, $"{uri!.Host} did not answer within {Timeout.TotalSeconds:0} seconds.", Retryable: true);
        }
        catch (HttpRequestException ex)
        {
            return new WebhookResult(false, ex.Message, Retryable: true);
        }
    }

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>How one webhook call went.</summary>
/// <param name="Succeeded">Whether the target accepted it.</param>
/// <param name="Detail">What to record about it.</param>
/// <param name="Retryable">Whether trying again could help.</param>
public record WebhookResult(bool Succeeded, string Detail, bool Retryable);

/// <summary>The JSON body a webhook posts.</summary>
/// <param name="Event">What happened, always <c>task.completed</c>.</param>
/// <param name="TaskId">Id of the completed task.</param>
/// <param name="Title">Its title.</param>
/// <param name="Notes">Its notes, held as markdown, or null.</param>
/// <param name="Category">Name of its category, or null.</param>
/// <param name="DueDate">Day it was due, or null.</param>
/// <param name="CompletedAt">When it was completed, in UTC.</param>
public record WebhookPayload(
    string Event,
    int TaskId,
    string Title,
    string? Notes,
    string? Category,
    DateOnly? DueDate,
    DateTimeOffset CompletedAt);
