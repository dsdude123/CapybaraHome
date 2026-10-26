using System.Text.Json;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Reads and writes the operations a task runs when it is completed, and runs them. Creating a
/// task and producing a document happen while the completion is being saved, so they are there
/// the moment the page comes back; a webhook is left for <see cref="SendPendingWebhooksAsync"/>
/// so a target that is slow or down cannot hold up the click that closed the task.
/// </summary>
public class TaskCloseOperationService(ICapybaraHomeDbContext context, TaskWebhookSender webhooks, TimeProvider clock)
{
    /// <summary>How many times a webhook is tried before the run is given up on.</summary>
    public const int MaxWebhookAttempts = 5;

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Returns the operations on a task, in the order they run.</summary>
    /// <param name="taskId">Task to read the operations of.</param>
    /// <param name="cancellationToken">Token that cancels the read.</param>
    public async Task<IReadOnlyList<TaskCloseOperationView>> ListAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var rows = await context.TaskCloseOperations.AsNoTracking()
            .Where(o => o.TaskId == taskId)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(o => o.Id).ToList();
        var runs = await context.TaskCloseOperationRuns.AsNoTracking()
            .Where(r => r.OperationId != null && ids.Contains(r.OperationId!.Value))
            .ToListAsync(cancellationToken);

        var lastByOperation = runs
            .GroupBy(r => r.OperationId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());

        return rows
            .Select(o => View(o, lastByOperation.TryGetValue(o.Id, out var run) ? RunView(run) : null))
            .ToList();
    }

    /// <summary>Returns one operation, or null when none has that id.</summary>
    /// <param name="id">Operation to read.</param>
    /// <param name="cancellationToken">Token that cancels the read.</param>
    public async Task<TaskCloseOperationView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await context.TaskCloseOperations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var run = await context.TaskCloseOperationRuns.AsNoTracking()
            .Where(r => r.OperationId == id)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return View(row, run is null ? null : RunView(run));
    }

    /// <summary>Adds an operation to a task and returns it as saved.</summary>
    /// <param name="taskId">Task the operation belongs to.</param>
    /// <param name="request">Fields to save.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    /// <exception cref="ArgumentException">The task does not exist, or the fields the kind needs are missing.</exception>
    public async Task<TaskCloseOperationView> CreateAsync(
        int taskId,
        CreateTaskCloseOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        var exists = await context.Tasks.AnyAsync(t => t.Id == taskId, cancellationToken);
        if (!exists)
        {
            throw new ArgumentException($"No task has id {taskId}.", nameof(taskId));
        }

        var operation = new TaskCloseOperation
        {
            TaskId = taskId,
            Kind = request.Kind,
            IsEnabled = request.IsEnabled,
            WebhookUrl = Blank(request.WebhookUrl),
            WebhookMethod = request.WebhookMethod,
            FollowUpTitle = Blank(request.FollowUpTitle),
            DueDateMode = request.DueDateMode,
            DueDateValue = request.DueDateValue,
            DueDateOn = request.DueDateOn,
            CarryOperationsForward = request.CarryOperationsForward,
            DocumentTitle = Blank(request.DocumentTitle),
            DocumentBody = Blank(request.DocumentBody),
            SortOrder = await NextSortOrderAsync(taskId, cancellationToken),
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        };

        Validate(operation);

        context.TaskCloseOperations.Add(operation);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(operation.Id, cancellationToken)
            ?? throw new InvalidOperationException("The new operation could not be read back.");
    }

    /// <summary>Applies the set members of <paramref name="request"/>, or returns null when no operation has that id.</summary>
    /// <param name="id">Operation to change.</param>
    /// <param name="request">Fields to apply.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    /// <exception cref="ArgumentException">The fields the kind needs are missing.</exception>
    public async Task<TaskCloseOperationView?> UpdateAsync(
        int id,
        UpdateTaskCloseOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        var operation = await context.TaskCloseOperations.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (operation is null)
        {
            return null;
        }

        if (request.Kind is { } kind)
        {
            operation.Kind = kind;
        }

        if (request.WebhookUrl is not null)
        {
            operation.WebhookUrl = Blank(request.WebhookUrl);
        }

        if (request.WebhookMethod is { } method)
        {
            operation.WebhookMethod = method;
        }

        if (request.FollowUpTitle is not null)
        {
            operation.FollowUpTitle = Blank(request.FollowUpTitle);
        }

        if (request.DueDateMode is { } mode)
        {
            operation.DueDateMode = mode;
        }

        if (request.DueDateValue is { } value)
        {
            operation.DueDateValue = value;
        }

        if (request.DueDateOn is { } on)
        {
            operation.DueDateOn = on;
        }

        if (request.CarryOperationsForward is { } carry)
        {
            operation.CarryOperationsForward = carry;
        }

        if (request.DocumentTitle is not null)
        {
            operation.DocumentTitle = Blank(request.DocumentTitle);
        }

        if (request.DocumentBody is not null)
        {
            operation.DocumentBody = Blank(request.DocumentBody);
        }

        if (request.IsEnabled is { } enabled)
        {
            operation.IsEnabled = enabled;
        }

        Validate(operation);

        operation.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes an operation. Returns false when no operation has that id.</summary>
    /// <param name="id">Operation to delete.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var operation = await context.TaskCloseOperations.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (operation is null)
        {
            return false;
        }

        context.TaskCloseOperations.Remove(operation);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Returns the most recent runs, newest first.</summary>
    /// <param name="limit">How many runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the read.</param>
    public async Task<IReadOnlyList<TaskCloseRunView>> ListRunsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var rows = await context.TaskCloseOperationRuns.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(RunView).ToList();
    }

    /// <summary>Returns the runs that produced a document, newest first.</summary>
    /// <param name="limit">How many documents to return.</param>
    /// <param name="cancellationToken">Token that cancels the read.</param>
    public async Task<IReadOnlyList<TaskCloseRunView>> ListDocumentsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var rows = await context.TaskCloseOperationRuns.AsNoTracking()
            .Where(r => r.Kind == TaskCloseOperationKind.PrintDocument)
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(RunView).ToList();
    }

    /// <summary>Returns one run, or null when none has that id.</summary>
    /// <param name="id">Run to read.</param>
    /// <param name="cancellationToken">Token that cancels the read.</param>
    public async Task<TaskCloseRunView?> GetRunAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await context.TaskCloseOperationRuns.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        return row is null ? null : RunView(row);
    }

    /// <summary>
    /// Runs the operations on a task that has just been moved to done. Called with the completion
    /// already saved, so a failure here cannot leave the task looking unfinished.
    /// </summary>
    /// <param name="task">The task that was completed.</param>
    /// <param name="cancellationToken">Token that cancels the work.</param>
    public async Task<TaskCloseRunSummary> RunForCompletionAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        var operations = await context.TaskCloseOperations
            .Where(o => o.TaskId == task.Id && o.IsEnabled)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);

        if (operations.Count == 0)
        {
            return new TaskCloseRunSummary(0, 0, 0, 0);
        }

        var now = task.CompletedAt ?? clock.GetUtcNow();
        var completedOn = DateOnly.FromDateTime(now.UtcDateTime);
        var categoryName = task.CategoryId is { } categoryId
            ? await context.Categories.Where(c => c.Id == categoryId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken)
            : null;

        var created = 0;
        var documents = 0;
        var pending = 0;

        foreach (var operation in operations)
        {
            var run = new TaskCloseOperationRun
            {
                OperationId = operation.Id,
                TaskId = task.Id,
                TaskTitle = task.Title,
                Kind = operation.Kind,
                CompletedAt = now,
                CreatedAt = now,
            };

            switch (operation.Kind)
            {
                case TaskCloseOperationKind.CreateTask:
                    var followUp = await CreateFollowUpAsync(task, operation, completedOn, cancellationToken);
                    run.CreatedTaskId = followUp.Id;
                    run.Status = TaskCloseRunStatus.Succeeded;
                    run.Attempts = 1;
                    run.LastAttemptAt = now;
                    run.Detail = followUp.DueDate is { } due
                        ? $"Created “{followUp.Title}”, due {due:MMMM d, yyyy}."
                        : $"Created “{followUp.Title}” with no due date.";
                    created++;
                    break;

                case TaskCloseOperationKind.PrintDocument:
                    run.DocumentTitle = operation.DocumentTitle ?? task.Title;
                    run.DocumentBody = operation.DocumentBody ?? task.Notes;
                    run.Status = TaskCloseRunStatus.Succeeded;
                    run.Attempts = 1;
                    run.LastAttemptAt = now;
                    run.Detail = "The document is ready to print.";
                    documents++;
                    break;

                default:
                    run.WebhookPayload = JsonSerializer.Serialize(
                        new WebhookPayload("task.completed", task.Id, task.Title, task.Notes, categoryName, task.DueDate, now),
                        PayloadOptions);
                    run.Status = TaskCloseRunStatus.Pending;
                    pending++;
                    break;
            }

            context.TaskCloseOperationRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new TaskCloseRunSummary(operations.Count, created, documents, pending);
    }

    /// <summary>
    /// Sends the webhook calls that are waiting, and gives up on one that has been tried
    /// <see cref="MaxWebhookAttempts"/> times. A failure that could clear on its own is left for
    /// a later sweep, each one further apart than the last.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the sweep.</param>
    public async Task<TaskCloseWebhookSweepSummary> SendPendingWebhooksAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var waiting = await context.TaskCloseOperationRuns
            .Include(r => r.Operation)
            .Where(r => r.Status == TaskCloseRunStatus.Pending && r.Kind == TaskCloseOperationKind.Webhook)
            .OrderBy(r => r.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var attempted = 0;
        var succeeded = 0;
        var failed = 0;

        foreach (var run in waiting)
        {
            if (run.LastAttemptAt is { } last && now < last + BackoffAfter(run.Attempts))
            {
                continue;
            }

            if (run.Operation?.WebhookUrl is not { Length: > 0 } url)
            {
                run.Status = TaskCloseRunStatus.Failed;
                run.LastAttemptAt = now;
                run.Detail = "The webhook was deleted before the call went out.";
                failed++;
                continue;
            }

            var payload = run.WebhookPayload is { Length: > 0 } json
                ? JsonSerializer.Deserialize<WebhookPayload>(json, PayloadOptions)!
                : new WebhookPayload("task.completed", run.TaskId ?? 0, run.TaskTitle, null, null, null, run.CompletedAt);

            var result = await webhooks.SendAsync(url, run.Operation.WebhookMethod, payload, cancellationToken);

            attempted++;
            run.Attempts++;
            run.LastAttemptAt = now;
            run.Detail = result.Detail;

            if (result.Succeeded)
            {
                run.Status = TaskCloseRunStatus.Succeeded;
                succeeded++;
            }
            else if (!result.Retryable || run.Attempts >= MaxWebhookAttempts)
            {
                run.Status = TaskCloseRunStatus.Failed;
                run.Detail = $"{result.Detail} Given up after {run.Attempts} attempt(s).";
                failed++;
            }
        }

        if (attempted > 0 || failed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new TaskCloseWebhookSweepSummary(attempted, succeeded, failed);
    }

    /// <summary>How long to wait after <paramref name="attempts"/> failed attempts before the next one.</summary>
    /// <param name="attempts">Attempts made so far.</param>
    public static TimeSpan BackoffAfter(int attempts) =>
        TimeSpan.FromMinutes(Math.Pow(2, Math.Clamp(attempts - 1, 0, 4)));

    /// <summary>
    /// Creates the task an operation asks for, copying what the completed task holds and working
    /// the due date out from the day it was completed. The operations are copied across as well
    /// unless the rule says otherwise, so a chore that is closed and set again keeps going.
    /// </summary>
    private async Task<TaskItem> CreateFollowUpAsync(
        TaskItem task,
        TaskCloseOperation operation,
        DateOnly completedOn,
        CancellationToken cancellationToken)
    {
        var followUp = new TaskItem
        {
            Title = operation.FollowUpTitle ?? task.Title,
            Notes = task.Notes,
            CategoryId = task.CategoryId,
            Status = TaskItemStatus.ToDo,
            DueDate = TaskDueDate.Resolve(operation.DueDateMode, operation.DueDateValue, operation.DueDateOn, completedOn),
        };

        context.Tasks.Add(followUp);
        await context.SaveChangesAsync(cancellationToken);

        if (!operation.CarryOperationsForward)
        {
            return followUp;
        }

        var toCopy = await context.TaskCloseOperations.AsNoTracking()
            .Where(o => o.TaskId == task.Id)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        foreach (var source in toCopy)
        {
            context.TaskCloseOperations.Add(new TaskCloseOperation
            {
                TaskId = followUp.Id,
                Kind = source.Kind,
                SortOrder = source.SortOrder,
                IsEnabled = source.IsEnabled,
                WebhookUrl = source.WebhookUrl,
                WebhookMethod = source.WebhookMethod,
                FollowUpTitle = source.FollowUpTitle,
                DueDateMode = source.DueDateMode,
                DueDateValue = source.DueDateValue,
                DueDateOn = source.DueDateOn,
                CarryOperationsForward = source.CarryOperationsForward,
                DocumentTitle = source.DocumentTitle,
                DocumentBody = source.DocumentBody,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        return followUp;
    }

    private async Task<int> NextSortOrderAsync(int taskId, CancellationToken cancellationToken)
    {
        var highest = await context.TaskCloseOperations
            .Where(o => o.TaskId == taskId)
            .Select(o => (int?)o.SortOrder)
            .MaxAsync(cancellationToken);

        return (highest ?? -1) + 1;
    }

    /// <summary>
    /// Checks that an operation carries what its kind needs. The message is shown to whoever is
    /// filling the form in, so it is thrown without a parameter name tacked onto it.
    /// </summary>
    /// <exception cref="ArgumentException">A field the kind needs is missing or will not do.</exception>
    private static void Validate(TaskCloseOperation operation)
    {
        if (operation.Kind == TaskCloseOperationKind.Webhook
            && WebhookAddress.Validate(operation.WebhookUrl, out _) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        if (operation.Kind == TaskCloseOperationKind.CreateTask
            && TaskDueDate.Validate(operation.DueDateMode, operation.DueDateValue, operation.DueDateOn) is { } wrongDate)
        {
            throw new ArgumentException(wrongDate);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TaskCloseOperationView View(TaskCloseOperation operation, TaskCloseRunView? lastRun) => new(
        operation.Id,
        operation.TaskId,
        operation.Kind,
        operation.SortOrder,
        operation.IsEnabled,
        operation.WebhookUrl,
        operation.WebhookMethod,
        operation.FollowUpTitle,
        operation.DueDateMode,
        operation.DueDateValue,
        operation.DueDateOn,
        operation.CarryOperationsForward,
        operation.DocumentTitle,
        operation.DocumentBody,
        lastRun,
        operation.CreatedAt,
        operation.UpdatedAt);

    private static TaskCloseRunView RunView(TaskCloseOperationRun run) => new(
        run.Id,
        run.OperationId,
        run.TaskId,
        run.TaskTitle,
        run.Kind,
        run.Status,
        run.Attempts,
        run.LastAttemptAt,
        run.Detail,
        run.CreatedTaskId,
        run.DocumentTitle,
        run.DocumentBody,
        run.CompletedAt,
        run.CreatedAt);
}
