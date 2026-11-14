using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CapybaraHome.Application;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Homes;
using CapybaraHome.Application.Reports;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Application.Users;
using CapybaraHome.Infrastructure;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web.Auth;
using CapybaraHome.Web.Components;
using CapybaraHome.Web.Endpoints;
using CapybaraHome.Web.Jira;
using CapybaraHome.Web.Mcp;
using CapybaraHome.Web.Reports;
using CapybaraHome.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// A property photo is the largest request the app accepts, so the request body limit follows
// the photo limit with room for the multipart framing around the file.
var photoOptions = builder.Configuration.GetSection(PropertyPhotoOptions.SectionName)
    .Get<PropertyPhotoOptions>() ?? new PropertyPhotoOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = photoOptions.MaxBytes + (1024 * 1024);
    options.Limits.MinRequestBodyDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.MinResponseDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// The MCP tools name the caller in their audit lines, which they read off the request.
builder.Services.AddHttpContextAccessor();

// Statuses travel as names, so the API and the export read the same way.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Rate limiting protects the API and MCP endpoints from abuse.
builder.Services.AddRateLimiter(options =>
{
    // Every request passes the global window as well as its endpoint's, so the global one is the
    // loosest of the three: one page load pulls a page and all of its static assets.
    // UseForwardedHeaders runs first, so the remote address is the real client behind Traefik.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetSlidingWindowLimiter(
            RateLimiting.ClientKey(context),
            _ => RateLimiting.Window(600)));

    options.AddPolicy(RateLimiting.McpPolicy, context => RateLimitPartition.GetSlidingWindowLimiter(
        RateLimiting.ClientKey(context),
        _ => RateLimiting.Window(60)));

    options.AddPolicy(RateLimiting.ApiPolicy, context => RateLimitPartition.GetSlidingWindowLimiter(
        RateLimiting.ClientKey(context),
        _ => RateLimiting.Window(200)));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter =
            ((int)RateLimiting.WindowLength.TotalSeconds).ToString();
        await context.HttpContext.Response.WriteAsync(
            "Rate limit exceeded. Please slow down your requests.", cancellationToken);
    };
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("CapybaraHome")
    ?? throw new InvalidOperationException("Connection string 'CapybaraHome' is not configured."));

builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.Configure<PropertyPhotoOptions>(builder.Configuration.GetSection(PropertyPhotoOptions.SectionName));
builder.Services.AddHostedService<ScheduledBackupService>();
builder.Services.AddHostedService<TaskAutomationRunner>();

builder.Services.AddScoped<DailyReportPrinting>();
builder.Services.AddHttpClient<IppPrinter>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHostedService<DailyReportRunner>();

// The wizard's pages render statically, so what step one learned is held here between posts.
builder.Services.AddSingleton<JiraWizardStore>();

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
    .WithTools<TaskTools>()
    .WithTools<MealTools>()
    .WithTools<HomeSearchTools>()
    .WithTools<GroceryTools>();

var app = builder.Build();

await PrepareDatabaseAsync(app);

app.UseForwardedHeaders();

// Ahead of everything that can answer on its own, so a rejected or redirected response
// carries the headers as well.
app.UseSecurityHeaders();

// Rate limiter middleware must come before authentication and authorization.
app.UseRateLimiter();

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
app.MapSettings();
app.MapHomeSearch();
app.MapReport();
app.MapMcp("/mcp")
    .RequireAuthorization(AuthenticationSetup.ApiPolicyName)
    .RequireRateLimiting(RateLimiting.McpPolicy);

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
