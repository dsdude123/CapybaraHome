namespace CapybaraHome.Application.Homes;

/// <summary>What one destination adds to a week's driving.</summary>
/// <param name="DestinationId">The destination driven to.</param>
/// <param name="Name">What that place is called.</param>
/// <param name="Miles">How far the drive is one way, or null when it is not known.</param>
/// <param name="RoundTripsPerWeek">How many round trips a week are driven to it.</param>
/// <param name="MilesPerWeek">The round trips together, in miles.</param>
public record FuelCostLine(
    int DestinationId,
    string Name,
    double? Miles,
    int RoundTripsPerWeek,
    double MilesPerWeek);

/// <summary>What the week's driving costs in one car.</summary>
/// <param name="VehicleId">The car.</param>
/// <param name="Name">What the car is called.</param>
/// <param name="GallonsPerWeek">Gallons the week's driving burns.</param>
/// <param name="WeeklyCost">What those gallons cost.</param>
/// <param name="MonthlyCost">The same driving over an average month.</param>
public record VehicleFuelCost(
    int VehicleId,
    string Name,
    double GallonsPerWeek,
    decimal WeeklyCost,
    decimal MonthlyCost);

/// <summary>What driving from one property costs in fuel.</summary>
/// <param name="MilesPerWeek">Miles a week, over every destination.</param>
/// <param name="Lines">Where those miles go, one line per destination.</param>
/// <param name="Vehicles">The cost in each car.</param>
/// <param name="MissingMiles">Destinations driven to whose distance is not known.</param>
public record PropertyFuelCost(
    double MilesPerWeek,
    IReadOnlyList<FuelCostLine> Lines,
    IReadOnlyList<VehicleFuelCost> Vehicles,
    IReadOnlyList<string> MissingMiles)
{
    /// <summary>Whether there is a week's driving to cost at all.</summary>
    public bool AnyDriving => MilesPerWeek > 0d;

    /// <summary>The cheapest car's monthly cost, which is the one figure worth a single line.</summary>
    public decimal? LowestMonthlyCost =>
        Vehicles.Count == 0 ? null : Vehicles.Min(v => v.MonthlyCost);
}

/// <summary>
/// Works out what a week of the usual driving costs in fuel, from the distance of each drive, how
/// many round trips a week are made to it, and each car's mileage and fuel price.
/// </summary>
public static class FuelCostCalculator
{
    /// <summary>Weeks in an average month, which turns a week's fuel into a monthly figure.</summary>
    private const decimal WeeksPerMonth = 52m / 12m;

    /// <summary>Costs the usual week's driving from <paramref name="property"/>.</summary>
    public static PropertyFuelCost Cost(
        PropertyView property,
        IReadOnlyList<SearchDestinationView> destinations,
        IReadOnlyList<VehicleView> vehicles)
    {
        var lines = new List<FuelCostLine>();
        var missing = new List<string>();

        foreach (var destination in destinations.Where(d => d.RoundTripsPerWeek > 0))
        {
            var miles = property.DriveTimes
                .FirstOrDefault(d => d.DestinationId == destination.Id)?.Miles;

            if (miles is null)
            {
                missing.Add(destination.Name);
            }

            lines.Add(new FuelCostLine(
                destination.Id,
                destination.Name,
                miles,
                destination.RoundTripsPerWeek,
                (miles ?? 0d) * 2d * destination.RoundTripsPerWeek));
        }

        var milesPerWeek = lines.Sum(l => l.MilesPerWeek);

        var costs = vehicles
            .Where(v => v.CanCost)
            .Select(vehicle =>
            {
                var gallons = milesPerWeek / vehicle.MilesPerGallon;
                var weekly = (decimal)gallons * vehicle.FuelPricePerGallon;
                return new VehicleFuelCost(vehicle.Id, vehicle.Name, gallons, weekly, weekly * WeeksPerMonth);
            })
            .ToList();

        return new PropertyFuelCost(milesPerWeek, lines, costs, missing);
    }
}
