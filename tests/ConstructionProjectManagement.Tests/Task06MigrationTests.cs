using ConstructionProjectManagement.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace ConstructionProjectManagement.Tests;

public sealed class Task06MigrationTests
{
    [Fact]
    public async Task Upgrade_PreservesExistingTaskAndProjectTriggers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261122070416_Task04WbsLifecycle");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Projects" ("ProjectCode", "Name", "StartDate", "ExpectedEndDate", "Budget", "Status", "CreatedAt", "UpdatedAt", "IsArchived")
            VALUES ('OLD', 'Old project', '2026-01-01', '2027-01-01', '0', 2, '2026-01-01', '2026-01-01', 0);
            INSERT INTO "Tasks" ("ProjectId", "Title", "Priority", "PlannedStartDate", "PlannedEndDate", "Status", "PercentComplete", "RequiresInspection", "CreatedAt", "UpdatedAt")
            VALUES (1, 'Old task', 1, '2026-01-01', '2027-01-01', 3, '75.0', 1, '2026-01-01', '2026-01-01');
            INSERT INTO "Tasks" ("ProjectId", "ParentTaskId", "Title", "Priority", "PlannedStartDate", "PlannedEndDate", "Status", "PercentComplete", "RequiresInspection", "CreatedAt", "UpdatedAt")
            VALUES (1, 1, 'Old child', 1, '2026-01-01', '2027-01-01', 0, '0', 0, '2026-01-01', '2026-01-01');
            INSERT INTO "TaskDependencies" ("TaskId", "PredecessorTaskId") VALUES (2, 1);
            INSERT INTO "AspNetUsers" ("Id", "IsActive", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('old-user', 1, 0, 0, 0, 0, 0);
            INSERT INTO "TaskAssignments" ("TaskId", "UserId", "AssignedAt") VALUES (1, 'old-user', '2026-01-01');
            INSERT INTO "TaskProgressHistories" ("TaskId", "PreviousPercentComplete", "NewPercentComplete", "PreviousStatus", "NewStatus", "UpdatedById", "UpdatedAt")
            VALUES (1, '0', '75', 0, 3, 'old-user', '2026-01-01');
            INSERT INTO "InspectionRequests" ("ProjectId", "TaskId", "Result", "RequestedById", "RequestedAt")
            VALUES (1, 1, 0, 'old-user', '2026-01-01');
            INSERT INTO "MaterialRequests" ("ProjectId", "TaskId", "RequestedById", "Status", "RequestedAt")
            VALUES (1, 1, 'old-user', 0, '2026-01-01');
            INSERT INTO "Equipment" ("EquipmentCode", "Name", "Status", "IsActive") VALUES ('OLD-EQ', 'Old equipment', 0, 1);
            INSERT INTO "EquipmentAssignments" ("EquipmentId", "ProjectId", "TaskId", "AssignedToId", "HandedOverById", "HandedOverAt")
            VALUES (1, 1, 1, 'old-user', 'old-user', '2026-01-01');
            """);

        await migrator.MigrateAsync();
        var task = await db.Tasks.SingleAsync(item => item.Title == "Old task");
        Assert.Equal("Old task", task.Title);
        Assert.Equal(75m, task.PercentComplete);
        Assert.Equal(1, (await db.Tasks.SingleAsync(item => item.Title == "Old child")).ParentTaskId);
        var dependency = await db.TaskDependencies.SingleAsync();
        Assert.Equal("FS", dependency.DependencyType);
        Assert.Equal(1, dependency.PredecessorTaskId);
        Assert.Equal(1, await db.TaskAssignments.CountAsync());
        Assert.Equal(1, await db.TaskProgressHistories.CountAsync());
        Assert.Equal(1, (await db.InspectionRequests.SingleAsync()).TaskId);
        Assert.Equal(1, (await db.MaterialRequests.SingleAsync()).TaskId);
        Assert.Equal(1, (await db.EquipmentAssignments.SingleAsync()).TaskId);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'TR_Projects_PMHistory_%'";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "PRAGMA foreign_key_check";
        await using var violations = await command.ExecuteReaderAsync();
        Assert.False(await violations.ReadAsync());
    }

    [Fact]
    public async Task Startup_RequiresExplicitPermissionForTask06Migration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261122070416_Task04WbsLifecycle");
        var config = new ConfigurationBuilder().Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DbInitializer.ApplyMigrationsAsync(db, config));
        Assert.Contains("CPMS_ALLOW_TASK06_MIGRATION", exception.Message);
        Assert.Contains(await db.Database.GetPendingMigrationsAsync(), name => name.EndsWith("_AddTask06TrackingExtensions"));
    }
}
