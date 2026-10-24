using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Web;

/// <summary>Turns the meal statuses into the words the UI shows.</summary>
public static class MealStatusNames
{
    /// <summary>Every status, in the order the UI offers them.</summary>
    public static readonly MealStatus[] All =
    [
        MealStatus.Pooled,
        MealStatus.Available,
        MealStatus.Queued,
        MealStatus.Planned,
        MealStatus.PlannedShared,
        MealStatus.Closed,
    ];

    /// <summary>Returns the label for <paramref name="status"/>.</summary>
    public static string Label(MealStatus status) => status switch
    {
        MealStatus.Pooled => "Pooled",
        MealStatus.Available => "Available",
        MealStatus.Queued => "Queued",
        MealStatus.Planned => "Planned",
        MealStatus.PlannedShared => "Planned shared",
        MealStatus.Closed => "Closed",
        _ => status.ToString(),
    };
}
