using Microsoft.AspNetCore.HttpOverrides;

namespace CapybaraHome.Web.Auth;

/// <summary>
/// Trusts the forwarded headers of a reverse proxy, which is how the app knows a request arrived
/// over HTTPS when something upstream terminated it. Without this the sign-in redirect and the
/// cookie are built from the scheme Kestrel saw, which is plain HTTP behind a proxy.
/// </summary>
public static class ForwardedHeadersSetup
{
    /// <summary>Configuration section the settings are bound from.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Configures which headers to read and which proxies to trust.</summary>
    public static IServiceCollection AddCapybaraForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;

            var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
            var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

            if (proxies.Length == 0 && networks.Length == 0)
            {
                // A container cannot know the proxy's address ahead of time, and the app is not
                // meant to be reachable except through it, so by default any hop is trusted.
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();
                return;
            }

            foreach (var proxy in proxies.Where(p => System.Net.IPAddress.TryParse(p, out _)))
            {
                options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            }

            foreach (var network in networks)
            {
                var parts = network.Split('/');
                if (parts.Length == 2
                    && System.Net.IPAddress.TryParse(parts[0], out var prefix)
                    && int.TryParse(parts[1], out var length))
                {
                    options.KnownNetworks.Add(new IPNetwork(prefix, length));
                }
            }
        });

        return services;
    }
}
