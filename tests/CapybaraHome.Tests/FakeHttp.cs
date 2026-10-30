using System.Net;
using System.Text;

namespace CapybaraHome.Tests;

/// <summary>An <see cref="HttpClient"/> that answers from a queue of canned replies and records what it was asked.</summary>
public sealed class FakeHttp
{
    private readonly Queue<(HttpStatusCode Status, string Body)> replies = new();
    private readonly List<(string Match, HttpStatusCode Status, string Body)> routes = [];

    /// <summary>The requests the client has sent, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The body of each request, copied as it was sent because the content is disposed after.</summary>
    public List<byte[]> Bodies { get; } = [];

    /// <summary>The header value on the last request, if there was one.</summary>
    public string? LastAuthorization => Requests.LastOrDefault()?.Headers.Authorization?.ToString();

    /// <summary>Queues a reply. They are handed out in the order they were added, the last one repeating.</summary>
    public FakeHttp Reply(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        replies.Enqueue((status, body));
        return this;
    }

    /// <summary>
    /// Queues a reply for the requests whose address contains <paramref name="match"/>. Once a
    /// route is registered the queue is only reached by a request no route matches, which lets a
    /// test that reads several addresses say which answer belongs to which.
    /// </summary>
    public FakeHttp ReplyTo(string match, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        routes.Add((match, status, body));
        return this;
    }

    /// <summary>Builds a client that answers from the queue.</summary>
    public HttpClient Client() => new(Handler());

    /// <summary>The handler itself, for a host that builds its own clients.</summary>
    public HttpMessageHandler Handler() => new FakeHandler(this);

    private sealed class FakeHandler(FakeHttp owner) : HttpMessageHandler
    {
        /// <summary>
        /// Picks the answer: the matching route, then the queue, and failing both a refusal, so a
        /// request a test did not plan for is answered rather than throwing out of the handler.
        /// </summary>
        private static (HttpStatusCode Status, string Body) Answer(FakeHttp owner, (string? Match, HttpStatusCode Status, string Body) route)
        {
            if (route.Match is not null)
            {
                return (route.Status, route.Body);
            }

            if (owner.replies.Count == 0)
            {
                return (HttpStatusCode.ServiceUnavailable, string.Empty);
            }

            return owner.replies.Count > 1 ? owner.replies.Dequeue() : owner.replies.Peek();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Requests.Add(request);
            owner.Bodies.Add(request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken));

            var url = request.RequestUri?.ToString() ?? string.Empty;
            var route = owner.routes.FirstOrDefault(r => url.Contains(r.Match, StringComparison.OrdinalIgnoreCase));

            var (status, body) = Answer(owner, route);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
