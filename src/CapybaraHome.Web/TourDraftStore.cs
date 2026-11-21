using System.Collections.Concurrent;

namespace CapybaraHome.Web;

/// <summary>
/// Holds the notes of a tour whose submission was refused, so the tour page can show them again
/// instead of losing what was typed. The upload posts to an endpoint rather than to the page, so
/// a ticket travels back in the address and the words stay here, in memory, for half an hour.
/// </summary>
public sealed class TourDraftStore(TimeProvider clock)
{
    /// <summary>How long refused notes are kept.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, (string Notes, DateTimeOffset ExpiresAt)> drafts = new();

    /// <summary>Keeps <paramref name="notes"/> and returns the ticket they are read back with, or null when there is nothing to keep.</summary>
    public string? Keep(string? notes)
    {
        var now = clock.GetUtcNow();
        foreach (var expired in drafts.Where(d => d.Value.ExpiresAt <= now).Select(d => d.Key).ToList())
        {
            drafts.TryRemove(expired, out _);
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var ticket = Guid.NewGuid().ToString("N");
        drafts[ticket] = (notes, now + Lifetime);
        return ticket;
    }

    /// <summary>Returns the notes kept under <paramref name="ticket"/>, or null when there are none or they have expired.</summary>
    public string? Read(string? ticket) =>
        ticket is not null && drafts.TryGetValue(ticket, out var draft) && draft.ExpiresAt > clock.GetUtcNow()
            ? draft.Notes
            : null;
}
