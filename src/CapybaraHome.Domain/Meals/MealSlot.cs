namespace CapybaraHome.Domain.Meals;

/// <summary>Which meal of the day a planned meal fills. Persisted by name so the database stays readable.</summary>
public enum MealSlot
{
    Breakfast,
    Lunch,
    Dinner,
}
