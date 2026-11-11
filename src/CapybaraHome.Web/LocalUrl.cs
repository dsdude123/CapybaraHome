namespace CapybaraHome.Web;

/// <summary>Checks the return addresses the editors carry through their posts.</summary>
public static class LocalUrl
{
    /// <summary>
    /// Returns <paramref name="value"/> when it addresses a page of this app, or null when it does
    /// not, so a crafted return address cannot send anyone off the site.
    /// </summary>
    public static string? Within(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();

        if (candidate.StartsWith('/') || candidate.StartsWith('#')
            || candidate.IndexOfAny(['\\', '\n', '\r', '\t']) >= 0
            || !Uri.IsWellFormedUriString(candidate, UriKind.Relative))
        {
            return null;
        }

        // A relative URI may still carry a scheme, as "javascript:alert(1)" does, so the part
        // before the first slash, query or fragment must not hold a colon.
        return candidate.Split('/', '?', '#')[0].Contains(':') ? null : candidate;
    }
}
