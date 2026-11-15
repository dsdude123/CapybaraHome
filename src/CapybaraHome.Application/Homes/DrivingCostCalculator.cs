namespace CapybaraHome.Application.Homes;

/// <summary>What one destination adds to a week's driving.</summary>
/// <param name="DestinationId">The destination driven to.</param>
/// <param name="Name">What that place is called.</param>
/// <param name="Miles">How far the drive is one way, or null when it is not known.</param>
/// <param name="RoundTripsPerWeek">How many round trips a week are driven to it.</param>
/// <param name="MilesPerWeek">The round trips together, in miles.</param>
/// <param name="Cars">The cars the fuel is reckoned in: the one chosen, or every one that can be costed.</param>
/// <param name="GallonsPerWeek">Gallons the week's trips burn, averaged over those cars.</param>
/// <param name="FuelPerWeek">What those gallons cost.</param>
/// <param name="Tolls">The toll roads the drive crosses.</param>
/// <param name="TollsPerWeek">Those tolls over the week's round trips, paid each way.</param>
/// <param name="CarpoolCreditPerWeek">What the week's days there pay back.</param>
/// <param name="UnpricedTolledRoads">Tolled roads on the drive that no toll road prices.</param>
public record DrivingCostLine(
    int DestinationId,
    string Name,
    double? Miles,
    decimal RoundTripsPerWeek,
    double MilesPerWeek,
    IReadOnlyList<string> Cars,
    double GallonsPerWeek,
    decimal FuelPerWeek,
    IReadOnlyList<TollRoadView> Tolls,
    decimal TollsPerWeek,
    decimal CarpoolCreditPerWeek,
    IReadOnlyList<string> UnpricedTolledRoads)
{
    /// <summary>The week's tolls with the carpool credit taken off, which is what the drive is owed.</summary>
    public decimal TollsLessCreditPerWeek => TollsPerWeek - CarpoolCreditPerWeek;

    /// <summary>The fuel and the tolls, less the carpool credit.</summary>
    public decimal WeeklyCost => FuelPerWeek + TollsLessCreditPerWeek;

    /// <summary>Whether the fuel is shared out over every car rather than charged to one.</summary>
    public bool IsAveraged => Cars.Count > 1;
}

/// <summary>What the usual week's driving from one property costs.</summary>
/// <param name="MilesPerWeek">Miles a week, over every destination.</param>
/// <param name="GallonsPerWeek">Gallons a week, over every destination.</param>
/// <param name="FuelPerWeek">What those gallons cost.</param>
/// <param name="TollsPerWeek">Tolls a week, over every destination.</param>
/// <param name="CarpoolCreditPerWeek">Carpool credit a week, over every destination.</param>
/// <param name="Lines">Where the week goes, one line per destination.</param>
/// <param name="MissingMiles">Destinations driven to whose distance is not known.</param>
/// <param name="MissingCars">Destinations driven to with no car that can be costed.</param>
public record PropertyDrivingCost(
    double MilesPerWeek,
    double GallonsPerWeek,
    decimal FuelPerWeek,
    decimal TollsPerWeek,
    decimal CarpoolCreditPerWeek,
    IReadOnlyList<DrivingCostLine> Lines,
    IReadOnlyList<string> MissingMiles,
    IReadOnlyList<string> MissingCars)
{
    /// <summary>Whether there is a week's driving to cost at all.</summary>
    public bool AnyDriving => MilesPerWeek > 0d || TollsPerWeek > 0m || CarpoolCreditPerWeek > 0m;

    /// <summary>The tolls with the carpool credit taken off, which no car changes.</summary>
    public decimal TollsLessCreditPerWeek => TollsPerWeek - CarpoolCreditPerWeek;

    /// <summary>The same over an average month.</summary>
    public decimal MonthlyTollsLessCredit => TollsLessCreditPerWeek * DrivingCostCalculator.WeeksPerMonth;

    /// <summary>The fuel and the tolls, less the carpool credit.</summary>
    public decimal WeeklyCost => FuelPerWeek + TollsLessCreditPerWeek;

    /// <summary>What a month of that driving costs, which is the figure the score weighs.</summary>
    public decimal MonthlyCost => WeeklyCost * DrivingCostCalculator.WeeksPerMonth;
}

/// <summary>
/// Works out what a week of the usual driving costs: the fuel, from the distance of each drive,
/// how many round trips a week are made to it, and the mileage and fuel price of the car it is
/// made in — averaged over every car that can be costed where whoever drives varies; the tolls on
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
        var missingMiles = new List<string>();
        var missingCars = new List<string>();

        var costable = vehicles.Where(v => v.CanCost).ToList();

        foreach (var destination in destinations.Where(d => d.RoundTripsPerWeek > 0m))
        {
            var drive = property.DriveTimes.FirstOrDefault(d => d.DestinationId == destination.Id);
            var miles = drive?.Miles;
            var tolled = drive?.TolledRoads ?? [];

            if (miles is null)
            {
                missingMiles.Add(destination.Name);
            }

            // A drive can be marked as crossing toll roads the measured route did not use, for a
            // route taken in practice; those marks then stand in for what the route reported.
            // Two names on one route can both belong to the same toll, which is still paid once.
            var tolls = drive?.TollRoadOverrideIds is { } chosen
                ? tollRoads.Where(toll => chosen.Contains(toll.Id)).ToList()
                : tollRoads.Where(toll => tolled.Any(toll.Prices)).ToList();

            var unpriced = drive?.TollRoadOverrideIds is null
                ? tolled.Where(road => !tollRoads.Any(toll => toll.Prices(road))).ToList()
                : [];

            var trips = destination.RoundTripsPerWeek;
            var milesPerWeek = (miles ?? 0d) * 2d * (double)trips;

            // A drive with no car of its own is shared out: whoever drives varies, so the fuel is
            // what it averages over the cars, rather than any one of them.
            var cars = destination.VehicleId is { } carId
                ? costable.Where(v => v.Id == carId).ToList()
                : costable;

            if (cars.Count == 0)
            {
                missingCars.Add(destination.Name);
            }

            var gallons = cars.Count == 0 ? 0d : cars.Average(car => milesPerWeek / car.MilesPerGallon);
            var fuel = cars.Count == 0
                ? 0m
                : cars.Average(car => (decimal)(milesPerWeek / car.MilesPerGallon) * car.FuelPricePerGallon);

            lines.Add(new DrivingCostLine(
                destination.Id,
                destination.Name,
                miles,
                trips,
                milesPerWeek,
                cars.Select(car => car.Name).ToList(),
                gallons,
                fuel,
                tolls,
                tolls.Sum(t => t.PeakToll) * 2m * trips,
                destination.CarpoolCreditPerDay * trips,
                unpriced));
        }

        return new PropertyDrivingCost(
            lines.Sum(l => l.MilesPerWeek),
            lines.Sum(l => l.GallonsPerWeek),
            lines.Sum(l => l.FuelPerWeek),
            lines.Sum(l => l.TollsPerWeek),
            lines.Sum(l => l.CarpoolCreditPerWeek),
            lines,
            missingMiles,
            missingCars);
    }
}
