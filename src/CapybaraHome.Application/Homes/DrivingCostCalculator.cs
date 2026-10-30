namespace CapybaraHome.Application.Homes;

/// <summary>What one destination adds to a week's driving.</summary>
/// <param name="DestinationId">The destination driven to.</param>
/// <param name="Name">What that place is called.</param>
/// <param name="Miles">How far the drive is one way, or null when it is not known.</param>
/// <param name="RoundTripsPerWeek">How many round trips a week are driven to it.</param>
/// <param name="MilesPerWeek">The round trips together, in miles.</param>
/// <param name="Tolls">The toll roads the drive crosses.</param>
/// <param name="TollsPerWeek">Those tolls over the week's round trips, paid each way.</param>
/// <param name="CarpoolCreditPerWeek">What the week's days there pay back.</param>
/// <param name="UnpricedTolledRoads">Tolled roads on the drive that no toll road prices.</param>
public record DrivingCostLine(
    int DestinationId,
    string Name,
    double? Miles,
    int RoundTripsPerWeek,
    double MilesPerWeek,
    IReadOnlyList<TollRoadView> Tolls,
    decimal TollsPerWeek,
    decimal CarpoolCreditPerWeek,
    IReadOnlyList<string> UnpricedTolledRoads);

/// <summary>What the week's driving costs in one car.</summary>
/// <param name="VehicleId">The car.</param>
/// <param name="Name">What the car is called.</param>
/// <param name="GallonsPerWeek">Gallons the week's driving burns.</param>
/// <param name="FuelPerWeek">What those gallons cost.</param>
/// <param name="WeeklyCost">The fuel and the tolls, less the carpool credit.</param>
/// <param name="MonthlyCost">The same over an average month.</param>
public record VehicleDrivingCost(
    int VehicleId,
    string Name,
    double GallonsPerWeek,
    decimal FuelPerWeek,
    decimal WeeklyCost,
    decimal MonthlyCost);

/// <summary>What the usual week's driving from one property costs.</summary>
/// <param name="MilesPerWeek">Miles a week, over every destination.</param>
/// <param name="TollsPerWeek">Tolls a week, over every destination.</param>
/// <param name="CarpoolCreditPerWeek">Carpool credit a week, over every destination.</param>
/// <param name="Lines">Where the week goes, one line per destination.</param>
/// <param name="Vehicles">The cost in each car.</param>
/// <param name="MissingMiles">Destinations driven to whose distance is not known.</param>
public record PropertyDrivingCost(
    double MilesPerWeek,
    decimal TollsPerWeek,
    decimal CarpoolCreditPerWeek,
    IReadOnlyList<DrivingCostLine> Lines,
    IReadOnlyList<VehicleDrivingCost> Vehicles,
    IReadOnlyList<string> MissingMiles)
{
    /// <summary>Whether there is a week's driving to cost at all.</summary>
    public bool AnyDriving => MilesPerWeek > 0d || TollsPerWeek > 0m || CarpoolCreditPerWeek > 0m;

    /// <summary>The cheapest car's monthly cost, which is the one figure worth a single line.</summary>
    public decimal? LowestMonthlyCost =>
        Vehicles.Count == 0 ? null : Vehicles.Min(v => v.MonthlyCost);

    /// <summary>The tolls and the carpool credit together over a month, which are the same in every car.</summary>
    public decimal MonthlyTollsLessCredit => (TollsPerWeek - CarpoolCreditPerWeek) * DrivingCostCalculator.WeeksPerMonth;
}

/// <summary>
/// Works out what a week of the usual driving costs: the fuel, from the distance of each drive,
/// how many round trips a week are made to it, and each car's mileage and fuel price; the tolls on
/// the roads each drive crosses, paid each way; and less what carpooling there pays back a day.
/// </summary>
public static class DrivingCostCalculator
{
    /// <summary>Weeks in an average month, which turns a week into a monthly figure.</summary>
    public const decimal WeeksPerMonth = 52m / 12m;

    /// <summary>Costs the usual week's driving from <paramref name="property"/>.</summary>
    public static PropertyDrivingCost Cost(
        PropertyView property,
        IReadOnlyList<SearchDestinationView> destinations,
        IReadOnlyList<VehicleView> vehicles,
        IReadOnlyList<TollRoadView> tollRoads)
    {
        var lines = new List<DrivingCostLine>();
        var missing = new List<string>();

        foreach (var destination in destinations.Where(d => d.RoundTripsPerWeek > 0))
        {
            var drive = property.DriveTimes.FirstOrDefault(d => d.DestinationId == destination.Id);
            var miles = drive?.Miles;
            var tolled = drive?.TolledRoads ?? [];

            if (miles is null)
            {
                missing.Add(destination.Name);
            }

            // Two names on one route can both belong to the same toll, which is still paid once.
            var tolls = tollRoads.Where(toll => tolled.Any(toll.Prices)).ToList();
            var unpriced = tolled.Where(road => !tollRoads.Any(toll => toll.Prices(road))).ToList();

            var trips = destination.RoundTripsPerWeek;

            lines.Add(new DrivingCostLine(
                destination.Id,
                destination.Name,
                miles,
                trips,
                (miles ?? 0d) * 2d * trips,
                tolls,
                tolls.Sum(t => t.PeakToll) * 2m * trips,
                destination.CarpoolCreditPerDay * trips,
                unpriced));
        }

        var milesPerWeek = lines.Sum(l => l.MilesPerWeek);
        var tollsPerWeek = lines.Sum(l => l.TollsPerWeek);
        var creditPerWeek = lines.Sum(l => l.CarpoolCreditPerWeek);

        var costs = vehicles
            .Where(v => v.CanCost)
            .Select(vehicle =>
            {
                var gallons = milesPerWeek / vehicle.MilesPerGallon;
                var fuel = (decimal)gallons * vehicle.FuelPricePerGallon;
                var weekly = fuel + tollsPerWeek - creditPerWeek;
                return new VehicleDrivingCost(vehicle.Id, vehicle.Name, gallons, fuel, weekly, weekly * WeeksPerMonth);
            })
            .ToList();

        return new PropertyDrivingCost(milesPerWeek, tollsPerWeek, creditPerWeek, lines, costs, missing);
    }
}
