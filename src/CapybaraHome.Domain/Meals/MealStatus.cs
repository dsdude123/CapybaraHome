namespace CapybaraHome.Domain.Meals;

/// <summary>Where a meal stands. Persisted by name so the database stays readable.</summary>
public enum MealStatus
{
    Pooled,
    Available,
    Queued,
    Planned,
    PlannedShared,
    Closed,
}
