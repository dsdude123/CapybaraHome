using CapybaraHome.Application.Meals;

namespace CapybaraHome.Web;

/// <summary>Maps the meal sort orders to the words the UI shows and the values the query string carries.</summary>
public static class MealSortNames
{
    /// <summary>Every sort order, in the order the picker offers them.</summary>
    public static readonly MealSort[] All =
    [
        MealSort.LastCooked,
        MealSort.LastCookedDescending,
        MealSort.Name,
        MealSort.NameDescending,
    ];

    /// <summary>Returns the label for <paramref name="sort"/>.</summary>
    public static string Label(MealSort sort) => sort switch
    {
        MealSort.LastCooked => "Last cooked, longest ago first",
        MealSort.LastCookedDescending => "Last cooked, most recent first",
        MealSort.Name => "Name, A to Z",
        MealSort.NameDescending => "Name, Z to A",
        _ => sort.ToString(),
    };

    /// <summary>Returns the query string value for <paramref name="sort"/>.</summary>
    public static string Value(MealSort sort) => sort switch
    {
        MealSort.LastCookedDescending => "cooked-desc",
        MealSort.Name => "name",
        MealSort.NameDescending => "name-desc",
        _ => "cooked",
    };

    /// <summary>Returns the sort order <paramref name="value"/> names, or the default when it names none.</summary>
    public static MealSort Parse(string? value) =>
        All.FirstOrDefault(sort => string.Equals(Value(sort), value, StringComparison.OrdinalIgnoreCase), MealSort.LastCooked);
}
