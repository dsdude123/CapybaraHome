using CapybaraHome.Application.Jira;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>
/// Covers the JIRA import against snapshots in the shape the REST search returns. The sample
/// issues here are invented; none of the real data is in the repository.
/// </summary>
public class JiraImportServiceTests
{
    private const string Snapshot = """
    {
      "issues": [
        {
          "key": "CAPY-1",
          "fields": {
            "summary": "A standing bucket",
            "issuetype": { "name": "Epic" },
            "status": { "name": "In Progress" }
          }
        },
        {
          "key": "CAPY-2",
          "fields": {
            "summary": "  A task with a parent epic  ",
            "issuetype": { "name": "Task" },
            "status": { "name": "On Hold" },
            "duedate": "2026-11-05",
            "created": "2024-01-02T03:04:05.000+0000",
            "updated": "2025-06-07T08:09:10.000+0000",
            "parent": { "key": "CAPY-1", "fields": { "issuetype": { "name": "Epic" } } },
            "description": {
              "type": "doc",
              "version": 1,
              "content": [
                { "type": "bulletList", "content": [
                  { "type": "listItem", "content": [ { "type": "paragraph", "content": [
                    { "type": "text", "text": "a finished line", "marks": [ { "type": "strike" } ] } ] } ] },
                  { "type": "listItem", "content": [ { "type": "paragraph", "content": [
                    { "type": "text", "text": "a line still to do" } ] } ] }
                ] }
              ]
            }
          }
        },
        {
          "key": "CAPY-3",
          "fields": {
            "summary": "A task with no epic",
            "issuetype": { "name": "Task" },
            "status": { "name": "Done" },
            "resolutiondate": "2026-02-03T04:05:06.000+0000",
            "description": { "type": "doc", "version": 1, "content": [] }
          }
        },
        {
          "key": "CAPY-4",
          "fields": {
            "summary": "A subtask",
            "issuetype": { "name": "Subtask" },
            "status": { "name": "To Do" },
            "parent": { "key": "CAPY-2", "fields": { "issuetype": { "name": "Task" } } }
          }
        }
      ]
    }
    """;

    [Fact]
    public async Task Epics_become_categories_and_issues_become_tasks()
    {
        await using var db = await TestDatabase.CreateAsync();
        var report = await new JiraImportService(db.Context).ImportAsync(Snapshot);

        Assert.Equal(1, report.CategoriesAdded);
        Assert.Equal(3, report.TasksAdded);
        Assert.Equal(0, report.TasksUpdated);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task Titles_come_across_trimmed_and_statuses_map_one_to_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new JiraImportService(db.Context).ImportAsync(Snapshot);

        var task = await db.Context.Tasks.Include(t => t.Category).SingleAsync(t => t.JiraKey == "CAPY-2");

        Assert.Equal("A task with a parent epic", task.Title);
        Assert.Equal(TaskItemStatus.OnHold, task.Status);
        Assert.Equal("A standing bucket", task.Category!.Name);
        Assert.Equal(new DateOnly(2026, 11, 5), task.DueDate);
    }

    [Fact]
    public async Task The_jira_timestamps_are_kept_rather_than_the_import_time()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new JiraImportService(db.Context).ImportAsync(Snapshot);

        var task = await db.Context.Tasks.SingleAsync(t => t.JiraKey == "CAPY-2");

        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), task.CreatedAt);
        Assert.Equal(new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero), task.UpdatedAt);
    }

    [Fact]
    public async Task A_struck_through_checklist_line_survives_as_markdown()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new JiraImportService(db.Context).ImportAsync(Snapshot);

        var task = await db.Context.Tasks.SingleAsync(t => t.JiraKey == "CAPY-2");

        Assert.Contains("- ~~a finished line~~", task.Notes);
        Assert.Contains("- a line still to do", task.Notes);
    }

    [Fact]
    public async Task An_empty_rich_text_field_imports_as_no_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new JiraImportService(db.Context).ImportAsync(Snapshot);

        var task = await db.Context.Tasks.SingleAsync(t => t.JiraKey == "CAPY-3");

        Assert.Null(task.Notes);
        Assert.Null(task.CategoryId);
        Assert.Equal(TaskItemStatus.Done, task.Status);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task A_subtask_keeps_its_parent_as_a_reference_in_the_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        await new JiraImportService(db.Context).ImportAsync(Snapshot);

        var task = await db.Context.Tasks.SingleAsync(t => t.JiraKey == "CAPY-4");

        Assert.Equal("Part of CAPY-2", task.Notes);
    }

    [Fact]
    public async Task Running_the_import_again_updates_rather_than_duplicates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraImportService(db.Context);

        await service.ImportAsync(Snapshot);
        var second = await service.ImportAsync(Snapshot);

        Assert.Equal(0, second.TasksAdded);
        Assert.Equal(3, second.TasksUpdated);
        Assert.Equal(0, second.CategoriesAdded);
        Assert.Equal(3, await db.Context.Tasks.CountAsync());
        Assert.Equal(1, await db.Context.Categories.CountAsync());
    }

    [Fact]
    public async Task A_status_with_no_mapping_lands_in_to_do_and_is_reported()
    {
        await using var db = await TestDatabase.CreateAsync();
        const string json = """
        { "issues": [ { "key": "CAPY-9", "fields": {
            "summary": "An issue in a status we do not know",
            "issuetype": { "name": "Task" },
            "status": { "name": "Blocked" } } } ] }
        """;

        var report = await new JiraImportService(db.Context).ImportAsync(json);

        Assert.Single(report.Warnings);
        Assert.Equal(TaskItemStatus.ToDo, db.Context.Tasks.Single().Status);
    }
}
