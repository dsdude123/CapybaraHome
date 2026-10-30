using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// The search's shared setup: where the drives are measured to, what a home has to do, and the
/// weights that turn all of it into a score.
/// </summary>
public class HomeSearchService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>Returns the destinations, in order.</summary>
    public async Task<IReadOnlyList<SearchDestinationView>> ListDestinationsAsync(CancellationToken cancellationToken = default)
    {
        return await context.SearchDestinations.AsNoTracking()
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Id)
            .Select(d => new SearchDestinationView(d.Id, d.Name, d.Address, d.Weight, d.Latitude, d.Longitude, d.SortOrder))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns one destination, or null when none has that id.</summary>
    public async Task<SearchDestinationView?> GetDestinationAsync(int id, CancellationToken cancellationToken = default)
    {
        return (await ListDestinationsAsync(cancellationToken)).FirstOrDefault(d => d.Id == id);
    }

    /// <summary>Adds a destination and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchDestinationView> CreateDestinationAsync(
        SearchDestinationRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = Required(request.Name, "A destination needs a name.", nameof(request));

        var last = await context.SearchDestinations.AsNoTracking()
            .OrderByDescending(d => d.SortOrder)
            .Select(d => (int?)d.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var destination = new SearchDestination
        {
            Name = name,
            Address = Blank(request.Address),
            Weight = request.Weight,
            SortOrder = (last ?? -1) + 1,
        };

        context.SearchDestinations.Add(destination);
        await context.SaveChangesAsync(cancellationToken);

        return new SearchDestinationView(
            destination.Id, destination.Name, destination.Address, destination.Weight,
            destination.Latitude, destination.Longitude, destination.SortOrder);
    }

    /// <summary>Renames a destination, or returns null when none has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchDestinationView?> UpdateDestinationAsync(
        int id,
        SearchDestinationRequest request,
        CancellationToken cancellationToken = default)
    {
        var destination = await context.SearchDestinations.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (destination is null)
        {
            return null;
        }

        var address = Blank(request.Address);

        // The coordinates belong to the address that was looked up, so a new address drops them
        // rather than leaving the old pin behind under a new name.
        if (!string.Equals(address, destination.Address, StringComparison.Ordinal))
        {
            destination.Latitude = null;
            destination.Longitude = null;
        }

        destination.Name = Required(request.Name, "A destination needs a name.", nameof(request));
        destination.Address = address;
        destination.Weight = request.Weight;

        await context.SaveChangesAsync(cancellationToken);

        return new SearchDestinationView(
            destination.Id, destination.Name, destination.Address, destination.Weight,
            destination.Latitude, destination.Longitude, destination.SortOrder);
    }

    /// <summary>Records where a destination is. Returns false when none has that id.</summary>
    public async Task<bool> PlaceDestinationAsync(
        int id,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var destination = await context.SearchDestinations.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (destination is null)
        {
            return false;
        }

        destination.Latitude = latitude;
        destination.Longitude = longitude;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Deletes a destination and the drive times to it. Returns false when none has that id.</summary>
    public async Task<bool> DeleteDestinationAsync(int id, CancellationToken cancellationToken = default)
    {
        var destination = await context.SearchDestinations.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (destination is null)
        {
            return false;
        }

        context.SearchDestinations.Remove(destination);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns the priorities, in order, with how many properties meet each one.</summary>
    public async Task<IReadOnlyList<SearchPriorityView>> ListPrioritiesAsync(CancellationToken cancellationToken = default)
    {
        return await context.SearchPriorities.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Select(p => new SearchPriorityView(
                p.Id,
                p.Name,
                p.Points,
                p.SortOrder,
                p.Properties.Count(m => m.Meets)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Adds a priority and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchPriorityView> CreatePriorityAsync(
        SearchPriorityRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = Required(request.Name, "A priority needs a name.", nameof(request));

        var last = await context.SearchPriorities.AsNoTracking()
            .OrderByDescending(p => p.SortOrder)
            .Select(p => (int?)p.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var priority = new SearchPriority
        {
            Name = name,
            Points = request.Points,
            SortOrder = (last ?? -1) + 1,
        };

        context.SearchPriorities.Add(priority);
        await context.SaveChangesAsync(cancellationToken);

        return new SearchPriorityView(priority.Id, priority.Name, priority.Points, priority.SortOrder, 0);
    }

    /// <summary>Changes a priority, or returns null when none has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchPriorityView?> UpdatePriorityAsync(
        int id,
        SearchPriorityRequest request,
        CancellationToken cancellationToken = default)
    {
        var priority = await context.SearchPriorities.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (priority is null)
        {
            return null;
        }

        priority.Name = Required(request.Name, "A priority needs a name.", nameof(request));
        priority.Points = request.Points;
        await context.SaveChangesAsync(cancellationToken);

        return (await ListPrioritiesAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Deletes a priority and the markings against it. Returns false when none has that id.</summary>
    public async Task<bool> DeletePriorityAsync(int id, CancellationToken cancellationToken = default)
    {
        var priority = await context.SearchPriorities.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (priority is null)
        {
            return false;
        }

        context.SearchPriorities.Remove(priority);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns the weights and the routing service, with every weight at zero until they are set.</summary>
    public async Task<HomeSearchSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var held = await context.HomeSearchSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return View(held ?? new HomeSearchSettings());
    }

    /// <summary>Applies the set members of <paramref name="request"/> and returns the settings as saved.</summary>
    public async Task<HomeSearchSettingsView> SaveSettingsAsync(
        HomeSearchSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = await context.HomeSearchSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new HomeSearchSettings();
            context.HomeSearchSettings.Add(settings);
        }

        settings.MonthlyFeePointsPerDollar = request.MonthlyFeePointsPerDollar ?? settings.MonthlyFeePointsPerDollar;
        settings.DepositPointsPerDollar = request.DepositPointsPerDollar ?? settings.DepositPointsPerDollar;
        settings.DriveTimePointsPerMinute = request.DriveTimePointsPerMinute ?? settings.DriveTimePointsPerMinute;
        settings.AmenityPoints = request.AmenityPoints ?? settings.AmenityPoints;
        settings.ProPoints = request.ProPoints ?? settings.ProPoints;
        settings.ConPoints = request.ConPoints ?? settings.ConPoints;

        if (request.RoutingBaseUrl is not null)
        {
            settings.RoutingBaseUrl = Blank(request.RoutingBaseUrl);
        }

        // An empty box means the key held should stay, since the page never renders it back; the
        // settings screen offers its own button to forget it.
        if (!string.IsNullOrWhiteSpace(request.RoutingApiKey))
        {
            settings.RoutingApiKey = request.RoutingApiKey.Trim();
        }

        settings.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return View(settings);
    }

    /// <summary>Forgets the routing key, so the app stops calling the service.</summary>
    public async Task ForgetRoutingKeyAsync(CancellationToken cancellationToken = default)
    {
        var settings = await context.HomeSearchSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings?.RoutingApiKey is null)
        {
            return;
        }

        settings.RoutingApiKey = null;
        settings.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Returns the routing key, which only the routing client reads.</summary>
    public Task<string?> GetRoutingKeyAsync(CancellationToken cancellationToken = default)
    {
        return context.HomeSearchSettings.AsNoTracking()
            .Select(s => s.RoutingApiKey)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static HomeSearchSettingsView View(HomeSearchSettings settings) => new(
        settings.MonthlyFeePointsPerDollar,
        settings.DepositPointsPerDollar,
        settings.DriveTimePointsPerMinute,
        settings.AmenityPoints,
        settings.ProPoints,
        settings.ConPoints,
        settings.RoutingBaseUrl,
        !string.IsNullOrWhiteSpace(settings.RoutingApiKey));

    private static string Required(string? value, string message, string parameter)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > 0 ? trimmed : throw new ArgumentException(message, parameter);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
