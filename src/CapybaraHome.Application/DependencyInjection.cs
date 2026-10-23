using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Jira;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Application;

/// <summary>Registers the application services.</summary>
public static class DependencyInjection
{
    /// <summary>Adds the handlers and validators this layer provides.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddTimeProvider();

        services.AddScoped<TaskService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<MealService>();
        services.AddScoped<MealTagService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<UserService>();
        services.AddScoped<ApiTokenService>();
        services.AddScoped<DataTransferService>();
        services.AddScoped<BackupWriter>();
        services.AddScoped<JiraImportService>();

        // The import pulls hundreds of issues a page at a time, so the client is given longer
        // than the default minute before it gives up on a single page.
        services.AddHttpClient<JiraCloudClient>(client => client.Timeout = TimeSpan.FromMinutes(2));

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(s => s.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
