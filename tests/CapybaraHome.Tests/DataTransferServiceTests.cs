using CapybaraHome.Application.Categories;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Tests;

/// <summary>Covers the export and import that back the automatic backups.</summary>
public class DataTransferServiceTests
{
    [Fact]
    public async Task An_export_imported_into_an_empty_database_restores_what_was_there()
    {
        await using var source = await TestDatabase.CreateAsync();
        var tasks = new TaskService(source.Context, source.Clock);
        var categories = new CategoryService(source.Context);
        var category = await categories.CreateAsync(new CategoryRequest("Garage", "#A8D5BA", 3));
        await tasks.CreateAsync(new CreateTaskRequest("Clear the shelves", "with the ladder", TaskItemStatus.InProgress, category.Id, new DateOnly(2026, 12, 1)));

        var json = await new DataTransferService(source.Context, source.Clock).ExportToJsonAsync();

        await using var target = await TestDatabase.CreateAsync();
        var summary = await new DataTransferService(target.Context, target.Clock).ImportFromJsonAsync(json, ImportMode.Merge);

        Assert.Equal(1, summary.CategoriesAdded);
        Assert.Equal(1, summary.TasksAdded);

        var restored = await new TaskService(target.Context, target.Clock).ListAsync(new TaskFilter());
        var restoredTask = Assert.Single(restored);
        Assert.Equal("Clear the shelves", restoredTask.Title);
        Assert.Equal("with the ladder", restoredTask.Notes);
        Assert.Equal(TaskItemStatus.InProgress, restoredTask.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), restoredTask.DueDate);
        Assert.Equal("Garage", restoredTask.CategoryName);
    }

    [Fact]
    public async Task A_merge_updates_the_tasks_it_can_match_on_their_jira_key()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        db.Context.Tasks.Add(new TaskItem { Title = "Old title", JiraKey = "CAPY-1" });
        await db.Context.SaveChangesAsync();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            db.Clock.GetUtcNow(),
            [],
            [new TaskRecord(1, "New title", null, null, TaskItemStatus.Done, null, null, "CAPY-1", db.Clock.GetUtcNow(), db.Clock.GetUtcNow())],
            []);

        var summary = await transfer.ImportAsync(document, ImportMode.Merge);

        Assert.Equal(1, summary.TasksUpdated);
        Assert.Equal(0, summary.TasksAdded);
        Assert.Equal("New title", db.Context.Tasks.Single().Title);
    }

    [Fact]
    public async Task Replacing_clears_what_was_there_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        db.Context.Tasks.Add(new TaskItem { Title = "Should not survive" });
        await db.Context.SaveChangesAsync();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            db.Clock.GetUtcNow(),
            [],
            [new TaskRecord(1, "The only task", null, null, TaskItemStatus.ToDo, null, null, null, db.Clock.GetUtcNow(), db.Clock.GetUtcNow())],
            []);

        await transfer.ImportAsync(document, ImportMode.Replace);

        Assert.Equal("The only task", db.Context.Tasks.Single().Title);
    }

    [Fact]
    public async Task A_document_from_a_later_format_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);

        var json = System.Text.Json.JsonSerializer.Serialize(
            new BackupDocument(99, db.Clock.GetUtcNow(), [], [], []),
            DataTransferService.JsonOptions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transfer.ImportFromJsonAsync(json, ImportMode.Merge));
    }

    [Fact]
    public async Task A_backup_writes_a_file_and_keeps_only_the_newest_ones()
    {
        await using var db = await TestDatabase.CreateAsync();
        var destination = Directory.CreateTempSubdirectory("capybarahome-tests");

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new BackupOptions
            {
                Enabled = true,
                Destination = destination.FullName,
                Keep = 2,
            });

            var writer = new BackupWriter(new DataTransferService(db.Context, db.Clock), options, db.Clock);

            for (var i = 0; i < 3; i++)
            {
                await writer.WriteAsync();
                db.Clock.Advance(TimeSpan.FromMinutes(1));
            }

            Assert.Equal(2, destination.GetFiles("capybarahome-*.json").Length);
        }
        finally
        {
            destination.Delete(recursive: true);
        }
    }
}
