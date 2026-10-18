namespace CapybaraHome.Domain.Tasks;

/// <summary>A grouping of tasks, equivalent to an epic in the JIRA data this replaces.</summary>
public class Category
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>Hex colour used to tint the category in the UI, for example "#A8D5BA".</summary>
    public string? ColorHex { get; set; }

    /// <summary>Position in hand-ordered lists; lower sorts first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Tasks in this category.</summary>
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
