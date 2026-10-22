namespace CapybaraHome.Application.Jira;

/// <summary>
/// Where to reach a JIRA Cloud site and who to reach it as. Held only for the length of an
/// import: the token is never written to the database, a log or a file.
/// </summary>
/// <param name="BaseUri">Root of the site, such as <c>https://example.atlassian.net/</c>.</param>
/// <param name="Email">Address of the Atlassian account the API token belongs to.</param>
/// <param name="ApiToken">An Atlassian API token for that account.</param>
public record JiraConnection(Uri BaseUri, string Email, string ApiToken)
{
    /// <summary>Reads what was typed into the wizard, filling in the scheme if it was left out.</summary>
    /// <param name="siteUrl">Site address, with or without a scheme or a trailing path.</param>
    /// <param name="email">Account address.</param>
    /// <param name="apiToken">API token for that account.</param>
    /// <exception cref="ArgumentException">A field is empty or the site address is not an https URL.</exception>
    public static JiraConnection Create(string? siteUrl, string? email, string? apiToken)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            throw new ArgumentException("Give the address of your JIRA site.", nameof(siteUrl));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Give the email address of your Atlassian account.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ArgumentException("Give an API token.", nameof(apiToken));
        }

        var typed = siteUrl.Trim();
        if (!typed.Contains("://", StringComparison.Ordinal))
        {
            typed = "https://" + typed;
        }

        if (!Uri.TryCreate(typed, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
        {
            throw new ArgumentException($"'{siteUrl}' is not a site address.", nameof(siteUrl));
        }

        if (parsed.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The site address has to be https.", nameof(siteUrl));
        }

        return new JiraConnection(new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/"), email.Trim(), apiToken.Trim());
    }
}

/// <summary>The account an API token turned out to belong to.</summary>
/// <param name="DisplayName">Name the site shows for the account.</param>
/// <param name="Email">Address on the account, if the site discloses it.</param>
public record JiraAccount(string DisplayName, string? Email);

/// <summary>A project the account can see.</summary>
/// <param name="Key">Project key, such as <c>ABC</c>.</param>
/// <param name="Name">Project name.</param>
public record JiraProjectSummary(string Key, string Name);
