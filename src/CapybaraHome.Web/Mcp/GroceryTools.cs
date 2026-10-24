using System.ComponentModel;
using CapybaraHome.Application.Grocery;
using ModelContextProtocol.Server;

namespace CapybaraHome.Web.Mcp;

/// <summary>
/// The grocery tools the Claude connector calls, so an item thought of away from the house can be
/// added to the same list the web UI shows.
/// </summary>
[McpServerToolType]
public class GroceryTools
{
    /// <summary>Reads the open grocery list, or null when none has been started.</summary>
    /// <param name="grocery">Grocery service.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_grocery_list")]
    [Description("Get the open grocery list and everything on it.")]
    public static Task<GroceryListView?> GetGroceryListAsync(
        GroceryService grocery,
        CancellationToken cancellationToken = default)
    {
        return grocery.GetCurrentAsync(cancellationToken);
    }

    /// <summary>Puts one item on the open list, starting a list when there is none.</summary>
    /// <param name="grocery">Grocery service.</param>
    /// <param name="item">What to buy.</param>
    /// <param name="quantity">How much to buy, as written, for example "2 cups".</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "add_grocery_item")]
    [Description("Add one item to the open grocery list, starting a list if there is none.")]
    public static Task<GroceryItemView> AddGroceryItemAsync(
        GroceryService grocery,
        string item,
        string? quantity = null,
        CancellationToken cancellationToken = default)
    {
        return grocery.AddItemAsync(new GroceryItemInput(item, quantity), cancellationToken);
    }

    /// <summary>Takes an item off the list.</summary>
    /// <param name="grocery">Grocery service.</param>
    /// <param name="id">Id of the item.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "remove_grocery_item")]
    [Description("Take one item off the grocery list by its id.")]
    public static Task<bool> RemoveGroceryItemAsync(
        GroceryService grocery,
        int id,
        CancellationToken cancellationToken = default)
    {
        return grocery.RemoveItemAsync(id, cancellationToken);
    }

    /// <summary>Closes the open list and starts an empty one.</summary>
    /// <param name="grocery">Grocery service.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "start_fresh_grocery_list")]
    [Description("Close the open grocery list and start an empty one. The old list is kept.")]
    public static Task<GroceryListView> StartFreshGroceryListAsync(
        GroceryService grocery,
        CancellationToken cancellationToken = default)
    {
        return grocery.StartFreshAsync(cancellationToken);
    }

    /// <summary>Reads what a generated list would ask about, without adding anything.</summary>
    /// <param name="grocery">Grocery service.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "list_grocery_candidates")]
    [Description("List what generating a grocery list would ask about: the frequent items and each planned meal's ingredients.")]
    public static Task<GroceryCandidates> ListGroceryCandidatesAsync(
        GroceryService grocery,
        CancellationToken cancellationToken = default)
    {
        return grocery.CandidatesAsync(cancellationToken);
    }
}
