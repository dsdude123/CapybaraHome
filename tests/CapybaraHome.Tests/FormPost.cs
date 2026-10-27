using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapybaraHome.Tests;

/// <summary>Posts a statically rendered page's form the way a browser with no JavaScript does.</summary>
public static class FormPost
{
    /// <summary>A client that reports a redirect rather than following it.</summary>
    public static HttpClient Client(AppFactory app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Posts <paramref name="fields"/> to the named form on <paramref name="path"/>.</summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string handler,
        Dictionary<string, string>? fields = null)
    {
        var form = fields ?? [];
        form["_handler"] = handler;
        form["__RequestVerificationToken"] = Hidden(await client.GetStringAsync(path), "__RequestVerificationToken");

        return await client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    /// <summary>Reads a hidden field's value out of a rendered page.</summary>
    public static string Hidden(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*?value=\"([^\"]*)\"");

        Assert.True(match.Success, $"No hidden field named {name} in the page.");
        return match.Groups[1].Value;
    }
}
