using System.ComponentModel;
using CapybaraHome.Application.Homes;
using ModelContextProtocol.Server;

namespace CapybaraHome.Web.Mcp;

/// <summary>
/// The home search tools the Claude connector calls. They are a thin layer over the same services
/// the web UI uses, so the two can never disagree about what a property scores.
/// </summary>
[McpServerToolType]
public class HomeSearchTools
{
    /// <summary>Reads the whole search: the current home, the candidates in score order, and the setup.</summary>
    /// <param name="board">Home search board service.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_home_search")]
    [Description("Read the home search: the current home, every candidate property with its score, and the destinations and priorities the scores came from.")]
    public static Task<HomeSearchBoard> GetHomeSearchAsync(
        HomeSearchBoardService board,
        CancellationToken cancellationToken = default)
    {
        return board.GetAsync(cancellationToken);
    }

    /// <summary>Reads one property in full, with its score broken down.</summary>
    /// <param name="board">Home search board service.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_property")]
    [Description("Get one property by its id, with its fees, utilities, amenities, pros and cons, drive times and score.")]
    public static Task<ScoredProperty?> GetPropertyAsync(
        HomeSearchBoardService board,
        int id,
        CancellationToken cancellationToken = default)
    {
        return board.GetScoredAsync(id, cancellationToken);
    }

    /// <summary>Adds a candidate property.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="name">What the place is called.</param>
    /// <param name="address">Street address, which the routing service reads to measure the drives.</param>
    /// <param name="rent">Monthly rent.</param>
    /// <param name="parkingFee">Monthly parking fee.</param>
    /// <param name="petRent">Monthly pet rent.</param>
    /// <param name="deposit">Deposit, which is paid once.</param>
    /// <param name="internet">What internet the place provides.</param>
    /// <param name="amenities">What it offers, such as a gym or a pool.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "add_property")]
    [Description("Add a candidate property to the home search.")]
    public static async Task<PropertyView> AddPropertyAsync(
        PropertyService properties,
        string name,
        string? address = null,
        decimal? rent = null,
        decimal? parkingFee = null,
        decimal? petRent = null,
        decimal? deposit = null,
        string? internet = null,
        string[]? amenities = null,
        CancellationToken cancellationToken = default)
    {
        return await properties.CreateAsync(
            new CreatePropertyRequest(
                name,
                address,
                Rent: rent,
                ParkingFee: parkingFee,
                PetRent: petRent,
                Deposit: deposit,
                Internet: internet,
                Amenities: amenities),
            cancellationToken);
    }

    /// <summary>Writes a note on a property.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="body">What to write, as markdown.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "add_property_note")]
    [Description("Write a note on a property in the home search.")]
    public static Task<PropertyNoteView?> AddPropertyNoteAsync(
        PropertyService properties,
        int id,
        string body,
        CancellationToken cancellationToken = default)
    {
        return properties.AddNoteAsync(id, body, cancellationToken);
    }
}
