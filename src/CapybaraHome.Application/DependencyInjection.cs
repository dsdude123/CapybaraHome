using System.Net.Http;
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
        services.AddScoped<TaskAutomationService>();
        services.AddScoped<TaskCloseOperationService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<MealService>();
        services.AddScoped<MealTagService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<UserService>();
        services.AddScoped<ApiTokenService>();
        services.AddScoped<DataTransferService>();
        services.AddScoped<BackupWriter>();
        services.AddScoped<JiraImportService>();
        services.AddScoped<JiraMealImportService>();

        // The import pulls hundreds of issues a page at a time, so the client is given longer
        // than the default minute before it gives up on a single page.
        services.AddHttpClient<JiraCloudClient>(client => client.Timeout = TimeSpan.FromMinutes(2));

        // A webhook goes to a URL the user typed, so redirects are not followed and every
        // connection is checked against the address rules before the socket opens.
        services.AddHttpClient<TaskWebhookSender>(client => client.Timeout = TaskWebhookSender.Timeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = TaskWebhookSender.ConnectAsync,
            });

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
