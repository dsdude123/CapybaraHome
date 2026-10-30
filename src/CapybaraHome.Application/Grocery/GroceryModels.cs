namespace CapybaraHome.Application.Grocery;

/// <summary>One thing to buy as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Quantity">How much to buy, as written, or null.</param>
/// <param name="Item">What to buy.</param>
/// <param name="Source">Where it came from, such as the meal it is an ingredient of, or null.</param>
/// <param name="SortOrder">Position in the list.</param>
public record GroceryItemView(int Id, string? Quantity, string Item, string? Source, int SortOrder);

/// <summary>A shopping list as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="StartedAt">When the list was started.</param>
/// <param name="ClosedAt">When a fresh list replaced it, or null while it is the open list.</param>
/// <param name="Items">What is on it, in order.</param>
public record GroceryListView(int Id, DateTimeOffset StartedAt, DateTimeOffset? ClosedAt, IReadOnlyList<GroceryItemView> Items);

/// <summary>A frequent item as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Quantity">How much is usually bought, or null.</param>
/// <param name="Item">What the item is.</param>
/// <param name="SortOrder">Position in the list offered.</param>
public record FrequentGroceryItemView(int Id, string? Quantity, string Item, int SortOrder);

/// <summary>One thing to buy as it is written.</summary>
/// <param name="Item">What to buy; required.</param>
/// <param name="Quantity">How much to buy, as written, or null.</param>
/// <param name="Source">Where it came from, or null when it was typed in.</param>
public record GroceryItemInput(string Item, string? Quantity = null, string? Source = null);

/// <summary>Something the generator offers to put on the list.</summary>
/// <param name="Item">What to buy.</param>
/// <param name="Quantity">How much to buy, as written, or null.</param>
/// <param name="Source">Where it came from: the meal's name, or null for a frequent item.</param>
/// <param name="AlreadyOnList">Whether an item of that name is on the open list already.</param>
public record GroceryCandidate(string Item, string? Quantity, string? Source, bool AlreadyOnList);

/// <summary>What the generator offers, with the frequent items and each planned meal kept apart.</summary>
/// <param name="Name">What to call the group: "Frequent items", or the meal and its planned variant.</param>
/// <param name="Candidates">What it offers, in order.</param>
public record GroceryCandidateGroup(string Name, IReadOnlyList<GroceryCandidate> Candidates);

/// <summary>Everything the generator found to ask about.</summary>
/// <param name="Groups">The frequent items first, then one group per planned meal.</param>
public record GroceryCandidates(IReadOnlyList<GroceryCandidateGroup> Groups)
{
    /// <summary>How many of the candidates are not on the list yet.</summary>
    public int Offered => Groups.Sum(group => group.Candidates.Count(candidate => !candidate.AlreadyOnList));
}
