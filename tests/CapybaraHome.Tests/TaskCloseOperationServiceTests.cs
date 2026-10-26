using System.Net;
using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers what happens when a task with operations on it is moved to done.</summary>
public class TaskCloseOperationServiceTests
{
    [Fact]
    public async Task Completing_a_task_creates_the_follow_up_with_the_due_date_the_rule_gives()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var task = await tasks.CreateAsync(new CreateTaskRequest("Change the furnace filter", "Behind the door"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.CreateTask,
            DueDateMode: TaskDueDateMode.InWeeks,
            DueDateValue: 6));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var followUp = Assert.Single(await tasks.ListAsync(new TaskFilter()), t => t.Id != task.Id);

        Assert.Equal("Change the furnace filter", followUp.Title);
        Assert.Equal("Behind the door", followUp.Notes);
        Assert.Equal(TaskItemStatus.ToDo, followUp.Status);
        Assert.Equal(DateOnly.FromDateTime(db.Clock.GetUtcNow().UtcDateTime).AddDays(42), followUp.DueDate);
    }

    [Fact]
    public async Task The_due_date_is_counted_from_when_the_task_was_actually_completed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var task = await tasks.CreateAsync(new CreateTaskRequest("Water the plants"));

        await db.CloseOperations().CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.CreateTask,
            DueDateMode: TaskDueDateMode.InDays,
            DueDateValue: 3));

        // Closed a fortnight after it was set up: the new task is due three days after the
        // completion, not three days after the rule was written.
        db.Clock.Advance(TimeSpan.FromDays(14));
        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var followUp = Assert.Single(await tasks.ListAsync(new TaskFilter()), t => t.Id != task.Id);
        Assert.Equal(DateOnly.FromDateTime(db.Clock.GetUtcNow().UtcDateTime).AddDays(3), followUp.DueDate);
    }

    [Fact]
    public async Task The_follow_up_keeps_the_category_the_title_it_was_given_and_the_operations()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var category = await new CategoryService(db.Context).CreateAsync(new CategoryRequest("Upkeep"));
        var task = await tasks.CreateAsync(new CreateTaskRequest("Service the mower", CategoryId: category.Id));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.CreateTask,
            FollowUpTitle: "Service the mower again",
            DueDateMode: TaskDueDateMode.InYears,
            DueDateValue: 1));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var followUp = Assert.Single(await tasks.ListAsync(new TaskFilter()), t => t.Id != task.Id);
        Assert.Equal("Service the mower again", followUp.Title);
        Assert.Equal(category.Id, followUp.CategoryId);

        var carried = Assert.Single(await operations.ListAsync(followUp.Id));
        Assert.Equal(TaskDueDateMode.InYears, carried.DueDateMode);

        // And the chain goes on: closing the new one sets the next.
        await tasks.UpdateAsync(followUp.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        Assert.Equal(3, (await tasks.ListAsync(new TaskFilter())).Count);
    }

    [Fact]
    public async Task A_rule_that_does_not_carry_its_operations_forward_stops_after_one_task()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var task = await tasks.CreateAsync(new CreateTaskRequest("File the tax return"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.CreateTask,
            DueDateMode: TaskDueDateMode.EndOfQuarter,
            CarryOperationsForward: false));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var followUp = Assert.Single(await tasks.ListAsync(new TaskFilter()), t => t.Id != task.Id);
        Assert.Empty(await operations.ListAsync(followUp.Id));

        await tasks.UpdateAsync(followUp.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        Assert.Equal(2, (await tasks.ListAsync(new TaskFilter())).Count);
    }

    [Fact]
    public async Task A_document_operation_produces_the_task_title_and_notes_unless_it_was_given_its_own()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var task = await tasks.CreateAsync(new CreateTaskRequest("Read the meter", "Cupboard under the stairs"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(TaskCloseOperationKind.PrintDocument));
        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.PrintDocument,
            DocumentTitle: "Meter reading slip",
            DocumentBody: "Reading: ____________"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var documents = await operations.ListDocumentsAsync();
        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, d => d.DocumentTitle == "Read the meter" && d.DocumentBody == "Cupboard under the stairs");
        Assert.Contains(documents, d => d.DocumentTitle == "Meter reading slip" && d.DocumentBody == "Reading: ____________");
        Assert.All(documents, d => Assert.Equal(TaskCloseRunStatus.Succeeded, d.Status));
    }

    [Fact]
    public async Task A_webhook_is_queued_and_sent_by_the_sweep_with_the_task_in_its_body()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply("ok");
        var operations = db.CloseOperations(TestDatabase.Sender(http));
        var tasks = db.Tasks(TestDatabase.Sender(http));
        var task = await tasks.CreateAsync(new CreateTaskRequest("Take the bins out", "Green one this week"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/bins"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var queued = Assert.Single(await operations.ListRunsAsync());
        Assert.Equal(TaskCloseRunStatus.Pending, queued.Status);
        Assert.Empty(http.Requests);

        var sweep = await operations.SendPendingWebhooksAsync();

        Assert.Equal(1, sweep.Attempted);
        Assert.Equal(1, sweep.Succeeded);

        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hooks.example.com/bins", request.RequestUri!.ToString());

        var body = Assert.Single(http.Bodies);
        Assert.Contains("task.completed", body);
        Assert.Contains("Take the bins out", body);
        Assert.Contains("Green one this week", body);

        var sent = Assert.Single(await operations.ListRunsAsync());
        Assert.Equal(TaskCloseRunStatus.Succeeded, sent.Status);
    }

    [Fact]
    public async Task A_get_webhook_sends_no_body()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply("ok");
        var operations = db.CloseOperations(TestDatabase.Sender(http));
        var tasks = db.Tasks(TestDatabase.Sender(http));
        var task = await tasks.CreateAsync(new CreateTaskRequest("Open the gate"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/gate",
            WebhookMethod: TaskWebhookMethod.Get));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        await operations.SendPendingWebhooksAsync();

        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(string.Empty, Assert.Single(http.Bodies));
    }

    [Fact]
    public async Task A_target_that_keeps_failing_is_retried_further_apart_and_then_given_up_on()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply("down", HttpStatusCode.InternalServerError);
        var operations = db.CloseOperations(TestDatabase.Sender(http));
        var tasks = db.Tasks(TestDatabase.Sender(http));
        var task = await tasks.CreateAsync(new CreateTaskRequest("Tell the heating"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/heating"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        for (var attempt = 1; attempt <= TaskCloseOperationService.MaxWebhookAttempts; attempt++)
        {
            var sweep = await operations.SendPendingWebhooksAsync();
            Assert.Equal(1, sweep.Attempted);

            // A sweep that comes round before the backoff has passed leaves the run alone.
            Assert.Equal(0, (await operations.SendPendingWebhooksAsync()).Attempted);
            db.Clock.Advance(TaskCloseOperationService.BackoffAfter(attempt) + TimeSpan.FromSeconds(1));
        }

        var run = Assert.Single(await operations.ListRunsAsync());
        Assert.Equal(TaskCloseRunStatus.Failed, run.Status);
        Assert.Equal(TaskCloseOperationService.MaxWebhookAttempts, run.Attempts);
        Assert.Contains("Given up", run.Detail);
    }

    [Fact]
    public async Task A_refusal_the_target_meant_is_not_retried()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply("no", HttpStatusCode.BadRequest);
        var operations = db.CloseOperations(TestDatabase.Sender(http));
        var tasks = db.Tasks(TestDatabase.Sender(http));
        var task = await tasks.CreateAsync(new CreateTaskRequest("Ring the doorbell"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/bell"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        await operations.SendPendingWebhooksAsync();

        var run = Assert.Single(await operations.ListRunsAsync());
        Assert.Equal(TaskCloseRunStatus.Failed, run.Status);
        Assert.Equal(1, run.Attempts);
        Assert.Contains("400", run.Detail);
    }

    [Fact]
    public async Task Only_a_move_to_done_runs_the_operations_and_a_paused_one_never_does()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var task = await tasks.CreateAsync(new CreateTaskRequest("Sweep the path"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.PrintDocument,
            IsEnabled: false));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.InProgress));
        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Notes: "Still done"));

        Assert.Empty(await operations.ListRunsAsync());

        await operations.UpdateAsync((await operations.ListAsync(task.Id)).Single().Id, new UpdateTaskCloseOperationRequest(IsEnabled: true));

        // Already done: saving the same status again is not a completion.
        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        Assert.Empty(await operations.ListRunsAsync());

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.ToDo));
        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        Assert.Single(await operations.ListRunsAsync());
    }

    [Fact]
    public async Task A_webhook_without_a_URL_the_server_may_call_is_refused_when_it_is_saved()
    {
        await using var db = await TestDatabase.CreateAsync();
        var operations = db.CloseOperations();
        var task = await db.Tasks().CreateAsync(new CreateTaskRequest("Poke the server"));

        foreach (var url in new[] { null, "", "not a url", "file:///etc/passwd", "http://127.0.0.1/x", "http://169.254.169.254/" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => operations.CreateAsync(
                task.Id,
                new CreateTaskCloseOperationRequest(TaskCloseOperationKind.Webhook, WebhookUrl: url)));
        }

        Assert.Empty(await operations.ListAsync(task.Id));
    }

    [Fact]
    public async Task A_due_date_rule_that_is_missing_its_number_is_refused_when_it_is_saved()
    {
        await using var db = await TestDatabase.CreateAsync();
        var operations = db.CloseOperations();
        var task = await db.Tasks().CreateAsync(new CreateTaskRequest("Rotate the mattress"));

        await Assert.ThrowsAsync<ArgumentException>(() => operations.CreateAsync(
            task.Id,
            new CreateTaskCloseOperationRequest(TaskCloseOperationKind.CreateTask, DueDateMode: TaskDueDateMode.InMonths)));

        await Assert.ThrowsAsync<ArgumentException>(() => operations.CreateAsync(
            task.Id,
            new CreateTaskCloseOperationRequest(TaskCloseOperationKind.CreateTask, DueDateMode: TaskDueDateMode.OnDate)));
    }

    [Fact]
    public async Task An_operation_cannot_be_hung_off_a_task_that_does_not_exist()
    {
        await using var db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => db.CloseOperations().CreateAsync(
            404,
            new CreateTaskCloseOperationRequest(TaskCloseOperationKind.PrintDocument)));
    }

    [Fact]
    public async Task A_produced_document_outlives_the_operation_and_the_task_it_came_from()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var task = await tasks.CreateAsync(new CreateTaskRequest("Log the boiler service"));

        var operation = await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.PrintDocument,
            DocumentTitle: "Boiler service record"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        var document = Assert.Single(await operations.ListDocumentsAsync());

        await operations.DeleteAsync(operation.Id);
        await tasks.DeleteAsync(task.Id);
        db.Context.ChangeTracker.Clear();

        var kept = await operations.GetRunAsync(document.Id);
        Assert.NotNull(kept);
        Assert.Equal("Boiler service record", kept!.DocumentTitle);
        Assert.Equal("Log the boiler service", kept.TaskTitle);
        Assert.Null(kept.TaskId);
        Assert.Null(kept.OperationId);
    }

    [Fact]
    public async Task A_webhook_whose_operation_was_deleted_before_the_sweep_is_not_sent()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().Reply("ok");
        var operations = db.CloseOperations(TestDatabase.Sender(http));
        var tasks = db.Tasks(TestDatabase.Sender(http));
        var task = await tasks.CreateAsync(new CreateTaskRequest("Shout at the cloud"));

        var operation = await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/cloud"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        await operations.DeleteAsync(operation.Id);
        db.Context.ChangeTracker.Clear();

        var sweep = await operations.SendPendingWebhooksAsync();

        Assert.Equal(0, sweep.Attempted);
        Assert.Equal(1, sweep.Failed);
        Assert.Empty(http.Requests);
        Assert.Equal(TaskCloseRunStatus.Failed, Assert.Single(await operations.ListRunsAsync()).Status);
    }

    [Fact]
    public async Task The_operations_run_in_the_order_they_were_added()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = db.Tasks();
        var operations = db.CloseOperations();
        var task = await tasks.CreateAsync(new CreateTaskRequest("Close the season"));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(TaskCloseOperationKind.PrintDocument));
        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.CreateTask,
            DueDateMode: TaskDueDateMode.NextQuarter));
        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/season"));

        await tasks.UpdateAsync(task.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));

        var runs = await db.Context.TaskCloseOperationRuns.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(
            [TaskCloseOperationKind.PrintDocument, TaskCloseOperationKind.CreateTask, TaskCloseOperationKind.Webhook],
            runs.Select(r => r.Kind));
    }
}
