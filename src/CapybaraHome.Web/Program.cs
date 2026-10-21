using System.Text.Json.Serialization;
using CapybaraHome.Application;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Users;
using CapybaraHome.Infrastructure;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web.Auth;
using CapybaraHome.Web.Components;
using CapybaraHome.Web.Endpoints;
using CapybaraHome.Web.Mcp;
using Microsoft.AspNetCore.DataProtection;
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

builder.Services.AddCapybaraAuthentication();
builder.Services.AddCapybaraForwardedHeaders(builder.Configuration);

// The keys that encrypt the sign-in cookie live with the data, so a new container reads the
// cookies the old one wrote instead of signing everybody out on every image pull.
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("CapybaraHome")
        .PersistKeysToFileSystem(Directory.CreateDirectory(keyPath));
}

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<TaskTools>();

var app = builder.Build();

await PrepareDatabaseAsync(app);

app.UseForwardedHeaders();

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

// Before authorization, so an installation with no accounts sends a browser to setup rather
// than to a sign-in page that no account can get through yet.
app.UseFirstRunSetup();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapApi();
app.MapMcp("/mcp").RequireAuthorization(AuthenticationSetup.ApiPolicyName);

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapPost("/sign-out", async (HttpContext http) =>
{
    await SignInManager.SignOutAsync(http);
    return Results.Redirect("/sign-in");
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
