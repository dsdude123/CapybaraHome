namespace CapybaraHome.Domain.Tasks;

/// <summary>
/// The unit an automation's repeat interval is counted in. Persisted by name so the database
/// stays readable.
/// </summary>
public enum TaskIntervalUnit
{
    Days,
    Weeks,
    Months,
    Years,
}
