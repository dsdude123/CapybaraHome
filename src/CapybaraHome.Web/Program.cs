using CapybaraHome.Application;
using CapybaraHome.Infrastructure;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("CapybaraHome")
    ?? throw new InvalidOperationException("Connection string 'CapybaraHome' is not configured."));

var app = builder.Build();

await ApplyMigrationsAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Brings the database up to the latest migration at startup.</summary>
static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
    await context.Database.MigrateAsync();
}

/// <summary>Entry point marker, exposed so the test project can reference the host.</summary>
public partial class Program;
