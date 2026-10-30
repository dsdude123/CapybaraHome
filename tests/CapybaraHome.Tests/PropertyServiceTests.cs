using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers the properties being tracked and everything hanging off them.</summary>
public class PropertyServiceTests
{
    [Fact]
    public async Task A_property_is_saved_with_its_utilities_amenities_and_points()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        var created = await service.CreateAsync(new CreatePropertyRequest(
            "  Harbor Steps  ",
            Address: " 1221 1st Ave ",
            Rent: 2380m,
            ParkingFee: 0m,
            PetRent: 25m,
            Deposit: 300m,
            Internet: "Fibre included",
            Utilities:
            [
                new PropertyUtilityInput(UtilityKind.Electricity, 70m),
                new PropertyUtilityInput(UtilityKind.Other, 15m, "Valet trash"),
            ],
            Amenities: ["Gym", "  ", "Pool"],
            Points:
            [
                new PropertyPointInput(PropertyPointKind.Pro, "Walk to the water"),
                new PropertyPointInput(PropertyPointKind.Con, "Small kitchen"),
            ]));

        Assert.Equal("Harbor Steps", created.Name);
        Assert.Equal("1221 1st Ave", created.Address);
        Assert.Equal(2, created.Utilities.Count);
        Assert.Equal(85m, created.UtilitiesTotal);
        Assert.Equal("Valet trash", created.Utilities.Single(u => u.Kind == UtilityKind.Other).Label);
        Assert.Equal(["Gym", "Pool"], created.Amenities.Select(a => a.Name));
        Assert.Equal("Walk to the water", created.Pros.Single().Text);
        Assert.Equal("Small kitchen", created.Cons.Single().Text);
        Assert.Equal(2490m, created.MonthlyTotal);
    }

    [Fact]
    public async Task A_blank_name_is_refused_on_the_way_in_and_on_the_way_through()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreatePropertyRequest("   ")));

        var created = await service.CreateAsync(new CreatePropertyRequest("Named"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(created.Id, new UpdatePropertyRequest(Name: " ")));
    }

    [Fact]
    public async Task The_lists_are_replaced_whole_rather_than_added_to()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        var created = await service.CreateAsync(new CreatePropertyRequest(
            "Replaced",
            Utilities: [new PropertyUtilityInput(UtilityKind.Gas, 40m)],
            Amenities: ["Gym"],
            Points: [new PropertyPointInput(PropertyPointKind.Pro, "Old")]));

        var updated = await service.UpdateAsync(created.Id, new UpdatePropertyRequest(
            Utilities: [new PropertyUtilityInput(UtilityKind.Water, 20m)],
            Amenities: ["Pool", "Lounge"],
            Points: [new PropertyPointInput(PropertyPointKind.Con, "New")]));

        Assert.Equal(UtilityKind.Water, updated!.Utilities.Single().Kind);
        Assert.Equal(["Pool", "Lounge"], updated.Amenities.Select(a => a.Name));
        Assert.Empty(updated.Pros);
        Assert.Equal("New", updated.Cons.Single().Text);
        Assert.Equal(1, await db.Context.PropertyUtilities.CountAsync());
        Assert.Equal(2, await db.Context.PropertyAmenities.CountAsync());
        Assert.Equal(1, await db.Context.PropertyPoints.CountAsync());
    }

    [Fact]
    public async Task A_money_field_left_out_is_kept_unless_the_whole_set_is_being_posted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreatePropertyRequest(
            "Money", Rent: 2000m, ParkingFee: 100m, Deposit: 500m));

        var patched = await service.UpdateAsync(created.Id, new UpdatePropertyRequest(Rent: 2100m));
        Assert.Equal(2100m, patched!.Rent);
        Assert.Equal(100m, patched.ParkingFee);

        var posted = await service.UpdateAsync(created.Id, new UpdatePropertyRequest(Rent: 2200m, ClearMoney: true));
        Assert.Equal(2200m, posted!.Rent);
        Assert.Null(posted.ParkingFee);
        Assert.Null(posted.Deposit);
    }

    [Fact]
    public async Task Only_one_property_at_a_time_is_the_current_home()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        var first = await service.CreateAsync(new CreatePropertyRequest("First", IsCurrent: true));
        var second = await service.CreateAsync(new CreatePropertyRequest("Second", IsCurrent: true));

        Assert.False((await service.GetAsync(first.Id))!.IsCurrent);
        Assert.True((await service.GetAsync(second.Id))!.IsCurrent);
        Assert.Equal(second.Id, (await service.GetCurrentAsync())!.Id);

        await service.UpdateAsync(first.Id, new UpdatePropertyRequest(IsCurrent: true));

        Assert.Equal(first.Id, (await service.GetCurrentAsync())!.Id);
        Assert.False((await service.GetAsync(second.Id))!.IsCurrent);
    }

    [Fact]
    public async Task With_no_current_home_marked_there_is_none_to_read()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        await service.CreateAsync(new CreatePropertyRequest("Candidate"));

        Assert.Null(await service.GetCurrentAsync());
    }

    [Fact]
    public async Task The_listing_puts_the_current_home_first_and_the_rest_by_name()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        await service.CreateAsync(new CreatePropertyRequest("Zebra"));
        await service.CreateAsync(new CreatePropertyRequest("Apple"));
        await service.CreateAsync(new CreatePropertyRequest("Now", IsCurrent: true));

        Assert.Equal(["Now", "Apple", "Zebra"], (await service.ListAsync()).Select(p => p.Name));
    }

    [Fact]
    public async Task Deleting_a_property_takes_everything_hanging_off_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var search = new HomeSearchService(db.Context, db.Clock);

        var property = await service.CreateAsync(new CreatePropertyRequest(
            "Going", Utilities: [new PropertyUtilityInput(UtilityKind.Gas, 10m)], Amenities: ["Gym"]));

        var destination = await search.CreateDestinationAsync(new SearchDestinationRequest("Work"));
        var priority = await search.CreatePriorityAsync(new SearchPriorityRequest("Laundry", 5m));

        await service.AddNoteAsync(property.Id, "A note");
        await service.SetDriveTimeAsync(property.Id, destination.Id, 20d, null, DriveTimeSource.Manual);
        await service.MarkPriorityAsync(property.Id, priority.Id, true);

        Assert.True(await service.DeleteAsync(property.Id));
        Assert.False(await service.DeleteAsync(property.Id));

        Assert.Equal(0, await db.Context.PropertyUtilities.CountAsync());
        Assert.Equal(0, await db.Context.PropertyAmenities.CountAsync());
        Assert.Equal(0, await db.Context.PropertyNotes.CountAsync());
        Assert.Equal(0, await db.Context.PropertyDriveTimes.CountAsync());
        Assert.Equal(0, await db.Context.PropertyPriorities.CountAsync());
    }

    [Fact]
    public async Task Notes_are_stamped_kept_newest_first_and_can_be_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var property = await service.CreateAsync(new CreatePropertyRequest("Noted"));

        var first = await service.AddNoteAsync(property.Id, "  Toured it  ");
        db.Clock.Advance(TimeSpan.FromHours(1));
        var second = await service.AddNoteAsync(property.Id, "Applied");

        Assert.Equal("Toured it", first!.Body);
        Assert.Equal(db.Clock.GetUtcNow(), second!.CreatedAt);
        Assert.Equal(["Applied", "Toured it"], (await service.GetAsync(property.Id))!.Notes.Select(n => n.Body));

        Assert.True(await service.DeleteNoteAsync(first.Id));
        Assert.False(await service.DeleteNoteAsync(first.Id));
        Assert.Single((await service.GetAsync(property.Id))!.Notes);
    }

    [Fact]
    public async Task A_blank_note_is_refused_and_a_note_on_nothing_is_not_written()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddNoteAsync(1, "   "));
        Assert.Null(await service.AddNoteAsync(404, "A note on nothing"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task A_property_can_be_marked_either_way_against_a_priority_or_left_unmarked(bool? meets)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var search = new HomeSearchService(db.Context, db.Clock);

        var property = await service.CreateAsync(new CreatePropertyRequest("Marked"));
        var priority = await search.CreatePriorityAsync(new SearchPriorityRequest("Laundry", 10m));

        Assert.True(await service.MarkPriorityAsync(property.Id, priority.Id, meets));

        var held = (await service.GetAsync(property.Id))!.Priorities;

        if (meets is null)
        {
            Assert.Empty(held);
        }
        else
        {
            Assert.Equal(meets, held.Single().Meets);
            Assert.Equal(10m, held.Single().Points);
        }
    }

    [Fact]
    public async Task A_marking_is_changed_in_place_and_can_be_taken_back_off()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var search = new HomeSearchService(db.Context, db.Clock);

        var property = await service.CreateAsync(new CreatePropertyRequest("Marked"));
        var priority = await search.CreatePriorityAsync(new SearchPriorityRequest("Laundry", 10m));

        await service.MarkPriorityAsync(property.Id, priority.Id, true);
        await service.MarkPriorityAsync(property.Id, priority.Id, false);
        Assert.False((await service.GetAsync(property.Id))!.Priorities.Single().Meets);

        await service.MarkPriorityAsync(property.Id, priority.Id, null);
        Assert.Empty((await service.GetAsync(property.Id))!.Priorities);

        // Clearing a marking that was never made is not a failure.
        Assert.True(await service.MarkPriorityAsync(property.Id, priority.Id, null));
    }

    [Fact]
    public async Task Marking_against_a_property_or_a_priority_that_is_not_there_does_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var search = new HomeSearchService(db.Context, db.Clock);

        var property = await service.CreateAsync(new CreatePropertyRequest("Marked"));
        var priority = await search.CreatePriorityAsync(new SearchPriorityRequest("Laundry"));

        Assert.False(await service.MarkPriorityAsync(404, priority.Id, true));
        Assert.False(await service.MarkPriorityAsync(property.Id, 404, true));
    }

    [Fact]
    public async Task A_drive_time_is_replaced_rather_than_repeated_and_can_be_forgotten()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var search = new HomeSearchService(db.Context, db.Clock);

        var property = await service.CreateAsync(new CreatePropertyRequest("Driven"));
        var destination = await search.CreateDestinationAsync(new SearchDestinationRequest("Work"));

        await service.SetDriveTimeAsync(property.Id, destination.Id, 25d, null, DriveTimeSource.Manual);
        await service.SetDriveTimeAsync(property.Id, destination.Id, 18.5d, 12.4d, DriveTimeSource.Measured);

        var held = (await service.GetAsync(property.Id))!.DriveTimes.Single();
        Assert.Equal(18.5d, held.Minutes);
        Assert.Equal(12.4d, held.Kilometres);
        Assert.Equal(DriveTimeSource.Measured, held.Source);
        Assert.Equal("Work", held.DestinationName);
        Assert.Equal(db.Clock.GetUtcNow(), held.MeasuredAt);

        Assert.True(await service.ClearDriveTimeAsync(property.Id, destination.Id));
        Assert.False(await service.ClearDriveTimeAsync(property.Id, destination.Id));
        Assert.Empty((await service.GetAsync(property.Id))!.DriveTimes);
    }

    [Fact]
    public async Task A_drive_to_or_from_something_that_is_not_there_is_not_recorded()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var search = new HomeSearchService(db.Context, db.Clock);

        var property = await service.CreateAsync(new CreatePropertyRequest("Driven"));
        var destination = await search.CreateDestinationAsync(new SearchDestinationRequest("Work"));

        Assert.False(await service.SetDriveTimeAsync(404, destination.Id, 10d, null, DriveTimeSource.Manual));
        Assert.False(await service.SetDriveTimeAsync(property.Id, 404, 10d, null, DriveTimeSource.Manual));
    }

    [Fact]
    public async Task Coordinates_are_recorded_and_an_address_can_be_cleared()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);
        var property = await service.CreateAsync(new CreatePropertyRequest("Placed", Address: "Somewhere"));

        var placed = await service.UpdateAsync(property.Id, new UpdatePropertyRequest(Latitude: 47.6d, Longitude: -122.3d));
        Assert.Equal(47.6d, placed!.Latitude);
        Assert.Equal(-122.3d, placed.Longitude);

        var cleared = await service.UpdateAsync(property.Id, new UpdatePropertyRequest(Address: "  ", Internet: ""));
        Assert.Null(cleared!.Address);
        Assert.Null(cleared.Internet);
    }

    [Fact]
    public async Task Reading_or_changing_a_property_that_is_not_there_gives_nothing_back()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new PropertyService(db.Context, db.Clock);

        Assert.Null(await service.GetAsync(404));
        Assert.Null(await service.UpdateAsync(404, new UpdatePropertyRequest(Name: "Nothing")));
    }
}
