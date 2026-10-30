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

    private static async Task<DateTimeOffset> UpdatedAtAsync(TestDatabase db) =>
        (await db.Context.HomeSearchSettings.FirstAsync()).UpdatedAt;
}
