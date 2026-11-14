using System.ComponentModel;
using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;
using ModelContextProtocol.Server;

namespace CapybaraHome.Web.Mcp;

/// <summary>
/// The tools the Claude connector calls. They are a thin layer over the same services the
/// web UI uses, so the two can never disagree about what a task is.
/// </summary>
[McpServerToolType]
public class TaskTools
{
    /// <summary>Finds tasks by text, status, category or how soon they are due.</summary>
    /// <param name="tasks">Task service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="text">Words to look for in the title or the notes.</param>
    /// <param name="status">Keep only tasks in this state: ToDo, InProgress, OnHold or Done.</param>
    /// <param name="categoryId">Keep only tasks in this category.</param>
    /// <param name="dueWithinDays">Keep only tasks due no more than this many days from today.</param>
    /// <param name="overdue">Keep only tasks that are past their due date and not done.</param>
    /// <param name="limit">Most tasks to return; defaults to 25.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "search_tasks")]
    [Description("Search the home task list by text, status, category or due date.")]
    public static async Task<IReadOnlyList<TaskView>> SearchTasksAsync(
        TaskService tasks,
        ILogger<TaskTools> logger,
        IHttpContextAccessor http,
        string? text = null,
        TaskItemStatus? status = null,
        int? categoryId = null,
        int? dueWithinDays = null,
        bool overdue = false,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] search_tasks user={User} text={Text} status={Status} limit={Limit}",
            http.CurrentUser(), text ?? "(none)", status?.ToString() ?? "(none)", limit);

        var filter = new TaskFilter(
            Text: text,
            Statuses: status is { } one ? [one] : null,
            CategoryId: categoryId,
            DueWithinDays: dueWithinDays,
            Overdue: overdue,
            Limit: Math.Clamp(limit, 1, 200));

        return await tasks.ListAsync(filter, cancellationToken: cancellationToken);
    }

    /// <summary>Reads one task in full.</summary>
    /// <param name="tasks">Task service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the task.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "get_task")]
    [Description("Get one home task by its id.")]
    public static Task<TaskView?> GetTaskAsync(
        TaskService tasks,
        ILogger<TaskTools> logger,
        IHttpContextAccessor http,
        int id,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] get_task user={User} taskId={TaskId}", http.CurrentUser(), id);
        return tasks.GetAsync(id, cancellationToken);
    }

    /// <summary>Adds a task.</summary>
    /// <param name="tasks">Task service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="title">What needs doing.</param>
    /// <param name="notes">Any detail, as markdown.</param>
    /// <param name="status">State to start in; defaults to ToDo.</param>
    /// <param name="categoryId">Category to file it under.</param>
    /// <param name="dueDate">Day it is due, as yyyy-MM-dd.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "create_task")]
    [Description("Add a home task.")]
    public static Task<TaskView> CreateTaskAsync(
        TaskService tasks,
        ILogger<TaskTools> logger,
        IHttpContextAccessor http,
        string title,
        string? notes = null,
        TaskItemStatus status = TaskItemStatus.ToDo,
        int? categoryId = null,
        DateOnly? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] create_task user={User} title={Title} status={Status} categoryId={CategoryId}",
            http.CurrentUser(), title, status, categoryId?.ToString() ?? "(none)");
        return tasks.CreateAsync(new CreateTaskRequest(title, notes, status, categoryId, dueDate), cancellationToken);
    }

    /// <summary>Changes a task. Anything left out keeps its current value.</summary>
    /// <param name="tasks">Task service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="id">Id of the task to change.</param>
    /// <param name="title">New title.</param>
    /// <param name="notes">New notes.</param>
    /// <param name="status">New state.</param>
    /// <param name="categoryId">New category.</param>
    /// <param name="clearCategory">Remove the category.</param>
    /// <param name="dueDate">New due date, as yyyy-MM-dd.</param>
    /// <param name="clearDueDate">Remove the due date.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "update_task")]
    [Description("Change a home task. Fields left out keep their current value.")]
    public static Task<TaskView?> UpdateTaskAsync(
        TaskService tasks,
        ILogger<TaskTools> logger,
        IHttpContextAccessor http,
        int id,
        string? title = null,
        string? notes = null,
        TaskItemStatus? status = null,
        int? categoryId = null,
        bool clearCategory = false,
        DateOnly? dueDate = null,
        bool clearDueDate = false,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] update_task user={User} taskId={TaskId} title={Title} status={Status} clearCategory={ClearCategory}",
            http.CurrentUser(), id, title ?? "(unchanged)", status?.ToString() ?? "(unchanged)", clearCategory);
        var request = new UpdateTaskRequest(title, notes, status, categoryId, clearCategory, dueDate, clearDueDate);
        return tasks.UpdateAsync(id, request, cancellationToken);
    }

    /// <summary>Lists the categories, with how far each has got.</summary>
    /// <param name="categories">Category service.</param>
    /// <param name="logger">Writes the audit line for the call.</param>
    /// <param name="http">Names the caller the audit line is written against.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    [McpServerTool(Name = "list_categories")]
    [Description("List the task categories and their progress.")]
    public static async Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(
        CategoryService categories,
        ILogger<TaskTools> logger,
        IHttpContextAccessor http,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[MCP] list_categories user={User}", http.CurrentUser());
        return await categories.ListAsync(cancellationToken);
    }
}
