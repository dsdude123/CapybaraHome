namespace CapybaraHome.Application.Homes;

/// <summary>What one destination's drive contributed to a property's score.</summary>
/// <param name="DestinationId">The destination driven to.</param>
/// <param name="Name">What that place is called.</param>
/// <param name="Minutes">The property's drive, or null when it has not been measured or typed.</param>
/// <param name="CurrentMinutes">The current home's drive, or null when that is unknown.</param>
/// <param name="Points">Points the comparison is worth.</param>
public record DriveTimeScore(
    int DestinationId,
    string Name,
    double? Minutes,
    double? CurrentMinutes,
    decimal Points);

/// <summary>
/// How good a match a property is, broken down so each number can be traced back to the weight
/// and the difference that produced it.
/// </summary>
/// <param name="PropertyId">The property scored.</param>
/// <param name="MonthlyFees">Points for the difference in what a month costs.</param>
/// <param name="Deposit">Points for the difference in deposit.</param>
/// <param name="DriveTimes">Points for the differences in drive time, added up.</param>
/// <param name="Amenities">Points for the amenities.</param>
/// <param name="Pros">Points for the pros.</param>
/// <param name="Cons">Points taken off for the cons.</param>
/// <param name="Priorities">Points for the priorities the property meets.</param>
/// <param name="DriveTimeLines">The drive comparisons behind <paramref name="DriveTimes"/>.</param>
/// <param name="MonthlyDifference">How much cheaper a month is than the current home's, as money.</param>
/// <param name="DepositDifference">How much lower the deposit is than the current home's, as money.</param>
public record PropertyScore(
    int PropertyId,
    decimal MonthlyFees,
    decimal Deposit,
    decimal DriveTimes,
    decimal Amenities,
    decimal Pros,
    decimal Cons,
    decimal Priorities,
    IReadOnlyList<DriveTimeScore> DriveTimeLines,
    decimal? MonthlyDifference,
    decimal? DepositDifference)
{
    /// <summary>Every component added up.</summary>
    public decimal Total => MonthlyFees + Deposit + DriveTimes + Amenities + Pros + Cons + Priorities;
}
