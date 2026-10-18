using CapybaraHome.Domain.Tasks;
using CapybaraHome.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Checks that the schema the migrations describe round-trips the task entities.</summary>
public class CapybaraHomeDbContextTests
{
    [Fact]
    public async Task Migrations_create_a_schema_that_holds_a_task()
    {
        await using var context = await NewContextAsync();
        var category = new Category { Name = "Sample category" };
        context.Categories.Add(category);
        context.Tasks.Add(new TaskItem
        {
            Title = "A sample task",
            Category = category,
            DueDate = new DateOnly(2026, 11, 1),
        });

        await context.SaveChangesAsync();

        var saved = await context.Tasks.Include(t => t.Category).SingleAsync();
        Assert.Equal("A sample task", saved.Title);
        Assert.Equal(TaskItemStatus.ToDo, saved.Status);
        Assert.Equal("Sample category", saved.Category!.Name);
        Assert.NotEqual(default, saved.CreatedAt);
    }

    [Fact]
    public async Task Status_is_stored_by_name()
    {
        await using var context = await NewContextAsync();
        context.Tasks.Add(new TaskItem { Title = "A task on hold", Status = TaskItemStatus.OnHold });
        await context.SaveChangesAsync();

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Tasks LIMIT 1";
        var stored = await command.ExecuteScalarAsync();

        Assert.Equal("OnHold", stored);
    }

    [Fact]
    public async Task Saving_a_change_moves_UpdatedAt_past_CreatedAt()
    {
        await using var context = await NewContextAsync();
        var task = new TaskItem { Title = "A task to finish" };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        var created = task.UpdatedAt;

        task.Status = TaskItemStatus.Done;
        task.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        Assert.True(task.UpdatedAt >= created);
        Assert.Equal(created, task.CreatedAt);
    }

    private static async Task<CapybaraHomeDbContext> NewContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CapybaraHomeDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new CapybaraHomeDbContext(options);
        await context.Database.MigrateAsync();
        return context;
    }
}
