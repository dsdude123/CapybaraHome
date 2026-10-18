using System.Text.Json.Serialization;
using CapybaraHome.Application;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Infrastructure;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web.Auth;
using CapybaraHome.Web.Components;
using CapybaraHome.Web.Endpoints;
using CapybaraHome.Web.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// Statuses travel as names, so the API and the export read the same way.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("CapybaraHome")
    ?? throw new InvalidOperationException("Connection string 'CapybaraHome' is not configured."));

builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.AddHostedService<ScheduledBackupService>();

builder.Services.AddCapybaraAuthentication(builder.Configuration, out var oidc);
builder.Services.AddSingleton(oidc);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<TaskTools>();

var app = builder.Build();

await PrepareDatabaseAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// The not-found page is for people. An API or connector call gets its own status code back
// rather than having a page re-executed over it, which would turn a 401 into a sign-in redirect.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapApi();
app.MapMcp("/mcp").RequireAuthorization(AuthenticationSetup.ApiPolicyName);

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapGet("/sign-in", (HttpContext http, string? returnUrl) =>
{
    if (!oidc.IsConfigured)
    {
        return Results.Redirect("/access-denied?reason=not-configured");
    }

    var target = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl;
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = target },
        [OpenIdConnectDefaults.AuthenticationScheme]);
}).AllowAnonymous();

app.MapPost("/sign-out", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.Run();

/// <summary>Brings the database up to the latest migration and seeds a usable dashboard.</summary>
static async Task PrepareDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
    await context.Database.MigrateAsync();

    var dashboard = scope.ServiceProvider.GetRequiredService<DashboardService>();
    await dashboard.SeedDefaultsAsync();
}

/// <summary>Entry point marker, exposed so the test project can reference the host.</summary>
public partial class Program;
