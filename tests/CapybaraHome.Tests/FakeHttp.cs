using System.Net;
using System.Text;

namespace CapybaraHome.Tests;

/// <summary>An <see cref="HttpClient"/> that answers from a queue of canned replies and records what it was asked.</summary>
public sealed class FakeHttp
{
    private readonly Queue<(HttpStatusCode Status, string Body)> replies = new();

    /// <summary>The requests the client has sent, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// The bodies those requests carried, in the same order, with no body as an empty string.
    /// They are read here because the caller disposes the request as soon as it has the answer.
    /// </summary>
    public List<string> Bodies { get; } = [];

    /// <summary>The header value on the last request, if there was one.</summary>
    public string? LastAuthorization => Requests.LastOrDefault()?.Headers.Authorization?.ToString();

    /// <summary>Queues a reply. They are handed out in the order they were added, the last one repeating.</summary>
    public FakeHttp Reply(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        replies.Enqueue((status, body));
        return this;
    }

    /// <summary>Builds a client that answers from the queue.</summary>
    public HttpClient Client() => new(Handler());

    /// <summary>The handler itself, for a host that builds its own clients.</summary>
    public HttpMessageHandler Handler() => new FakeHandler(this);

    private sealed class FakeHandler(FakeHttp owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Requests.Add(request);
            owner.Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);

            var (status, body) = owner.replies.Count > 1 ? owner.replies.Dequeue() : owner.replies.Peek();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
