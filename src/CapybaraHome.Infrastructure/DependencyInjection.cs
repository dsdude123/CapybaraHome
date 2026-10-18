using CapybaraHome.Application.Abstractions;
using CapybaraHome.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Infrastructure;

/// <summary>Registers the infrastructure services.</summary>
public static class DependencyInjection
{
    /// <summary>Adds the database context, backed by SQLite at <paramref name="connectionString"/>.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CapybaraHomeDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ICapybaraHomeDbContext>(provider => provider.GetRequiredService<CapybaraHomeDbContext>());
        return services;
    }
}
