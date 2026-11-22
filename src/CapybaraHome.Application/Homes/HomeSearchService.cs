using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// The search's shared setup: the scenarios it is scored under, where the drives are measured to,
/// what a home has to do, and the weights that turn all of it into a score. Everything but the
/// routing service and the priorities belongs to the active scenario.
/// </summary>
public class HomeSearchService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>What the first scenario is called, before any is named.</summary>
    public const string DefaultProfileName = "Default";

    /// <summary>Returns the scenarios, in order, the active one marked.</summary>
    public async Task<IReadOnlyList<SearchProfileView>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        await ActiveAsync(cancellationToken);

        return await context.SearchProfiles.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Select(p => new SearchProfileView(
                p.Id,
                p.Name,
                p.IsActive,
                p.SortOrder,
                p.Destinations.Count,
                p.Vehicles.Count,
                p.TollRoads.Count))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns the scenario the pages are showing, making the first one if there is none.</summary>
    public async Task<SearchProfileView> GetActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        var active = await ActiveAsync(cancellationToken);
        return (await ListProfilesAsync(cancellationToken)).First(p => p.Id == active.Id);
    }

    /// <summary>Adds an empty scenario and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchProfileView> CreateProfileAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var profile = new SearchProfile
        {
            Name = Required(name, "A scenario needs a name.", nameof(name)),
            SortOrder = await NextProfileOrderAsync(cancellationToken),
            UpdatedAt = clock.GetUtcNow(),
        };

        context.SearchProfiles.Add(profile);
        await context.SaveChangesAsync(cancellationToken);

        return (await ListProfilesAsync(cancellationToken)).First(p => p.Id == profile.Id);
    }

    /// <summary>
    /// Copies a scenario under a new name: its weights, destinations, cars and toll roads, the car
    /// each destination drives and the toll roads marked by hand against each drive, and the drive
    /// times already measured, so only the difference has to be changed. Returns null when no
    /// scenario has that id.
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchProfileView?> CopyProfileAsync(
        int id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var copied = Required(name, "A scenario needs a name.", nameof(name));

        var source = await context.SearchProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (source is null)
        {
            return null;
        }

        var profile = new SearchProfile
        {
            Name = copied,
            SortOrder = await NextProfileOrderAsync(cancellationToken),
            MonthlyFeePointsPerDollar = source.MonthlyFeePointsPerDollar,
            DepositPointsPerDollar = source.DepositPointsPerDollar,
            DriveTimePointsPerMinute = source.DriveTimePointsPerMinute,
            AmenityPoints = source.AmenityPoints,
            ProPoints = source.ProPoints,
            ConPoints = source.ConPoints,
            SizePointsPerSquareFoot = source.SizePointsPerSquareFoot,
            UpdatedAt = clock.GetUtcNow(),
        };

        context.SearchProfiles.Add(profile);

        var cars = new Dictionary<int, Vehicle>();
        foreach (var car in await Owned(context.Vehicles, id).OrderBy(v => v.SortOrder).ToListAsync(cancellationToken))
        {
            var made = new Vehicle
            {
                Profile = profile,
                Name = car.Name,
                MilesPerGallon = car.MilesPerGallon,
                FuelPricePerGallon = car.FuelPricePerGallon,
                SortOrder = car.SortOrder,
            };

            cars[car.Id] = made;
            context.Vehicles.Add(made);
        }

        var tolls = new Dictionary<int, TollRoad>();
        foreach (var toll in await Owned(context.TollRoads, id).OrderBy(t => t.SortOrder).ToListAsync(cancellationToken))
        {
            var made = new TollRoad
            {
                Profile = profile,
                Road = toll.Road,
                PeakToll = toll.PeakToll,
                SortOrder = toll.SortOrder,
            };

            tolls[toll.Id] = made;
            context.TollRoads.Add(made);
        }

        var destinations = await Owned(context.SearchDestinations, id)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);

        var drives = await context.PropertyDriveTimes.AsNoTracking()
            .Where(d => d.Destination!.ProfileId == id)
            .ToListAsync(cancellationToken);

        // The marks name toll roads by id, and the copies have no ids until they are saved, so
        // each copied drive is noted here and its marks filled in afterwards.
        var marked = new List<(PropertyDriveTime Copy, IReadOnlyList<int> Marks)>();

        foreach (var destination in destinations)
        {
            var made = new SearchDestination
            {
                Profile = profile,
                Name = destination.Name,
                Address = destination.Address,
                Weight = destination.Weight,
                RoundTripsPerWeek = destination.RoundTripsPerWeek,
                ParkingPerDay = destination.ParkingPerDay,
                CommuteCreditPerDay = destination.CommuteCreditPerDay,
                DriveTimeMultiplier = destination.DriveTimeMultiplier,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                SortOrder = destination.SortOrder,
                Vehicle = destination.VehicleId is { } carId && cars.TryGetValue(carId, out var car) ? car : null,
            };

            context.SearchDestinations.Add(made);

            foreach (var drive in drives.Where(d => d.DestinationId == destination.Id))
            {
                var copy = new PropertyDriveTime
                {
                    PropertyId = drive.PropertyId,
                    Destination = made,
                    Minutes = drive.Minutes,
                    Miles = drive.Miles,
                    Source = drive.Source,
                    MeasuredAt = drive.MeasuredAt,
                    TolledRoads = [.. drive.TolledRoads],
                    ParkingPerDayOverride = drive.ParkingPerDayOverride,
                    CommuteCreditPerDayOverride = drive.CommuteCreditPerDayOverride,
                };

                context.PropertyDriveTimes.Add(copy);

                if (drive.TollRoadOverrideIds is { } marks)
                {
                    marked.Add((copy, marks));
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        foreach (var (copy, marks) in marked)
        {
            copy.TollRoadOverrideIds = marks
                .Where(tolls.ContainsKey)
                .Select(tollId => tolls[tollId].Id)
                .ToList();
        }

        if (marked.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return (await ListProfilesAsync(cancellationToken)).First(p => p.Id == profile.Id);
    }

    /// <summary>Renames a scenario, or returns null when none has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<SearchProfileView?> RenameProfileAsync(
        int id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var profile = await context.SearchProfiles.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        profile.Name = Required(name, "A scenario needs a name.", nameof(name));
        profile.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return (await ListProfilesAsync(cancellationToken)).First(p => p.Id == id);
    }

    /// <summary>Makes a scenario the one the pages show, or returns null when none has that id.</summary>
    public async Task<SearchProfileView?> ActivateProfileAsync(int id, CancellationToken cancellationToken = default)
    {
        var profiles = await context.SearchProfiles.ToListAsync(cancellationToken);
        if (profiles.All(p => p.Id != id))
        {
            return null;
        }

        foreach (var profile in profiles)
        {
            profile.IsActive = profile.Id == id;
        }

        await context.SaveChangesAsync(cancellationToken);

        return (await ListProfilesAsync(cancellationToken)).First(p => p.Id == id);
    }

    /// <summary>
    /// Deletes a scenario, with its destinations, cars, toll roads and the drive times measured to
    /// them. Returns false when none has that id, or when it is the only one left.
    /// </summary>
    public async Task<bool> DeleteProfileAsync(int id, CancellationToken cancellationToken = default)
    {
        var profiles = await context.SearchProfiles.OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var profile = profiles.FirstOrDefault(p => p.Id == id);

        // The search has to be scored under something, so the last scenario stays.
        if (profile is null || profiles.Count < 2)
        {
            return false;
        }

        context.SearchProfiles.Remove(profile);

        if (profile.IsActive)
        {
            profiles.First(p => p.Id != id).IsActive = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns the destinations of the active scenario, in order.</summary>
    public async Task<IReadOnlyList<SearchDestinationView>> ListDestinationsAsync(CancellationToken cancellationToken = default)
    {
        var active = await ActiveAsync(cancellationToken);

        return await context.SearchDestinations.AsNoTracking()
            .Where(d => d.ProfileId == active.Id)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Id)
            .Select(d => new SearchDestinationView(
                d.Id, d.Name, d.Address, d.Weight, d.RoundTripsPerWeek, d.CommuteCreditPerDay,
                d.Latitude, d.Longitude, d.SortOrder, d.DriveTimeMultiplier, d.VehicleId,
                d.ParkingPerDay))
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
        var active = await ActiveAsync(cancellationToken);

        var last = await Owned(context.SearchDestinations, active.Id).AsNoTracking()
            .OrderByDescending(d => d.SortOrder)
            .Select(d => (int?)d.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var destination = new SearchDestination
        {
            ProfileId = active.Id,
            Name = name,
            Address = Blank(request.Address),
            Weight = request.Weight,
            RoundTripsPerWeek = Trips(request.RoundTripsPerWeek),
            ParkingPerDay = Positive(request.ParkingPerDay),
            CommuteCreditPerDay = Positive(request.CommuteCreditPerDay),
            DriveTimeMultiplier = Multiplier(request.DriveTimeMultiplier),
            VehicleId = await CarAsync(request.VehicleId, cancellationToken),
            SortOrder = (last ?? -1) + 1,
        };

        context.SearchDestinations.Add(destination);
        await context.SaveChangesAsync(cancellationToken);

        return Viewed(destination);
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

            // Every drive measured to it was measured to where it used to be, so those times go
            // with the pin; the ones typed in by hand are his own figures and stay.
            context.PropertyDriveTimes.RemoveRange(await context.PropertyDriveTimes
                .Where(d => d.DestinationId == id && d.Source == DriveTimeSource.Measured)
                .ToListAsync(cancellationToken));
        }

        destination.Name = Required(request.Name, "A destination needs a name.", nameof(request));
        destination.Address = address;
        destination.Weight = request.Weight;
        destination.RoundTripsPerWeek = Trips(request.RoundTripsPerWeek);
        destination.ParkingPerDay = Positive(request.ParkingPerDay);
        destination.CommuteCreditPerDay = Positive(request.CommuteCreditPerDay);
        destination.DriveTimeMultiplier = Multiplier(request.DriveTimeMultiplier);
        destination.VehicleId = await CarAsync(request.VehicleId, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return Viewed(destination);
    }

    /// <summary>A destination as the pages and the API see it.</summary>
    private static SearchDestinationView Viewed(SearchDestination destination) =>
        new(destination.Id, destination.Name, destination.Address, destination.Weight,
            destination.RoundTripsPerWeek, destination.CommuteCreditPerDay,
            destination.Latitude, destination.Longitude, destination.SortOrder,
            destination.DriveTimeMultiplier, destination.VehicleId, destination.ParkingPerDay);

    // A car from another scenario, or one since deleted, leaves the drive shared out rather than
    // failing the save.
    private async Task<int?> CarAsync(int? vehicleId, CancellationToken cancellationToken)
    {
        var active = await ActiveAsync(cancellationToken);

        return vehicleId is { } id
            && await context.Vehicles.AnyAsync(v => v.Id == id && v.ProfileId == active.Id, cancellationToken)
                ? id
                : null;
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
                p.Properties.Count(m => m.Meets),
                p.Penalty))
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
            Penalty = Math.Abs(request.Penalty),
            SortOrder = (last ?? -1) + 1,
        };

        context.SearchPriorities.Add(priority);
        await context.SaveChangesAsync(cancellationToken);

        return new SearchPriorityView(priority.Id, priority.Name, priority.Points, priority.SortOrder, 0, priority.Penalty);
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
        priority.Penalty = Math.Abs(request.Penalty);
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

    /// <summary>Returns the toll roads of the active scenario, in order.</summary>
    public async Task<IReadOnlyList<TollRoadView>> ListTollRoadsAsync(CancellationToken cancellationToken = default)
    {
        var active = await ActiveAsync(cancellationToken);

        return await Owned(context.TollRoads, active.Id).AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .Select(t => new TollRoadView(t.Id, t.Road, t.PeakToll, t.SortOrder))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Adds a toll road and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The road is blank.</exception>
    public async Task<TollRoadView> CreateTollRoadAsync(
        TollRoadRequest request,
        CancellationToken cancellationToken = default)
    {
        var road = Required(request.Road, "A toll road needs the name to look for.", nameof(request));
        var active = await ActiveAsync(cancellationToken);

        var last = await Owned(context.TollRoads, active.Id).AsNoTracking()
            .OrderByDescending(t => t.SortOrder)
            .Select(t => (int?)t.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var toll = new TollRoad
        {
            ProfileId = active.Id,
            Road = road,
            PeakToll = Positive(request.PeakToll),
            SortOrder = (last ?? -1) + 1,
        };

        context.TollRoads.Add(toll);
        await context.SaveChangesAsync(cancellationToken);

        return new TollRoadView(toll.Id, toll.Road, toll.PeakToll, toll.SortOrder);
    }

    /// <summary>Changes a toll road, or returns null when none has that id.</summary>
    /// <exception cref="ArgumentException">The road is blank.</exception>
    public async Task<TollRoadView?> UpdateTollRoadAsync(
        int id,
        TollRoadRequest request,
        CancellationToken cancellationToken = default)
    {
        var toll = await context.TollRoads.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (toll is null)
        {
            return null;
        }

        toll.Road = Required(request.Road, "A toll road needs the name to look for.", nameof(request));
        toll.PeakToll = Positive(request.PeakToll);
        await context.SaveChangesAsync(cancellationToken);

        return new TollRoadView(toll.Id, toll.Road, toll.PeakToll, toll.SortOrder);
    }

    /// <summary>Deletes a toll road. Returns false when none has that id.</summary>
    public async Task<bool> DeleteTollRoadAsync(int id, CancellationToken cancellationToken = default)
    {
        var toll = await context.TollRoads.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (toll is null)
        {
            return false;
        }

        context.TollRoads.Remove(toll);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns the cars of the active scenario, in order.</summary>
    public async Task<IReadOnlyList<VehicleView>> ListVehiclesAsync(CancellationToken cancellationToken = default)
    {
        var active = await ActiveAsync(cancellationToken);

        return await Owned(context.Vehicles, active.Id).AsNoTracking()
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Id)
            .Select(v => new VehicleView(v.Id, v.Name, v.MilesPerGallon, v.FuelPricePerGallon, v.SortOrder))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Adds a car and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<VehicleView> CreateVehicleAsync(
        VehicleRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = Required(request.Name, "A car needs a name.", nameof(request));
        var active = await ActiveAsync(cancellationToken);

        var last = await Owned(context.Vehicles, active.Id).AsNoTracking()
            .OrderByDescending(v => v.SortOrder)
            .Select(v => (int?)v.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var vehicle = new Vehicle
        {
            ProfileId = active.Id,
            Name = name,
            MilesPerGallon = Positive(request.MilesPerGallon),
            FuelPricePerGallon = Positive(request.FuelPricePerGallon),
            SortOrder = (last ?? -1) + 1,
        };

        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync(cancellationToken);

        return new VehicleView(
            vehicle.Id, vehicle.Name, vehicle.MilesPerGallon, vehicle.FuelPricePerGallon, vehicle.SortOrder);
    }

    /// <summary>Changes a car, or returns null when none has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<VehicleView?> UpdateVehicleAsync(
        int id,
        VehicleRequest request,
        CancellationToken cancellationToken = default)
    {
        var vehicle = await context.Vehicles.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        vehicle.Name = Required(request.Name, "A car needs a name.", nameof(request));
        vehicle.MilesPerGallon = Positive(request.MilesPerGallon);
        vehicle.FuelPricePerGallon = Positive(request.FuelPricePerGallon);
        await context.SaveChangesAsync(cancellationToken);

        return new VehicleView(
            vehicle.Id, vehicle.Name, vehicle.MilesPerGallon, vehicle.FuelPricePerGallon, vehicle.SortOrder);
    }

    /// <summary>Deletes a car. Returns false when none has that id.</summary>
    public async Task<bool> DeleteVehicleAsync(int id, CancellationToken cancellationToken = default)
    {
        var vehicle = await context.Vehicles.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return false;
        }

        context.Vehicles.Remove(vehicle);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Returns the active scenario's weights and the routing service, with every weight at zero
    /// until they are set.
    /// </summary>
    public async Task<HomeSearchSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var active = await ActiveAsync(cancellationToken);
        var held = await context.HomeSearchSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return View(active, held ?? new HomeSearchSettings());
    }

    /// <summary>
    /// Applies the set members of <paramref name="request"/> and returns the settings as saved. The
    /// weights are the active scenario's; the routing service is shared by all of them.
    /// </summary>
    public async Task<HomeSearchSettingsView> SaveSettingsAsync(
        HomeSearchSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var active = await ActiveAsync(cancellationToken);

        var settings = await context.HomeSearchSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new HomeSearchSettings();
            context.HomeSearchSettings.Add(settings);
        }

        active.MonthlyFeePointsPerDollar = request.MonthlyFeePointsPerDollar ?? active.MonthlyFeePointsPerDollar;
        active.DepositPointsPerDollar = request.DepositPointsPerDollar ?? active.DepositPointsPerDollar;
        active.DriveTimePointsPerMinute = request.DriveTimePointsPerMinute ?? active.DriveTimePointsPerMinute;
        active.AmenityPoints = request.AmenityPoints ?? active.AmenityPoints;
        active.ProPoints = request.ProPoints ?? active.ProPoints;
        active.ConPoints = request.ConPoints ?? active.ConPoints;
        active.SizePointsPerSquareFoot = request.SizePointsPerSquareFoot ?? active.SizePointsPerSquareFoot;
        active.UpdatedAt = clock.GetUtcNow();

        if (request.RoutingBaseUrl is not null)
        {
            settings.RoutingBaseUrl = RoutingEndpoints.Normalize(request.RoutingBaseUrl);
        }

        // An empty box means the key held should stay, since the page never renders it back; the
        // settings screen offers its own button to forget it.
        if (!string.IsNullOrWhiteSpace(request.RoutingApiKey))
        {
            settings.RoutingApiKey = request.RoutingApiKey.Trim();
        }

        settings.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return View(active, settings);
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

    private static HomeSearchSettingsView View(SearchProfile profile, HomeSearchSettings settings) => new(
        profile.MonthlyFeePointsPerDollar,
        profile.DepositPointsPerDollar,
        profile.DriveTimePointsPerMinute,
        profile.AmenityPoints,
        profile.ProPoints,
        profile.ConPoints,
        profile.SizePointsPerSquareFoot,
        settings.RoutingBaseUrl,
        !string.IsNullOrWhiteSpace(settings.RoutingApiKey));

    /// <summary>
    /// The scenario the pages are showing. A search that has never had one named gets the first
    /// profile here, which is what every destination, car and toll road already saved belongs to.
    /// </summary>
    private async Task<SearchProfile> ActiveAsync(CancellationToken cancellationToken)
    {
        var profiles = await context.SearchProfiles.OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        if (profiles.FirstOrDefault(p => p.IsActive) is { } active)
        {
            return active;
        }

        // One left unmarked, or none at all: the first is the one to show.
        if (profiles.FirstOrDefault() is { } first)
        {
            first.IsActive = true;
            await context.SaveChangesAsync(cancellationToken);
            return first;
        }

        var made = new SearchProfile
        {
            Name = DefaultProfileName,
            IsActive = true,
            UpdatedAt = clock.GetUtcNow(),
        };

        context.SearchProfiles.Add(made);
        await context.SaveChangesAsync(cancellationToken);
        return made;
    }

    private async Task<int> NextProfileOrderAsync(CancellationToken cancellationToken)
    {
        var last = await context.SearchProfiles.AsNoTracking()
            .OrderByDescending(p => p.SortOrder)
            .Select(p => (int?)p.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        return (last ?? -1) + 1;
    }

    private static IQueryable<T> Owned<T>(DbSet<T> set, int profileId)
        where T : class, IProfileOwned
    {
        return set.Where(owned => owned.ProfileId == profileId);
    }

    private static string Required(string? value, string message, string parameter)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > 0 ? trimmed : throw new ArgumentException(message, parameter);
    }

    private static decimal Trips(decimal value) => value > 0m ? value : 0m;

    private static double Positive(double value) => value > 0d ? value : 0d;

    private static decimal Positive(decimal value) => value > 0m ? value : 0m;

    // A multiplier of zero or less would wipe the drive out rather than stretch it, so anything
    // that is not a sensible factor leaves the measured time alone.
    private static double Multiplier(double value) => value > 0d ? value : 1d;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
