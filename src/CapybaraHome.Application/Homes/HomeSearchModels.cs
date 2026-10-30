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
/// <param name="Kilometres">How far it is, when the routing service said.</param>
/// <param name="Source">Whether the time was measured or typed in.</param>
/// <param name="MeasuredAt">When it was measured or last typed.</param>
public record PropertyDriveTimeView(
    int DestinationId,
    string DestinationName,
    double Minutes,
    double? Kilometres,
    DriveTimeSource Source,
    DateTimeOffset MeasuredAt);

/// <summary>How a property was marked against one priority.</summary>
/// <param name="PriorityId">The priority.</param>
/// <param name="Name">What the priority is.</param>
/// <param name="Points">Points meeting it is worth.</param>
/// <param name="Meets">Whether the property meets it, or null when it has not been marked.</param>
public record PropertyPriorityView(int PriorityId, string Name, decimal Points, bool? Meets);

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
/// <param name="ClearMoney">When true, the money fields left null in this request are cleared.</param>
/// <param name="Internet">New internet description; pass an empty string to clear it.</param>
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
    IReadOnlyList<PropertyUtilityInput>? Utilities = null,
    IReadOnlyList<string>? Amenities = null,
    IReadOnlyList<PropertyPointInput>? Points = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>Fields accepted when creating or updating a destination.</summary>
/// <param name="Name">What the place is called; required.</param>
/// <param name="Address">Street address.</param>
/// <param name="Weight">How much its drive counts next to the others.</param>
public record SearchDestinationRequest(string Name, string? Address = null, decimal Weight = 1m);

/// <summary>A destination as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the place is called.</param>
/// <param name="Address">Street address.</param>
/// <param name="Weight">How much its drive counts next to the others.</param>
/// <param name="Latitude">Latitude the address was resolved to, or null.</param>
/// <param name="Longitude">Longitude the address was resolved to, or null.</param>
/// <param name="SortOrder">Position in the list.</param>
public record SearchDestinationView(
    int Id,
    string Name,
    string? Address,
    decimal Weight,
    double? Latitude,
    double? Longitude,
    int SortOrder);

/// <summary>Fields accepted when creating or updating a priority.</summary>
/// <param name="Name">What the priority is; required.</param>
/// <param name="Points">Points a property earns for meeting it.</param>
public record SearchPriorityRequest(string Name, decimal Points = 0m);

/// <summary>A priority as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the priority is.</param>
/// <param name="Points">Points a property earns for meeting it.</param>
/// <param name="SortOrder">Position in the list.</param>
/// <param name="MetCount">How many properties are marked as meeting it.</param>
public record SearchPriorityView(int Id, string Name, decimal Points, int SortOrder, int MetCount);

/// <summary>The scoring weights and the routing service, as the API and the UI show them.</summary>
/// <param name="MonthlyFeePointsPerDollar">Points per dollar a month cheaper than the current home.</param>
/// <param name="DepositPointsPerDollar">Points per dollar of deposit below the current home's.</param>
/// <param name="DriveTimePointsPerMinute">Points per minute of drive saved, before the destination's weight.</param>
/// <param name="AmenityPoints">Points for each amenity.</param>
/// <param name="ProPoints">Points for each pro.</param>
/// <param name="ConPoints">Points taken off for each con.</param>
/// <param name="RoutingBaseUrl">Base address of the OpenRouteService instance.</param>
/// <param name="HasRoutingApiKey">Whether a key is held for it; the key itself is never read back.</param>
public record HomeSearchSettingsView(
    decimal MonthlyFeePointsPerDollar,
    decimal DepositPointsPerDollar,
    decimal DriveTimePointsPerMinute,
    decimal AmenityPoints,
    decimal ProPoints,
    decimal ConPoints,
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
        || ConPoints != 0m;

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
/// <param name="RoutingBaseUrl">Base address of the OpenRouteService instance; pass an empty string to clear it.</param>
/// <param name="RoutingApiKey">Key for it; an empty string keeps the key already held.</param>
public record HomeSearchSettingsRequest(
    decimal? MonthlyFeePointsPerDollar = null,
    decimal? DepositPointsPerDollar = null,
    decimal? DriveTimePointsPerMinute = null,
    decimal? AmenityPoints = null,
    decimal? ProPoints = null,
    decimal? ConPoints = null,
    string? RoutingBaseUrl = null,
    string? RoutingApiKey = null);

/// <summary>Fields accepted when writing a note on a property.</summary>
/// <param name="Body">What to write, held as markdown.</param>
public record PropertyNoteRequest(string Body);

/// <summary>Fields accepted when marking a property against a priority.</summary>
/// <param name="Meets">Whether it meets the priority, or null to go back to unmarked.</param>
public record PropertyPriorityRequest(bool? Meets);

/// <summary>Fields accepted when recording a drive time by hand.</summary>
/// <param name="Minutes">How long the drive takes, or null to forget the time held.</param>
/// <param name="Kilometres">How far it is, when known.</param>
public record PropertyDriveTimeRequest(double? Minutes, double? Kilometres = null);
