using Microsoft.EntityFrameworkCore;
using CapybaraHome.Application.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers the destinations, the priorities and the weights the scores are worked out under.</summary>
public class HomeSearchServiceTests
{
    [Fact]
    public async Task Destinations_keep_the_order_they_were_added_in()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        await service.CreateDestinationAsync(new SearchDestinationRequest("  Work  ", " 400 Broad St ", 3m));
        await service.CreateDestinationAsync(new SearchDestinationRequest("Gym"));

        var held = await service.ListDestinationsAsync();

        Assert.Equal(["Work", "Gym"], held.Select(d => d.Name));
        Assert.Equal("400 Broad St", held[0].Address);
        Assert.Equal(3m, held[0].Weight);
        Assert.Equal(1m, held[1].Weight);
        Assert.Equal([0, 1], held.Select(d => d.SortOrder));
    }

    [Fact]
    public async Task A_destination_keeps_its_pin_while_the_address_is_the_same_and_loses_it_when_it_changes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);
        var destination = await service.CreateDestinationAsync(new SearchDestinationRequest("Work", "400 Broad St"));

        await service.PlaceDestinationAsync(destination.Id, 47.6d, -122.3d);

        var renamed = await service.UpdateDestinationAsync(destination.Id, new SearchDestinationRequest("The office", "400 Broad St", 2m));
        Assert.Equal(47.6d, renamed!.Latitude);
        Assert.Equal(2m, renamed.Weight);

        var moved = await service.UpdateDestinationAsync(destination.Id, new SearchDestinationRequest("The office", "999 Elsewhere"));
        Assert.Null(moved!.Latitude);
        Assert.Null(moved.Longitude);
    }

    [Fact]
    public async Task A_destination_takes_the_drive_times_with_it_when_it_goes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);
        var properties = new PropertyService(db.Context, db.Clock);

        var destination = await service.CreateDestinationAsync(new SearchDestinationRequest("Work"));
        var property = await properties.CreateAsync(new CreatePropertyRequest("Somewhere"));
        await properties.SetDriveTimeAsync(property.Id, destination.Id, 20d, null, CapybaraHome.Domain.Homes.DriveTimeSource.Manual);

        Assert.True(await service.DeleteDestinationAsync(destination.Id));
        Assert.False(await service.DeleteDestinationAsync(destination.Id));
        Assert.Empty((await properties.GetAsync(property.Id))!.DriveTimes);
    }

    [Fact]
    public async Task A_blank_destination_or_priority_name_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDestinationAsync(new SearchDestinationRequest("  ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePriorityAsync(new SearchPriorityRequest(" ")));

        var destination = await service.CreateDestinationAsync(new SearchDestinationRequest("Work"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateDestinationAsync(destination.Id, new SearchDestinationRequest("")));

        var priority = await service.CreatePriorityAsync(new SearchPriorityRequest("Laundry"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdatePriorityAsync(priority.Id, new SearchPriorityRequest("")));
    }

    [Fact]
    public async Task Changing_or_deleting_something_that_is_not_there_gives_nothing_back()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        Assert.Null(await service.GetDestinationAsync(404));
        Assert.Null(await service.UpdateDestinationAsync(404, new SearchDestinationRequest("Work")));
        Assert.False(await service.PlaceDestinationAsync(404, 1d, 2d));
        Assert.False(await service.DeleteDestinationAsync(404));
        Assert.Null(await service.UpdatePriorityAsync(404, new SearchPriorityRequest("Laundry")));
        Assert.False(await service.DeletePriorityAsync(404));
    }

    [Fact]
    public async Task A_priority_counts_the_properties_marked_as_meeting_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);
        var properties = new PropertyService(db.Context, db.Clock);

        var priority = await service.CreatePriorityAsync(new SearchPriorityRequest("Laundry", 10m));
        var meets = await properties.CreateAsync(new CreatePropertyRequest("Meets"));
        var does_not = await properties.CreateAsync(new CreatePropertyRequest("Does not"));

        await properties.MarkPriorityAsync(meets.Id, priority.Id, true);
        await properties.MarkPriorityAsync(does_not.Id, priority.Id, false);

        Assert.Equal(1, (await service.ListPrioritiesAsync()).Single().MetCount);
    }

    [Fact]
    public async Task A_priority_can_be_renamed_repointed_and_deleted_with_its_markings()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);
        var properties = new PropertyService(db.Context, db.Clock);

        var priority = await service.CreatePriorityAsync(new SearchPriorityRequest("Laundry", 10m));
        var property = await properties.CreateAsync(new CreatePropertyRequest("Somewhere"));
        await properties.MarkPriorityAsync(property.Id, priority.Id, true);

        var updated = await service.UpdatePriorityAsync(priority.Id, new SearchPriorityRequest("In-unit laundry", -5m));
        Assert.Equal("In-unit laundry", updated!.Name);
        Assert.Equal(-5m, updated.Points);

        Assert.True(await service.DeletePriorityAsync(priority.Id));
        Assert.Empty((await properties.GetAsync(property.Id))!.Priorities);
    }

    [Fact]
    public async Task The_settings_start_at_zero_with_no_service_set_up()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        var settings = await service.GetSettingsAsync();

        Assert.False(settings.AnyWeightSet);
        Assert.False(settings.CanMeasureDriveTimes);
        Assert.False(settings.HasRoutingApiKey);
        Assert.Null(settings.RoutingBaseUrl);
        Assert.Equal(0m, settings.MonthlyFeePointsPerDollar);
    }

    [Fact]
    public async Task Saving_a_weight_leaves_the_others_where_they_were()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        await service.SaveSettingsAsync(new HomeSearchSettingsRequest(
            MonthlyFeePointsPerDollar: 0.05m,
            DepositPointsPerDollar: 0.01m,
            DriveTimePointsPerMinute: 1.5m,
            AmenityPoints: 3m,
            ProPoints: 2m,
            ConPoints: 4m));

        var saved = await service.SaveSettingsAsync(new HomeSearchSettingsRequest(AmenityPoints: 5m));

        Assert.Equal(5m, saved.AmenityPoints);
        Assert.Equal(0.05m, saved.MonthlyFeePointsPerDollar);
        Assert.Equal(4m, saved.ConPoints);
        Assert.True(saved.AnyWeightSet);
        Assert.Equal(db.Clock.GetUtcNow(), await UpdatedAtAsync(db));
    }

    [Fact]
    public async Task The_routing_key_is_kept_by_an_empty_box_and_never_read_back()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        var set = await service.SaveSettingsAsync(new HomeSearchSettingsRequest(
            RoutingBaseUrl: " https://api.openrouteservice.org ", RoutingApiKey: "  a-key  "));

        Assert.Equal("https://api.openrouteservice.org", set.RoutingBaseUrl);
        Assert.True(set.HasRoutingApiKey);
        Assert.True(set.CanMeasureDriveTimes);
        Assert.Equal("a-key", await service.GetRoutingKeyAsync());

        var again = await service.SaveSettingsAsync(new HomeSearchSettingsRequest(RoutingApiKey: "   "));
        Assert.True(again.HasRoutingApiKey);
        Assert.Equal("a-key", await service.GetRoutingKeyAsync());
    }

    [Fact]
    public async Task The_routing_key_and_the_address_can_both_be_taken_back_out()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        // Forgetting a key that was never held is not a failure.
        await service.ForgetRoutingKeyAsync();

        await service.SaveSettingsAsync(new HomeSearchSettingsRequest(
            RoutingBaseUrl: "https://api.openrouteservice.org", RoutingApiKey: "a-key"));

        await service.ForgetRoutingKeyAsync();
        Assert.False((await service.GetSettingsAsync()).HasRoutingApiKey);
        Assert.Null(await service.GetRoutingKeyAsync());

        var cleared = await service.SaveSettingsAsync(new HomeSearchSettingsRequest(RoutingBaseUrl: ""));
        Assert.Null(cleared.RoutingBaseUrl);
        Assert.False(cleared.CanMeasureDriveTimes);
    }

    [Fact]
    public async Task A_destination_keeps_how_many_round_trips_a_week_are_driven_to_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        var work = await service.CreateDestinationAsync(
            new SearchDestinationRequest("Work", "400 Broad St", 3m, 3));

        Assert.Equal(3, work.RoundTripsPerWeek);

        var fewer = await service.UpdateDestinationAsync(
            work.Id, new SearchDestinationRequest("Work", "400 Broad St", 3m, 2));
        Assert.Equal(2, fewer!.RoundTripsPerWeek);

        // A negative count would owe miles back, so it is read as none.
        var none = await service.UpdateDestinationAsync(
            work.Id, new SearchDestinationRequest("Work", "400 Broad St", 3m, -4));
        Assert.Equal(0, none!.RoundTripsPerWeek);
    }

    [Fact]
    public async Task Cars_keep_the_order_they_were_added_in_and_can_be_changed_and_dropped()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        var first = await service.CreateVehicleAsync(new VehicleRequest("  The hatchback  ", 34.5d, 4.19m));
        await service.CreateVehicleAsync(new VehicleRequest("The truck", 18d, 4.39m));

        var held = await service.ListVehiclesAsync();
        Assert.Equal(["The hatchback", "The truck"], held.Select(v => v.Name));
        Assert.Equal([0, 1], held.Select(v => v.SortOrder));
        Assert.Equal(34.5d, held[0].MilesPerGallon);
        Assert.Equal(4.19m, held[0].FuelPricePerGallon);
        Assert.True(held[0].CanCost);

        var changed = await service.UpdateVehicleAsync(first.Id, new VehicleRequest("The hatchback", 36d, 4.05m));
        Assert.Equal(36d, changed!.MilesPerGallon);
        Assert.Equal(4.05m, changed.FuelPricePerGallon);

        Assert.True(await service.DeleteVehicleAsync(first.Id));
        Assert.False(await service.DeleteVehicleAsync(first.Id));
        Assert.Single(await service.ListVehiclesAsync());
        Assert.Null(await service.UpdateVehicleAsync(first.Id, new VehicleRequest("Gone")));
    }

    [Fact]
    public async Task A_car_needs_a_name_and_refuses_a_negative_mileage_or_price()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateVehicleAsync(new VehicleRequest("   ")));

        var vehicle = await service.CreateVehicleAsync(new VehicleRequest("Car", -30d, -4m));
        Assert.Equal(0d, vehicle.MilesPerGallon);
        Assert.Equal(0m, vehicle.FuelPricePerGallon);
        Assert.False(vehicle.CanCost);
    }

    [Fact]
    public async Task Toll_roads_keep_their_order_and_can_be_changed_and_dropped()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        var first = await service.CreateTollRoadAsync(new TollRoadRequest("  520  ", 4.50m));
        await service.CreateTollRoadAsync(new TollRoadRequest("99", 2.25m));

        var held = await service.ListTollRoadsAsync();
        Assert.Equal(["520", "99"], held.Select(t => t.Road));
        Assert.Equal([0, 1], held.Select(t => t.SortOrder));

        var changed = await service.UpdateTollRoadAsync(first.Id, new TollRoadRequest("SR 520", -3m));
        Assert.Equal("SR 520", changed!.Road);
        Assert.Equal(0m, changed.PeakToll);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTollRoadAsync(new TollRoadRequest(" ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateTollRoadAsync(first.Id, new TollRoadRequest("")));

        Assert.True(await service.DeleteTollRoadAsync(first.Id));
        Assert.False(await service.DeleteTollRoadAsync(first.Id));
        Assert.Null(await service.UpdateTollRoadAsync(first.Id, new TollRoadRequest("Gone")));
        Assert.Single(await service.ListTollRoadsAsync());
    }

    [Fact]
    public async Task A_destination_keeps_its_carpool_credit_and_refuses_a_negative_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);

        var work = await service.CreateDestinationAsync(new SearchDestinationRequest("Work", null, 3m, 3, 5m));
        Assert.Equal(5m, work.CarpoolCreditPerDay);

        var negative = await service.UpdateDestinationAsync(work.Id, new SearchDestinationRequest("Work", null, 3m, 3, -5m));
        Assert.Equal(0m, negative!.CarpoolCreditPerDay);
    }

    [Fact]
    public async Task A_drive_keeps_its_tolled_roads_trimmed_and_once_each()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new HomeSearchService(db.Context, db.Clock);
        var properties = new PropertyService(db.Context, db.Clock);

        var destination = await service.CreateDestinationAsync(new SearchDestinationRequest("Work"));
        var property = await properties.CreateAsync(new CreatePropertyRequest("Somewhere"));

        await properties.SetDriveTimeAsync(
            property.Id, destination.Id, 20d, 10d, CapybaraHome.Domain.Homes.DriveTimeSource.Manual,
            [" 520 ", "520", "", "99"]);

        Assert.Equal(["520", "99"], (await properties.GetAsync(property.Id))!.DriveTimes.Single().TolledRoads);
    }

    private static async Task<DateTimeOffset> UpdatedAtAsync(TestDatabase db) =>
        (await db.Context.HomeSearchSettings.FirstAsync()).UpdatedAt;
}
