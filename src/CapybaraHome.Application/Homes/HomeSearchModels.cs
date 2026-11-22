using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Application.Homes;

/// <summary>One line of a property's utility breakdown as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Kind">Which utility it is.</param>
/// <param name="Label">What the line is called, which names an "other" line.</param>
/// <param name="MonthlyCost">The average monthly cost.</param>
public record PropertyUtilityView(int Id, UtilityKind Kind, string? Label, decimal MonthlyCost);

/// <summary>An amenity as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the amenity is.</param>
/// <param name="SortOrder">Position in the list.</param>
public record PropertyAmenityView(int Id, string Name, int SortOrder);

/// <summary>A pro or a con as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Kind">Which way the point cuts.</param>
/// <param name="Text">What the point is.</param>
/// <param name="SortOrder">Position in its list.</param>
public record PropertyPointView(int Id, PropertyPointKind Kind, string Text, int SortOrder);

/// <summary>A note as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Body">What was written, held as markdown.</param>
/// <param name="CreatedAt">When it was written.</param>
public record PropertyNoteView(int Id, string Body, DateTimeOffset CreatedAt);

/// <summary>A picture as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="FileName">Name the file was uploaded under.</param>
/// <param name="ContentType">Media type the file is served as.</param>
/// <param name="ByteCount">How large the file is.</param>
/// <param name="Caption">What the picture shows.</param>
/// <param name="SortOrder">Position in the property's pictures.</param>
/// <param name="CreatedAt">When it was uploaded.</param>
public record PropertyPhotoView(
    int Id,
    string FileName,
    string ContentType,
    long ByteCount,
    string? Caption,
    int SortOrder,
    DateTimeOffset CreatedAt);

/// <summary>A drive time as the API and the UI show it.</summary>
/// <param name="DestinationId">The destination driven to.</param>
/// <param name="DestinationName">What that place is called.</param>
/// <param name="Minutes">How long the drive takes.</param>
/// <param name="Miles">How far it is, when it was measured or typed in.</param>
/// <param name="TolledRoads">Names of the tolled roads a measured route reported.</param>
/// <param name="Source">Whether the time was measured or typed in.</param>
/// <param name="MeasuredAt">When it was measured or last typed.</param>
/// <param name="AdjustedMinutes">The minutes the drive is reckoned to take, its destination's multiplier applied.</param>
/// <param name="TollRoadOverrideIds">Toll roads charged whatever the route reported, or null to follow it.</param>
/// <param name="ParkingPerDayOverride">What parking costs a day on this drive, or null to follow the destination.</param>
/// <param name="CommuteCreditPerDayOverride">What a day on this drive pays back, or null to follow the destination.</param>
public record PropertyDriveTimeView(
    int DestinationId,
    string DestinationName,
    double Minutes,
    double? Miles,
    IReadOnlyList<string> TolledRoads,
    DriveTimeSource Source,
    DateTimeOffset MeasuredAt,
    double AdjustedMinutes,
    IReadOnlyList<int>? TollRoadOverrideIds,
    decimal? ParkingPerDayOverride = null,
    decimal? CommuteCreditPerDayOverride = null)
{
    /// <summary>Whether the minutes shown differ from the ones measured or typed.</summary>
    public bool IsAdjusted => Math.Abs(AdjustedMinutes - Minutes) > 0.005d;

    /// <summary>Whether this drive charges its own parking or credit rather than the destination's.</summary>
    public bool HasFeeOverride => ParkingPerDayOverride is not null || CommuteCreditPerDayOverride is not null;
}

/// <summary>How a property was marked against one priority.</summary>
/// <param name="PriorityId">The priority.</param>
/// <param name="Name">What the priority is.</param>
/// <param name="Points">Points meeting it is worth.</param>
/// <param name="Meets">Whether the property meets it, or null when it has not been marked.</param>
/// <param name="Penalty">Points not meeting it takes off; zero for none.</param>
public record PropertyPriorityView(int PriorityId, string Name, decimal Points, bool? Meets, decimal Penalty = 0m);

/// <summary>A property as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the place is called.</param>
/// <param name="Address">Street address.</param>
/// <param name="IsCurrent">Whether this is the home lived in now.</param>
/// <param name="Rent">Monthly rent.</param>
/// <param name="ParkingFee">Monthly parking fee.</param>
/// <param name="PetRent">Monthly pet rent.</param>
/// <param name="Deposit">Deposit, which is paid once.</param>
/// <param name="Internet">What internet the place provides, as written.</param>
/// <param name="ListingUrl">Web address of the listing it was found on, or null.</param>
/// <param name="SquareFeet">How big the place is, in square feet.</param>
/// <param name="Latitude">Latitude the address was resolved to, or null.</param>
/// <param name="Longitude">Longitude the address was resolved to, or null.</param>
/// <param name="Utilities">The monthly utilities, by kind.</param>
/// <param name="Amenities">What the place offers.</param>
/// <param name="Points">The written pros and cons.</param>
/// <param name="Notes">Notes written about it, newest first.</param>
/// <param name="Photos">Pictures of it, in order.</param>
/// <param name="DriveTimes">The drive times to the destinations.</param>
/// <param name="Priorities">How it was marked against the priorities.</param>
/// <param name="CreatedAt">When the property was added.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record PropertyView(
    int Id,
    string Name,
    string? Address,
    bool IsCurrent,
    decimal? Rent,
    decimal? ParkingFee,
    decimal? PetRent,
    decimal? Deposit,
    string? Internet,
    string? ListingUrl,
    int? SquareFeet,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<PropertyUtilityView> Utilities,
    IReadOnlyList<PropertyAmenityView> Amenities,
    IReadOnlyList<PropertyPointView> Points,
    IReadOnlyList<PropertyNoteView> Notes,
    IReadOnlyList<PropertyPhotoView> Photos,
    IReadOnlyList<PropertyDriveTimeView> DriveTimes,
    IReadOnlyList<PropertyPriorityView> Priorities,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>What the utilities add up to each month.</summary>
    public decimal UtilitiesTotal => Utilities.Sum(u => u.MonthlyCost);

    /// <summary>Rent, parking, pet rent and the utilities together, which is what a month costs.</summary>
    public decimal MonthlyTotal =>
        (Rent ?? 0m) + (ParkingFee ?? 0m) + (PetRent ?? 0m) + UtilitiesTotal;

    /// <summary>The rent divided by the square footage, or null when either is unknown.</summary>
    public decimal? RentPerSquareFoot =>
        Rent is { } rent && SquareFeet is > 0 ? rent / SquareFeet.Value : null;

    /// <summary>The pros, in order.</summary>
    public IEnumerable<PropertyPointView> Pros => Points.Where(p => p.Kind == PropertyPointKind.Pro);

    /// <summary>The cons, in order.</summary>
    public IEnumerable<PropertyPointView> Cons => Points.Where(p => p.Kind == PropertyPointKind.Con);
}

/// <summary>One line of a utility breakdown as it is written.</summary>
/// <param name="Kind">Which utility it is.</param>
/// <param name="MonthlyCost">The average monthly cost.</param>
/// <param name="Label">What the line is called, for an "other" line.</param>
public record PropertyUtilityInput(UtilityKind Kind, decimal MonthlyCost, string? Label = null);

/// <summary>A pro or a con as it is written.</summary>
/// <param name="Kind">Which way the point cuts.</param>
/// <param name="Text">What the point is.</param>
public record PropertyPointInput(PropertyPointKind Kind, string Text);

/// <summary>Fields accepted when creating a property.</summary>
/// <param name="Name">What the place is called; required.</param>
/// <param name="Address">Street address.</param>
/// <param name="IsCurrent">Whether this is the home lived in now.</param>
/// <param name="Rent">Monthly rent.</param>
/// <param name="ParkingFee">Monthly parking fee.</param>
/// <param name="PetRent">Monthly pet rent.</param>
/// <param name="Deposit">Deposit, which is paid once.</param>
/// <param name="Internet">What internet the place provides.</param>
/// <param name="ListingUrl">Web address of the listing it was found on, over http or https.</param>
/// <param name="SquareFeet">How big the place is, in square feet.</param>
/// <param name="Utilities">The utility breakdown.</param>
/// <param name="Amenities">What the place offers, in the order given.</param>
/// <param name="Points">The pros and cons, in the order given.</param>
public record CreatePropertyRequest(
    string Name,
    string? Address = null,
    bool IsCurrent = false,
    decimal? Rent = null,
    decimal? ParkingFee = null,
    decimal? PetRent = null,
    decimal? Deposit = null,
    string? Internet = null,
    string? ListingUrl = null,
    int? SquareFeet = null,
    IReadOnlyList<PropertyUtilityInput>? Utilities = null,
    IReadOnlyList<string>? Amenities = null,
    IReadOnlyList<PropertyPointInput>? Points = null);

/// <summary>Fields accepted when updating a property. A null member leaves that field alone.</summary>
/// <param name="Name">New name.</param>
/// <param name="Address">New address; pass an empty string to clear it.</param>
/// <param name="IsCurrent">Whether this is the home lived in now.</param>
/// <param name="Rent">New monthly rent.</param>
/// <param name="ParkingFee">New monthly parking fee.</param>
/// <param name="PetRent">New monthly pet rent.</param>
/// <param name="Deposit">New deposit.</param>
/// <param name="ClearMoney">When true, the money fields and the square footage left null in this request are cleared.</param>
/// <param name="Internet">New internet description; pass an empty string to clear it.</param>
/// <param name="ListingUrl">New listing address; pass an empty string to clear it.</param>
/// <param name="SquareFeet">New size in square feet.</param>
/// <param name="Utilities">The whole utility breakdown, replacing the one held.</param>
/// <param name="Amenities">The whole list of amenities, replacing the one held.</param>
/// <param name="Points">The whole list of pros and cons, replacing the one held.</param>
/// <param name="Latitude">Latitude the address resolved to.</param>
/// <param name="Longitude">Longitude the address resolved to.</param>
public record UpdatePropertyRequest(
    string? Name = null,
    string? Address = null,
    bool? IsCurrent = null,
    decimal? Rent = null,
    decimal? ParkingFee = null,
    decimal? PetRent = null,
    decimal? Deposit = null,
    bool ClearMoney = false,
    string? Internet = null,
    string? ListingUrl = null,
    int? SquareFeet = null,
    IReadOnlyList<PropertyUtilityInput>? Utilities = null,
    IReadOnlyList<string>? Amenities = null,
    IReadOnlyList<PropertyPointInput>? Points = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>A scenario of the search as the pages see it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the scenario is called.</param>
/// <param name="IsActive">Whether this is the profile the pages are showing.</param>
/// <param name="SortOrder">Position in the list.</param>
/// <param name="DestinationCount">How many destinations it drives to.</param>
/// <param name="VehicleCount">How many cars it drives.</param>
/// <param name="TollRoadCount">How many toll roads it prices.</param>
public record SearchProfileView(
    int Id,
    string Name,
    bool IsActive,
    int SortOrder,
    int DestinationCount,
    int VehicleCount,
    int TollRoadCount);

/// <summary>Fields accepted when creating or updating a destination.</summary>
/// <param name="Name">What the place is called; required.</param>
/// <param name="Address">Street address.</param>
/// <param name="Weight">How much its drive counts next to the others.</param>
/// <param name="RoundTripsPerWeek">How many round trips a week are driven to it; fractions are allowed.</param>
/// <param name="CommuteCreditPerDay">What each day driven there pays back.</param>
/// <param name="DriveTimeMultiplier">What every drive to it is multiplied by; one leaves the times alone.</param>
/// <param name="VehicleId">The car the drive is made in, or null to average every car that can be costed.</param>
/// <param name="ParkingPerDay">What parking there costs for a day.</param>
public record SearchDestinationRequest(
    string Name,
    string? Address = null,
    decimal Weight = 1m,
    decimal RoundTripsPerWeek = 0m,
    decimal CommuteCreditPerDay = 0m,
    double DriveTimeMultiplier = 1d,
    int? VehicleId = null,
    decimal ParkingPerDay = 0m);

/// <summary>A destination as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the place is called.</param>
/// <param name="Address">Street address.</param>
/// <param name="Weight">How much its drive counts next to the others.</param>
/// <param name="RoundTripsPerWeek">How many round trips a week are driven to it.</param>
/// <param name="CommuteCreditPerDay">What each day driven there pays back.</param>
/// <param name="Latitude">Latitude the address was resolved to, or null.</param>
/// <param name="Longitude">Longitude the address was resolved to, or null.</param>
/// <param name="SortOrder">Position in the list.</param>
/// <param name="DriveTimeMultiplier">What every drive to it is multiplied by.</param>
/// <param name="VehicleId">The car the drive is made in, or null when it is averaged over them all.</param>
/// <param name="ParkingPerDay">What parking there costs for a day.</param>
public record SearchDestinationView(
    int Id,
    string Name,
    string? Address,
    decimal Weight,
    decimal RoundTripsPerWeek,
    decimal CommuteCreditPerDay,
    double? Latitude,
    double? Longitude,
    int SortOrder,
    double DriveTimeMultiplier = 1d,
    int? VehicleId = null,
    decimal ParkingPerDay = 0m);

/// <summary>Fields accepted when adding or changing a toll road.</summary>
/// <param name="Road">What to look for in the tolled roads' names; required.</param>
/// <param name="PeakToll">What one crossing costs at the peak.</param>
public record TollRoadRequest(string Road, decimal PeakToll = 0m);

/// <summary>A toll road as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Road">What to look for in the tolled roads' names.</param>
/// <param name="PeakToll">What one crossing costs at the peak.</param>
/// <param name="SortOrder">Position in the list.</param>
public record TollRoadView(int Id, string Road, decimal PeakToll, int SortOrder)
{
    /// <summary>Whether a tolled road with this name is one this toll prices.</summary>
    public bool Prices(string tolledRoad) =>
        tolledRoad.Contains(Road, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Fields accepted when adding or changing a car.</summary>
/// <param name="Name">What the car is called; required.</param>
/// <param name="MilesPerGallon">Average miles it does to the gallon.</param>
/// <param name="FuelPricePerGallon">What a gallon costs now.</param>
public record VehicleRequest(string Name, double MilesPerGallon = 0d, decimal FuelPricePerGallon = 0m);

/// <summary>A car as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the car is called.</param>
/// <param name="MilesPerGallon">Average miles it does to the gallon.</param>
/// <param name="FuelPricePerGallon">What a gallon costs now.</param>
/// <param name="SortOrder">Position in the list.</param>
public record VehicleView(
    int Id,
    string Name,
    double MilesPerGallon,
    decimal FuelPricePerGallon,
    int SortOrder)
{
    /// <summary>Whether the car can be costed, which needs both a mileage and a price.</summary>
    public bool CanCost => MilesPerGallon > 0d && FuelPricePerGallon > 0m;
}

/// <summary>Fields accepted when creating or updating a priority.</summary>
/// <param name="Name">What the priority is; required.</param>
/// <param name="Points">Points a property earns for meeting it.</param>
/// <param name="Penalty">Points a property loses when marked as not meeting it; zero for none.</param>
public record SearchPriorityRequest(string Name, decimal Points = 0m, decimal Penalty = 0m);

/// <summary>A priority as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the priority is.</param>
/// <param name="Points">Points a property earns for meeting it.</param>
/// <param name="SortOrder">Position in the list.</param>
/// <param name="MetCount">How many properties are marked as meeting it.</param>
/// <param name="Penalty">Points a property loses when marked as not meeting it; zero for none.</param>
public record SearchPriorityView(int Id, string Name, decimal Points, int SortOrder, int MetCount, decimal Penalty = 0m);

/// <summary>The scoring weights and the routing service, as the API and the UI show them.</summary>
/// <param name="MonthlyFeePointsPerDollar">Points per dollar a month cheaper than the current home.</param>
/// <param name="DepositPointsPerDollar">Points per dollar of deposit below the current home's.</param>
/// <param name="DriveTimePointsPerMinute">Points per minute of drive saved, before the destination's weight.</param>
/// <param name="AmenityPoints">Points for each amenity.</param>
/// <param name="ProPoints">Points for each pro.</param>
/// <param name="ConPoints">Points taken off for each con.</param>
/// <param name="SizePointsPerSquareFoot">Points per square foot larger than the current home.</param>
/// <param name="RoutingBaseUrl">Base address of the OpenRouteService instance.</param>
/// <param name="HasRoutingApiKey">Whether a key is held for it; the key itself is never read back.</param>
public record HomeSearchSettingsView(
    decimal MonthlyFeePointsPerDollar,
    decimal DepositPointsPerDollar,
    decimal DriveTimePointsPerMinute,
    decimal AmenityPoints,
    decimal ProPoints,
    decimal ConPoints,
    decimal SizePointsPerSquareFoot,
    string? RoutingBaseUrl,
    bool HasRoutingApiKey)
{
    /// <summary>Whether any weight has been set, which is what makes a score mean anything.</summary>
    public bool AnyWeightSet =>
        MonthlyFeePointsPerDollar != 0m
        || DepositPointsPerDollar != 0m
        || DriveTimePointsPerMinute != 0m
        || AmenityPoints != 0m
        || ProPoints != 0m
        || ConPoints != 0m
        || SizePointsPerSquareFoot != 0m;

    /// <summary>Whether addresses can be placed and drives measured rather than typed.</summary>
    public bool CanMeasureDriveTimes => !string.IsNullOrWhiteSpace(RoutingBaseUrl);
}

/// <summary>Fields accepted when saving the settings. A null member leaves that field alone.</summary>
/// <param name="MonthlyFeePointsPerDollar">Points per dollar a month cheaper than the current home.</param>
/// <param name="DepositPointsPerDollar">Points per dollar of deposit below the current home's.</param>
/// <param name="DriveTimePointsPerMinute">Points per minute of drive saved.</param>
/// <param name="AmenityPoints">Points for each amenity.</param>
/// <param name="ProPoints">Points for each pro.</param>
/// <param name="ConPoints">Points taken off for each con.</param>
/// <param name="SizePointsPerSquareFoot">Points per square foot larger than the current home.</param>
/// <param name="RoutingBaseUrl">Base address of the OpenRouteService instance; pass an empty string to clear it.</param>
/// <param name="RoutingApiKey">Key for it; an empty string keeps the key already held.</param>
public record HomeSearchSettingsRequest(
    decimal? MonthlyFeePointsPerDollar = null,
    decimal? DepositPointsPerDollar = null,
    decimal? DriveTimePointsPerMinute = null,
    decimal? AmenityPoints = null,
    decimal? ProPoints = null,
    decimal? ConPoints = null,
    decimal? SizePointsPerSquareFoot = null,
    string? RoutingBaseUrl = null,
    string? RoutingApiKey = null);

/// <summary>Fields accepted when writing a note on a property.</summary>
/// <param name="Body">What to write, held as markdown.</param>
public record PropertyNoteRequest(string Body);

/// <summary>Fields accepted when marking a property against a priority.</summary>
/// <param name="Meets">Whether it meets the priority, or null to go back to unmarked.</param>
public record PropertyPriorityRequest(bool? Meets);

/// <summary>What one drive charges itself, whatever its destination charges.</summary>
/// <param name="ParkingPerDay">What parking costs a day on this drive, or null to follow the destination.</param>
/// <param name="CommuteCreditPerDay">What a day on this drive pays back, or null to follow the destination.</param>
public record DriveFeeOverrides(decimal? ParkingPerDay, decimal? CommuteCreditPerDay);

/// <summary>Fields accepted when recording a drive time by hand.</summary>
/// <param name="Minutes">How long the drive takes, or null to forget the time held.</param>
/// <param name="Miles">How far it is, when known.</param>
/// <param name="TolledRoads">Names of the tolled roads a measured route reported.</param>
/// <param name="TollRoadOverrideIds">Toll roads to charge whatever the route reported, or null to follow it.</param>
/// <param name="ParkingPerDayOverride">What parking costs a day on this drive, or null to follow the destination.</param>
/// <param name="CommuteCreditPerDayOverride">What a day on this drive pays back, or null to follow the destination.</param>
public record PropertyDriveTimeRequest(
    double? Minutes,
    double? Miles = null,
    IReadOnlyList<string>? TolledRoads = null,
    IReadOnlyList<int>? TollRoadOverrideIds = null,
    decimal? ParkingPerDayOverride = null,
    decimal? CommuteCreditPerDayOverride = null);
