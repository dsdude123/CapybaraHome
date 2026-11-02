using System.Linq;
using System.Text.Json.Serialization;
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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Kestrel request size limits protect against oversized payloads on all endpoints.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
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

// Statuses travel as names, so the API and the export read the same way.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Rate limiting protects the API and MCP endpoints from abuse.
builder.Services.AddRateLimiter(options =>
{
    // Global per-IP limiter: 120 requests per minute, sliding window.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString()
                ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "unknown",
            factory => new SlidingWindowRateLimiterOptions
            {
                AutoRecovery = true,
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4 // granular buckets
            }));

    // Stricter policy for the MCP endpoint: 60 requests per minute.
    options.AddPolicy("mcp", rateLimitOptions =>
    {
        rateLimitOptions.PermitPartition()
            .OnRejected(async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsync(
                    "MCP rate limit exceeded. Please slow down your requests.");
                return Task.CompletedTask;
            });
    });

    // API endpoints get a slightly higher limit: 200 requests per minute.
    options.AddPolicy("api", rateLimitOptions =>
    {
        rateLimitOptions.PermitPartition()
            .OnRejected(async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsync(
                    "API rate limit exceeded. Please slow down your requests.");
                return Task.CompletedTask;
            });
    });

    options.RejectionStatusCode = 429;
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

// Security headers on every response.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
    await next();
});

app.MapApi().WithRateLimitPolicy("api");
app.MapSettings();
app.MapHomeSearch();
app.MapReport();
app.MapMcp("/mcp")
    .RequireAuthorization(AuthenticationSetup.ApiPolicyName)
    .WithRateLimitPolicy("mcp");

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
