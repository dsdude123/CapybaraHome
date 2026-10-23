using CapybaraHome.Application.Tasks;

namespace CapybaraHome.Web;

/// <summary>Maps the task sort orders to the words the UI shows and the values the query string carries.</summary>
public static class TaskSortNames
{
    /// <summary>Every sort order, in the order the picker offers them.</summary>
    public static readonly TaskSort[] All =
    [
        TaskSort.DueDate,
        TaskSort.DueDateDescending,
        TaskSort.Title,
        TaskSort.TitleDescending,
        TaskSort.Category,
        TaskSort.RecentlyUpdated,
    ];

    /// <summary>Returns the label for <paramref name="sort"/>.</summary>
    public static string Label(TaskSort sort) => sort switch
    {
        TaskSort.DueDate => "Due date, earliest first",
        TaskSort.DueDateDescending => "Due date, latest first",
        TaskSort.Title => "Title, A to Z",
        TaskSort.TitleDescending => "Title, Z to A",
        TaskSort.Category => "Category",
        TaskSort.RecentlyUpdated => "Recently changed",
        _ => sort.ToString(),
    };

    /// <summary>Returns the query string value for <paramref name="sort"/>.</summary>
    public static string Value(TaskSort sort) => sort switch
    {
        TaskSort.DueDateDescending => "due-desc",
        TaskSort.Title => "title",
        TaskSort.TitleDescending => "title-desc",
        TaskSort.Category => "category",
        TaskSort.RecentlyUpdated => "updated",
        _ => "due",
    };

    /// <summary>Returns the sort order <paramref name="value"/> names, or the default when it names none.</summary>
    public static TaskSort Parse(string? value) =>
        All.FirstOrDefault(sort => string.Equals(Value(sort), value, StringComparison.OrdinalIgnoreCase), TaskSort.DueDate);
}
