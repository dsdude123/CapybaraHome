namespace CapybaraHome.Domain.Tasks;

/// <summary>How often an automation generates its task. Persisted by name so the database stays readable.</summary>
public enum TaskAutomationFrequency
{
    Daily,
    Weekly,
    BiWeekly,
    Monthly,
    Quarterly,
    Yearly,
}
