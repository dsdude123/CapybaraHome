using System.ComponentModel;
using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;
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
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_home_search")]
    [Description("Read the home search: the current home, every candidate property with its score, and the destinations and priorities the scores came from.")]
    public static Task<HomeSearchBoard> GetHomeSearchAsync(
        HomeSearchBoardService board,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] get_home_search user={User}", http.CurrentUser());
        return board.GetAsync(cancellationToken);
    }

    /// <summary>Reads one property in full, with its score broken down.</summary>
    /// <param name="board">Home search board service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_property")]
    [Description("Get one property by its id, with its fees, utilities, amenities, pros and cons (each with the id the point tools take), priority marks, drive times and score.")]
    public static Task<ScoredProperty?> GetPropertyAsync(
        HomeSearchBoardService board,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int id,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] get_property user={User} propertyId={PropertyId}", http.CurrentUser(), id);
        return board.GetScoredAsync(id, cancellationToken);
    }

    /// <summary>Adds a candidate property.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="name">What the place is called.</param>
    /// <param name="address">Street address, which the routing service reads to measure the drives.</param>
    /// <param name="rent">Monthly rent.</param>
    /// <param name="parkingFee">Monthly parking fee.</param>
    /// <param name="petRent">Monthly pet rent.</param>
    /// <param name="deposit">Deposit, which is paid once.</param>
    /// <param name="internet">What internet the place provides.</param>
    /// <param name="listingUrl">Web address of the listing it was found on, over http or https.</param>
    /// <param name="squareFeet">How big the place is, in square feet.</param>
    /// <param name="amenities">What it offers, such as a gym or a pool.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "add_property")]
    [Description("Add a candidate property to the home search.")]
    public static async Task<PropertyView> AddPropertyAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        string name,
        string? address = null,
        decimal? rent = null,
        decimal? parkingFee = null,
        decimal? petRent = null,
        decimal? deposit = null,
        string? internet = null,
        string? listingUrl = null,
        int? squareFeet = null,
        string[]? amenities = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] add_property user={User} name={Name} rent={Rent} address={Address}",
            http.CurrentUser(), name, rent?.ToString() ?? "(none)", address ?? "(none)");
        return await properties.CreateAsync(
            new CreatePropertyRequest(
                name,
                address,
                Rent: rent,
                ParkingFee: parkingFee,
                PetRent: petRent,
                Deposit: deposit,
                Internet: internet,
                ListingUrl: listingUrl,
                SquareFeet: squareFeet,
                Amenities: amenities),
            cancellationToken);
    }

    /// <summary>Writes a note on a property.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="body">What to write, as markdown.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "add_property_note")]
    [Description("Write a note on a property in the home search.")]
    public static Task<PropertyNoteView?> AddPropertyNoteAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int id,
        string body,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] add_property_note user={User} propertyId={PropertyId} noteLength={NoteLength}",
            http.CurrentUser(), id, body.Length);
        return properties.AddNoteAsync(id, body, cancellationToken);
    }

    /// <summary>Changes a property's own fields. Fields left out keep their current value.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="name">What the place is called.</param>
    /// <param name="address">Street address; an empty string clears it. A new address drops the measured drives.</param>
    /// <param name="isCurrent">Whether this is the home lived in now, which every other property is scored against.</param>
    /// <param name="rent">Monthly rent.</param>
    /// <param name="parkingFee">Monthly parking fee.</param>
    /// <param name="petRent">Monthly pet rent.</param>
    /// <param name="deposit">Deposit, which is paid once.</param>
    /// <param name="internet">What internet the place provides; an empty string clears it.</param>
    /// <param name="listingUrl">Web address of the listing, over http or https; an empty string clears it.</param>
    /// <param name="squareFeet">How big the place is, in square feet.</param>
    /// <param name="amenities">The whole list of amenities, replacing the one held.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "update_property")]
    [Description("Change a property in the home search. Fields left out keep their current value; amenities, when given, replace the whole list. Pros and cons are changed with add_property_point, update_property_point and remove_property_point, and priorities with set_property_priority.")]
    public static Task<PropertyView?> UpdatePropertyAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int id,
        string? name = null,
        string? address = null,
        bool? isCurrent = null,
        decimal? rent = null,
        decimal? parkingFee = null,
        decimal? petRent = null,
        decimal? deposit = null,
        string? internet = null,
        string? listingUrl = null,
        int? squareFeet = null,
        string[]? amenities = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] update_property user={User} propertyId={PropertyId} name={Name} rent={Rent} address={Address}",
            http.CurrentUser(), id, name ?? "(unchanged)", rent?.ToString() ?? "(unchanged)", address ?? "(unchanged)");
        return properties.UpdateAsync(
            id,
            new UpdatePropertyRequest(
                Name: name,
                Address: address,
                IsCurrent: isCurrent,
                Rent: rent,
                ParkingFee: parkingFee,
                PetRent: petRent,
                Deposit: deposit,
                Internet: internet,
                ListingUrl: listingUrl,
                SquareFeet: squareFeet,
                Amenities: amenities),
            cancellationToken);
    }

    /// <summary>Marks a property against a priority, or clears the mark.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="priorityId">Id of the priority.</param>
    /// <param name="meets">True when it meets the priority, false when it does not, null to leave it unmarked.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "set_property_priority")]
    [Description("Mark whether a property meets a priority. Meeting it earns the priority's points, not meeting it takes off its penalty, and leaving meets out clears the mark so it counts for nothing. Returns false when the property or the priority is not there.")]
    public static Task<bool> SetPropertyPriorityAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int id,
        int priorityId,
        bool? meets = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] set_property_priority user={User} propertyId={PropertyId} priorityId={PriorityId} meets={Meets}",
            http.CurrentUser(), id, priorityId, meets?.ToString() ?? "(unmarked)");
        return properties.MarkPriorityAsync(id, priorityId, meets, cancellationToken);
    }

    /// <summary>Adds a pro or a con to a property.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the property.</param>
    /// <param name="kind">Pro or Con.</param>
    /// <param name="text">What the point is.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "add_property_point")]
    [Description("Add a pro or a con to a property. Pros and cons are for what the priorities do not already cover: a point that restates a priority counts it twice in the score.")]
    public static Task<PropertyPointView?> AddPropertyPointAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int id,
        PropertyPointKind kind,
        string text,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] add_property_point user={User} propertyId={PropertyId} kind={Kind} textLength={TextLength}",
            http.CurrentUser(), id, kind, text.Length);
        return properties.AddPointAsync(id, kind, text, cancellationToken);
    }

    /// <summary>Rewords a pro or a con, or moves it to the other list.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="pointId">Id of the pro or con, as get_property lists it.</param>
    /// <param name="text">What it now says, or left out to keep the wording.</param>
    /// <param name="kind">Pro or Con, or left out to keep it where it is.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "update_property_point")]
    [Description("Reword a property's pro or con by its id, or turn a pro into a con or back. Fields left out keep their current value.")]
    public static Task<PropertyPointView?> UpdatePropertyPointAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int pointId,
        string? text = null,
        PropertyPointKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] update_property_point user={User} pointId={PointId} kind={Kind} textLength={TextLength}",
            http.CurrentUser(), pointId, kind?.ToString() ?? "(unchanged)", text?.Length.ToString() ?? "(unchanged)");
        return properties.UpdatePointAsync(pointId, text, kind, cancellationToken);
    }

    /// <summary>Removes a pro or a con.</summary>
    /// <param name="properties">Property service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="pointId">Id of the pro or con, as get_property lists it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "remove_property_point")]
    [Description("Remove a pro or a con from a property by its id. Returns false when no pro or con has that id.")]
    public static Task<bool> RemovePropertyPointAsync(
        PropertyService properties,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int pointId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] remove_property_point user={User} pointId={PointId}", http.CurrentUser(), pointId);
        return properties.DeletePointAsync(pointId, cancellationToken);
    }

    /// <summary>Lists the priorities with how many properties meet each one.</summary>
    /// <param name="search">Home search setup service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "list_priorities")]
    [Description("List the home search priorities in order, with the points each earns, the penalty for not meeting it and how many properties meet it.")]
    public static Task<IReadOnlyList<SearchPriorityView>> ListPrioritiesAsync(
        HomeSearchService search,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] list_priorities user={User}", http.CurrentUser());
        return search.ListPrioritiesAsync(cancellationToken);
    }

    /// <summary>Adds a priority to the end of the list.</summary>
    /// <param name="search">Home search setup service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="name">What a home has to do.</param>
    /// <param name="points">Points a property earns for meeting it.</param>
    /// <param name="penalty">Points a property loses when marked as not meeting it; zero for none.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "create_priority")]
    [Description("Add a priority to the home search. It is shared by every scenario, and no property is marked against it until set_property_priority is called.")]
    public static Task<SearchPriorityView> CreatePriorityAsync(
        HomeSearchService search,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        string name,
        decimal points = 0m,
        decimal penalty = 0m,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] create_priority user={User} name={Name} points={Points} penalty={Penalty}",
            http.CurrentUser(), name, points, penalty);
        return search.CreatePriorityAsync(new SearchPriorityRequest(name, points, penalty), cancellationToken);
    }

    /// <summary>Changes a priority. Fields left out keep their current value.</summary>
    /// <param name="search">Home search setup service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the priority.</param>
    /// <param name="name">What a home has to do.</param>
    /// <param name="points">Points a property earns for meeting it.</param>
    /// <param name="penalty">Points a property loses when marked as not meeting it; zero for none.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "update_priority")]
    [Description("Rename a home search priority or change its points or penalty. Fields left out keep their current value; the marks on the properties stay.")]
    public static async Task<SearchPriorityView?> UpdatePriorityAsync(
        HomeSearchService search,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        int id,
        string? name = null,
        decimal? points = null,
        decimal? penalty = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] update_priority user={User} priorityId={PriorityId} name={Name} points={Points} penalty={Penalty}",
            http.CurrentUser(), id, name ?? "(unchanged)", points?.ToString() ?? "(unchanged)", penalty?.ToString() ?? "(unchanged)");

        var held = (await search.ListPrioritiesAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);
        if (held is null)
        {
            return null;
        }

        return await search.UpdatePriorityAsync(
            id,
            new SearchPriorityRequest(name ?? held.Name, points ?? held.Points, penalty ?? held.Penalty),
            cancellationToken);
    }

    /// <summary>Changes the active scenario's scoring weights. Weights left out keep their current value.</summary>
    /// <param name="search">Home search setup service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="monthlyFeePointsPerDollar">Points per dollar a month cheaper than the current home.</param>
    /// <param name="depositPointsPerDollar">Points per dollar of deposit below the current home's.</param>
    /// <param name="driveTimePointsPerMinute">Points per minute of drive saved, before the destination's weight.</param>
    /// <param name="amenityPoints">Points for each amenity.</param>
    /// <param name="proPoints">Points for each pro.</param>
    /// <param name="conPoints">Points taken off for each con.</param>
    /// <param name="sizePointsPerSquareFoot">Points per square foot larger than the current home.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "update_search_settings")]
    [Description("Change the scoring weights of the home search's active scenario. Weights left out keep their current value. The routing service is set on the settings page only.")]
    public static Task<HomeSearchSettingsView> UpdateSearchSettingsAsync(
        HomeSearchService search,
        ILogger<HomeSearchTools> logger,
        IHttpContextAccessor http,
        decimal? monthlyFeePointsPerDollar = null,
        decimal? depositPointsPerDollar = null,
        decimal? driveTimePointsPerMinute = null,
        decimal? amenityPoints = null,
        decimal? proPoints = null,
        decimal? conPoints = null,
        decimal? sizePointsPerSquareFoot = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] update_search_settings user={User}", http.CurrentUser());
        return search.SaveSettingsAsync(
            new HomeSearchSettingsRequest(
                MonthlyFeePointsPerDollar: monthlyFeePointsPerDollar,
                DepositPointsPerDollar: depositPointsPerDollar,
                DriveTimePointsPerMinute: driveTimePointsPerMinute,
                AmenityPoints: amenityPoints,
                ProPoints: proPoints,
                ConPoints: conPoints,
                SizePointsPerSquareFoot: sizePointsPerSquareFoot),
            cancellationToken);
    }
}
