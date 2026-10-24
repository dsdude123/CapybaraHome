using System.Collections.Concurrent;
using System.Security.Cryptography;
using CapybaraHome.Application.Jira;

namespace CapybaraHome.Web.Jira;

/// <summary>
/// Holds what the JIRA wizard learned in step one while the later steps are posted back. The
/// pages render statically, so there is no circuit to keep this in; a ticket travels in the
/// form instead and the credentials stay here, in memory, until the import ends or the entry
/// expires. Nothing here is written to the database or to disk.
/// </summary>
public sealed class JiraWizardStore(TimeProvider clock)
{
    /// <summary>How long an unfinished wizard is kept before its credentials are dropped.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Entry> entries = new();

    /// <summary>One wizard in progress.</summary>
    /// <param name="Connection">Site and credentials, as typed in step one.</param>
    /// <param name="Account">Account the credentials turned out to belong to.</param>
    /// <param name="Projects">Projects that account can see.</param>
    /// <param name="ExpiresAt">When this entry is dropped.</param>
    public record Entry(
        JiraConnection Connection,
        JiraAccount Account,
        IReadOnlyList<JiraProjectSummary> Projects,
        DateTimeOffset ExpiresAt)
    {
        /// <summary>Project chosen in step two, once one has been.</summary>
        public string? ProjectKey { get; set; }

        /// <summary>The labels found on the chosen project's issues, once they have been read.</summary>
        public IReadOnlyList<string> Labels { get; set; } = [];

        /// <summary>
        /// The issues the wizard already fetched, kept so the step that sorts the labels and the
        /// step that imports read one board rather than fetching it twice. The store disposes it.
        /// </summary>
        public JiraIssueSnapshot? Snapshot { get; set; }
    }

    /// <summary>Keeps a connection and hands back the ticket that finds it again.</summary>
    public string Add(JiraConnection connection, JiraAccount account, IReadOnlyList<JiraProjectSummary> projects)
    {
        Prune();

        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        entries[ticket] = new Entry(connection, account, projects, clock.GetUtcNow() + Lifetime);
        return ticket;
    }

    /// <summary>Finds a live entry, or null when the ticket is unknown or has expired.</summary>
    public Entry? Find(string? ticket)
    {
        Prune();

        if (ticket is null || !entries.TryGetValue(ticket, out var entry))
        {
            return null;
        }

        return entry.ExpiresAt <= clock.GetUtcNow() ? null : entry;
    }

    /// <summary>Drops an entry, and the credentials in it, as soon as the import is done with them.</summary>
    public void Remove(string? ticket)
    {
        if (ticket is not null && entries.TryRemove(ticket, out var entry))
        {
            entry.Snapshot?.Dispose();
        }
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();

        foreach (var (ticket, entry) in entries)
        {
            if (entry.ExpiresAt <= now && entries.TryRemove(ticket, out var dropped))
            {
                dropped.Snapshot?.Dispose();
            }
        }
    }
}
