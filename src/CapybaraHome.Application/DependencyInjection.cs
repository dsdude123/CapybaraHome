using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Application;

/// <summary>Registers the application services.</summary>
public static class DependencyInjection
{
    /// <summary>Adds the handlers and validators this layer provides.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
