namespace CapybaraHome.Application.Categories;

/// <summary>A category as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">Display name.</param>
/// <param name="ColorHex">Hex colour used to tint the category, or null.</param>
/// <param name="SortOrder">Position in hand-ordered lists.</param>
/// <param name="TaskCount">Tasks filed under the category.</param>
/// <param name="DoneCount">How many of those are done.</param>
public record CategoryView(int Id, string Name, string? ColorHex, int SortOrder, int TaskCount, int DoneCount)
{
    /// <summary>Share of the category's tasks that are done, from 0 to 1. Zero when it holds no tasks.</summary>
    public double Progress => TaskCount == 0 ? 0 : (double)DoneCount / TaskCount;
}

/// <summary>Fields accepted when creating or updating a category.</summary>
/// <param name="Name">Display name; required.</param>
/// <param name="ColorHex">Hex colour, for example "#A8D5BA".</param>
/// <param name="SortOrder">Position in hand-ordered lists.</param>
public record CategoryRequest(string Name, string? ColorHex = null, int SortOrder = 0);
