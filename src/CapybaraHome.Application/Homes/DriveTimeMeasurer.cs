using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Application.Homes;

/// <summary>What measuring a property's drives did.</summary>
/// <param name="Measured">How many destinations were measured.</param>
/// <param name="Problems">What stopped the rest, one line each.</param>
public record DriveTimeOutcome(int Measured, IReadOnlyList<string> Problems);

/// <summary>
/// Measures every drive from one property, placing whatever address has not been placed yet
/// along the way. One destination failing does not stop the others.
/// </summary>
public class DriveTimeMeasurer(
    PropertyService properties,
    HomeSearchService search,
    RoutingClient routing)
{
    /// <summary>Measures the drives from <paramref name="propertyId"/> to every destination.</summary>
    /// <exception cref="RoutingException">No routing service is set up.</exception>
    public async Task<DriveTimeOutcome?> MeasureAsync(int propertyId, CancellationToken cancellationToken = default)
    {
        var property = await properties.GetAsync(propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var destinations = await search.ListDestinationsAsync(cancellationToken);
        if (destinations.Count == 0)
        {
            return new DriveTimeOutcome(0, ["There are no destinations to measure the drive to yet."]);
        }

        var problems = new List<string>();

        var from = await PlacePropertyAsync(property, problems, cancellationToken);
        if (from is null)
        {
            return new DriveTimeOutcome(0, problems);
        }

        var measured = 0;

        foreach (var destination in destinations)
        {
            var to = await PlaceDestinationAsync(destination, problems, cancellationToken);
            if (to is null)
            {
                continue;
            }

            try
            {
                var drive = await routing.MeasureAsync(
                    from.Value.Latitude, from.Value.Longitude, to.Value.Latitude, to.Value.Longitude, cancellationToken);

                if (drive is null)
                {
                    problems.Add($"{destination.Name}: the routing service found no road route.");
                    continue;
                }

                await properties.SetDriveTimeAsync(
                    propertyId, destination.Id, drive.Minutes, drive.Miles, DriveTimeSource.Measured, cancellationToken);

                measured++;
            }
            catch (RoutingException ex)
            {
                problems.Add($"{destination.Name}: {ex.Message}");
            }
        }

        return new DriveTimeOutcome(measured, problems);
    }

    private async Task<(double Latitude, double Longitude)?> PlacePropertyAsync(
        PropertyView property,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (property.Latitude is { } held && property.Longitude is { } heldLongitude)
        {
            return (held, heldLongitude);
        }

        if (string.IsNullOrWhiteSpace(property.Address))
        {
            problems.Add($"{property.Name} has no address, so there is nowhere to drive from.");
            return null;
        }

        var placed = await routing.PlaceAsync(property.Address, cancellationToken);
        if (placed is null)
        {
            problems.Add($"The geocoder did not recognise {property.Name}'s address.");
            return null;
        }

        await properties.UpdateAsync(
            property.Id,
            new UpdatePropertyRequest(Latitude: placed.Latitude, Longitude: placed.Longitude),
            cancellationToken);

        return (placed.Latitude, placed.Longitude);
    }

    private async Task<(double Latitude, double Longitude)?> PlaceDestinationAsync(
        SearchDestinationView destination,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (destination.Latitude is { } held && destination.Longitude is { } heldLongitude)
        {
            return (held, heldLongitude);
        }

        if (string.IsNullOrWhiteSpace(destination.Address))
        {
            problems.Add($"{destination.Name} has no address, so there is nowhere to drive to.");
            return null;
        }

        try
        {
            var placed = await routing.PlaceAsync(destination.Address, cancellationToken);
            if (placed is null)
            {
                problems.Add($"The geocoder did not recognise {destination.Name}'s address.");
                return null;
            }

            await search.PlaceDestinationAsync(destination.Id, placed.Latitude, placed.Longitude, cancellationToken);
            return (placed.Latitude, placed.Longitude);
        }
        catch (RoutingException ex)
        {
            problems.Add($"{destination.Name}: {ex.Message}");
            return null;
        }
    }
}
