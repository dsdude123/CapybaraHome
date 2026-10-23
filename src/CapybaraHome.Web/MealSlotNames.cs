using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Web;

/// <summary>Turns the meal slots into the words the UI shows, and holds the planning defaults.</summary>
public static class MealSlotNames
{
    /// <summary>Every slot, in the order of the day.</summary>
    public static readonly MealSlot[] All =
    [
        MealSlot.Breakfast,
        MealSlot.Lunch,
        MealSlot.Dinner,
    ];

    /// <summary>Returns the label for <paramref name="slot"/>.</summary>
    public static string Label(MealSlot slot) => slot switch
    {
        MealSlot.Breakfast => "Breakfast",
        MealSlot.Lunch => "Lunch",
        MealSlot.Dinner => "Dinner",
        _ => slot.ToString(),
    };

    /// <summary>
    /// How many of <paramref name="slot"/> a planning session aims for before it is told otherwise:
    /// breakfasts from Friday to Monday, lunches on the two days worked from home, and a dinner a day.
    /// Every one of them is changed in the planning page itself.
    /// </summary>
    public static int DefaultTarget(MealSlot slot) => slot switch
    {
        MealSlot.Breakfast => 4,
        MealSlot.Lunch => 2,
        MealSlot.Dinner => 7,
        _ => 0,
    };
}
