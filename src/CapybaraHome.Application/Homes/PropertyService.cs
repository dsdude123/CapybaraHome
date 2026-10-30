using System.Linq.Expressions;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Homes;

/// <summary>Reads and writes the properties being tracked. The only place property rules live.</summary>
public class PropertyService(ICapybaraHomeDbContext context, TimeProvider clock)
{
    /// <summary>Returns every property, the current home first and then by name.</summary>
    public async Task<IReadOnlyList<PropertyView>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.Properties.AsNoTracking()
            .OrderByDescending(p => p.IsCurrent)
            .ThenBy(p => p.Name)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns one property, or null when no property has that id.</summary>
    public async Task<PropertyView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.Properties.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Returns the home lived in now, or null when none is marked.</summary>
    public async Task<PropertyView?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        return await context.Properties.AsNoTracking()
            .Where(p => p.IsCurrent)
            .OrderBy(p => p.Id)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Creates a property and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<PropertyView> CreateAsync(CreatePropertyRequest request, CancellationToken cancellationToken = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A property needs a name.", nameof(request));
        }

        var property = new Property
        {
            Name = name,
            Address = Blank(request.Address),
            IsCurrent = request.IsCurrent,
            Rent = request.Rent,
            ParkingFee = request.ParkingFee,
            PetRent = request.PetRent,
            Deposit = request.Deposit,
            Internet = Blank(request.Internet),
            Utilities = Utilities(request.Utilities),
            Amenities = Amenities(request.Amenities),
            Points = Points(request.Points),
        };

        context.Properties.Add(property);
        await context.SaveChangesAsync(cancellationToken);

        if (property.IsCurrent)
        {
            await LeaveOneCurrentAsync(property.Id, cancellationToken);
        }

        return await GetAsync(property.Id, cancellationToken)
            ?? throw new InvalidOperationException("The new property could not be read back.");
    }

    /// <summary>Applies the set members of <paramref name="request"/>, or returns null when no property has that id.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public async Task<PropertyView?> UpdateAsync(int id, UpdatePropertyRequest request, CancellationToken cancellationToken = default)
    {
        var property = await context.Properties
            .Include(p => p.Utilities)
            .Include(p => p.Amenities)
            .Include(p => p.Points)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (property is null)
        {
            return null;
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("A property needs a name.", nameof(request));
            }

            property.Name = name;
        }

        if (request.Address is not null)
        {
            property.Address = Blank(request.Address);
        }

        if (request.Internet is not null)
        {
            property.Internet = Blank(request.Internet);
        }

        if (request.IsCurrent is { } current)
        {
            property.IsCurrent = current;
        }

        // The editor posts every money box every time, so a box left empty means that fee is gone
        // rather than unchanged. An API caller sending one field keeps the rest by leaving the flag off.
        if (request.ClearMoney)
        {
            property.Rent = request.Rent;
            property.ParkingFee = request.ParkingFee;
            property.PetRent = request.PetRent;
            property.Deposit = request.Deposit;
        }
        else
        {
            property.Rent = request.Rent ?? property.Rent;
            property.ParkingFee = request.ParkingFee ?? property.ParkingFee;
            property.PetRent = request.PetRent ?? property.PetRent;
            property.Deposit = request.Deposit ?? property.Deposit;
        }

        if (request.Latitude is { } latitude)
        {
            property.Latitude = latitude;
        }

        if (request.Longitude is { } longitude)
        {
            property.Longitude = longitude;
        }

        if (request.Utilities is not null)
        {
            context.PropertyUtilities.RemoveRange(property.Utilities);
            property.Utilities = Utilities(request.Utilities);
        }

        if (request.Amenities is not null)
        {
            context.PropertyAmenities.RemoveRange(property.Amenities);
            property.Amenities = Amenities(request.Amenities);
        }

        if (request.Points is not null)
        {
            context.PropertyPoints.RemoveRange(property.Points);
            property.Points = Points(request.Points);
        }

        await context.SaveChangesAsync(cancellationToken);

        if (property.IsCurrent)
        {
            await LeaveOneCurrentAsync(property.Id, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a property and everything hanging off it. Returns false when no property has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var property = await context.Properties.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (property is null)
        {
            return false;
        }

        context.Properties.Remove(property);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Writes a note on a property, or returns null when no property has that id.</summary>
    /// <exception cref="ArgumentException">The note is blank.</exception>
    public async Task<PropertyNoteView?> AddNoteAsync(int propertyId, string body, CancellationToken cancellationToken = default)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("A note needs something in it.", nameof(body));
        }

        if (!await context.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken))
        {
            return null;
        }

        var note = new PropertyNote
        {
            PropertyId = propertyId,
            Body = text,
            CreatedAt = clock.GetUtcNow(),
        };

        context.PropertyNotes.Add(note);
        await context.SaveChangesAsync(cancellationToken);

        return new PropertyNoteView(note.Id, note.Body, note.CreatedAt);
    }

    /// <summary>Deletes a note. Returns false when no note has that id.</summary>
    public async Task<bool> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default)
    {
        var note = await context.PropertyNotes.SingleOrDefaultAsync(n => n.Id == noteId, cancellationToken);
        if (note is null)
        {
            return false;
        }

        context.PropertyNotes.Remove(note);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Marks a property against a priority, or clears the marking when <paramref name="meets"/> is
    /// null. Returns false when the property or the priority is not there.
    /// </summary>
    /// <param name="propertyId">The property being marked.</param>
    /// <param name="priorityId">The priority it is marked against.</param>
    /// <param name="meets">Whether it meets the priority, or null to go back to unmarked.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<bool> MarkPriorityAsync(
        int propertyId,
        int priorityId,
        bool? meets,
        CancellationToken cancellationToken = default)
    {
        if (!await context.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken)
            || !await context.SearchPriorities.AnyAsync(p => p.Id == priorityId, cancellationToken))
        {
            return false;
        }

        var held = await context.PropertyPriorities
            .SingleOrDefaultAsync(m => m.PropertyId == propertyId && m.PriorityId == priorityId, cancellationToken);

        if (meets is null)
        {
            if (held is not null)
            {
                context.PropertyPriorities.Remove(held);
            }
        }
        else if (held is null)
        {
            context.PropertyPriorities.Add(new PropertyPriority
            {
                PropertyId = propertyId,
                PriorityId = priorityId,
                Meets = meets.Value,
            });
        }
        else
        {
            held.Meets = meets.Value;
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Records the drive from a property to a destination, replacing the time held. Returns false
    /// when the property or the destination is not there.
    /// </summary>
    /// <param name="propertyId">The property driven from.</param>
    /// <param name="destinationId">The destination driven to.</param>
    /// <param name="minutes">How long the drive takes.</param>
    /// <param name="miles">How far it is, when known.</param>
    /// <param name="source">Whether the time was measured or typed in.</param>
    /// <param name="tolledRoads">Names of the tolled roads the drive uses.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<bool> SetDriveTimeAsync(
        int propertyId,
        int destinationId,
        double minutes,
        double? miles,
        DriveTimeSource source,
        IReadOnlyList<string>? tolledRoads = null,
        CancellationToken cancellationToken = default)
    {
        if (!await context.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken)
            || !await context.SearchDestinations.AnyAsync(d => d.Id == destinationId, cancellationToken))
        {
            return false;
        }

        var held = await context.PropertyDriveTimes
            .SingleOrDefaultAsync(d => d.PropertyId == propertyId && d.DestinationId == destinationId, cancellationToken);

        if (held is null)
        {
            held = new PropertyDriveTime { PropertyId = propertyId, DestinationId = destinationId };
            context.PropertyDriveTimes.Add(held);
        }

        held.Minutes = minutes;
        held.Miles = miles;
        held.TolledRoads = (tolledRoads ?? [])
            .Select(road => road.Trim())
            .Where(road => road.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        held.Source = source;
        held.MeasuredAt = clock.GetUtcNow();

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Forgets the drive from a property to a destination. Returns false when none was held.</summary>
    public async Task<bool> ClearDriveTimeAsync(int propertyId, int destinationId, CancellationToken cancellationToken = default)
    {
        var held = await context.PropertyDriveTimes
            .SingleOrDefaultAsync(d => d.PropertyId == propertyId && d.DestinationId == destinationId, cancellationToken);

        if (held is null)
        {
            return false;
        }

        context.PropertyDriveTimes.Remove(held);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Only one home is lived in, so marking a property current unmarks whichever held it.
    private async Task LeaveOneCurrentAsync(int keep, CancellationToken cancellationToken)
    {
        var others = await context.Properties
            .Where(p => p.IsCurrent && p.Id != keep)
            .ToListAsync(cancellationToken);

        if (others.Count == 0)
        {
            return;
        }

        foreach (var other in others)
        {
            other.IsCurrent = false;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<PropertyUtility> Utilities(IReadOnlyList<PropertyUtilityInput>? lines) =>
        (lines ?? [])
            .Select(line => new PropertyUtility
            {
                Kind = line.Kind,
                Label = Blank(line.Label),
                MonthlyCost = line.MonthlyCost,
            })
            .ToList();

    private static List<PropertyAmenity> Amenities(IReadOnlyList<string>? names) =>
        (names ?? [])
            .Select(name => (name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Select((name, index) => new PropertyAmenity { Name = name, SortOrder = index })
            .ToList();

    private static List<PropertyPoint> Points(IReadOnlyList<PropertyPointInput>? points) =>
        (points ?? [])
            .Where(point => !string.IsNullOrWhiteSpace(point.Text))
            .Select((point, index) => new PropertyPoint
            {
                Kind = point.Kind,
                Text = point.Text.Trim(),
                SortOrder = index,
            })
            .ToList();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly Expression<Func<Property, PropertyView>> Projection = property => new PropertyView(
        property.Id,
        property.Name,
        property.Address,
        property.IsCurrent,
        property.Rent,
        property.ParkingFee,
        property.PetRent,
        property.Deposit,
        property.Internet,
        property.Latitude,
        property.Longitude,
        property.Utilities
            .OrderBy(u => u.Kind)
            .ThenBy(u => u.Id)
            .Select(u => new PropertyUtilityView(u.Id, u.Kind, u.Label, u.MonthlyCost))
            .ToList(),
        property.Amenities
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Id)
            .Select(a => new PropertyAmenityView(a.Id, a.Name, a.SortOrder))
            .ToList(),
        property.Points
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Select(p => new PropertyPointView(p.Id, p.Kind, p.Text, p.SortOrder))
            .ToList(),
        // Newest first by id, not by CreatedAt: SQLite refuses a DateTimeOffset in ORDER BY, and
        // the ids are handed out in the order the notes were written anyway.
        property.Notes
            .OrderByDescending(n => n.Id)
            .Select(n => new PropertyNoteView(n.Id, n.Body, n.CreatedAt))
            .ToList(),
        property.Photos
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Select(p => new PropertyPhotoView(
                p.Id, p.FileName, p.ContentType, p.ByteCount, p.Caption, p.SortOrder, p.CreatedAt))
            .ToList(),
        property.DriveTimes
            .OrderBy(d => d.Destination!.SortOrder)
            .ThenBy(d => d.DestinationId)
            .Select(d => new PropertyDriveTimeView(
                d.DestinationId, d.Destination!.Name, d.Minutes, d.Miles, d.TolledRoads, d.Source, d.MeasuredAt))
            .ToList(),
        property.Priorities
            .OrderBy(m => m.Priority!.SortOrder)
            .ThenBy(m => m.PriorityId)
            .Select(m => new PropertyPriorityView(m.PriorityId, m.Priority!.Name, m.Priority!.Points, m.Meets))
            .ToList(),
        property.CreatedAt,
        property.UpdatedAt);
}
