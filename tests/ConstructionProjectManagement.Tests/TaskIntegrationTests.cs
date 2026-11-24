using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Controllers;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatus = ConstructionProjectManagement.Models.TaskStatus;
using Task = System.Threading.Tasks.Task;

namespace ConstructionProjectManagement.Tests;

public sealed class TaskIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ApplicationDbContext _db = null!;
    private Project _project = null!;
    private TaskEntity _ownTask = null!;
    private TaskEntity _otherTask = null!;
    private Material _material = null!;
    private Equipment _equipment = null!;
    private ApplicationUser _user = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection).Options);
        await _db.Database.MigrateAsync();
        _project = new Project { ProjectCode = "P1", Name = "Dự án 1" };
        var otherProject = new Project { ProjectCode = "P2", Name = "Dự án 2" };
        _user = new ApplicationUser { Id = "user", UserName = "user", IsActive = true };
        _ownTask = new TaskEntity { Project = _project, Title = "Công việc 1", PlannedEndDate = DateTime.UtcNow.AddDays(10) };
        _otherTask = new TaskEntity { Project = otherProject, Title = "Công việc 2", PlannedEndDate = DateTime.UtcNow.AddDays(10) };
        _material = new Material { MaterialCode = "M1", Name = "Vật tư", Unit = "cái" };
        _equipment = new Equipment { EquipmentCode = "E1", Name = "Thiết bị" };
        _db.AddRange(_project, otherProject, _user, _ownTask, _otherTask, _material, _equipment);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", false)]
    [InlineData("Supervising Engineer", false)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", false)]
    public async Task ResolveBlocker_OnlyManagersCanResolveOthersComments(string role, bool allowed)
    {
        var author = new ApplicationUser { Id = "author", UserName = "author", IsActive = true };
        _project.ClientName = _user.OrganizationName = "Client";
        _db.Users.Add(author);
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        var comment = new TaskComment { TaskId = _ownTask.Id, AuthorId = author.Id, Content = "Vướng mắc", IsBlocker = true };
        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync();
        var result = await new TaskCommentService(_db).ResolveBlockerAsync(_ownTask.Id, comment.Id, _user.Id, [role]);
        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(!allowed, (await _db.TaskComments.AsNoTracking().SingleAsync()).IsBlocker);
        Assert.Equal(allowed ? 1 : 0, await _db.TaskAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("Site Manager")]
    [InlineData("Supervising Engineer")]
    [InlineData("Worker")]
    [InlineData("Materials Staff")]
    public async Task ResolveBlocker_AuthorCanResolveOwnOldComment(string role)
    {
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        var comment = new TaskComment { TaskId = _ownTask.Id, AuthorId = _user.Id, Content = "Vướng mắc",
            IsBlocker = true, CreatedAt = DateTime.UtcNow.AddDays(-1) };
        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync();
        var result = await new TaskCommentService(_db).ResolveBlockerAsync(_ownTask.Id, comment.Id, _user.Id, [role]);
        Assert.True(result.Succeeded, result.Error);
        Assert.Null(comment.UpdatedAt);
        Assert.Equal(_user.Id, (await _db.TaskAuditLogs.SingleAsync()).PerformedById);
    }

    [Fact]
    public async Task ResolveBlocker_PreservesTaskAndOtherCommentsAndRejectsRepeat()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 35;
        var stamp = _ownTask.ConcurrencyStamp;
        var predecessor = new TaskEntity { ProjectId = _project.Id, Title = "Tiền nhiệm", PlannedEndDate = DateTime.Today.AddDays(1) };
        var first = new TaskComment { Task = _ownTask, AuthorId = _user.Id, Content = "Một", IsBlocker = true, UpdatedAt = DateTime.UtcNow.AddHours(-1) };
        var second = new TaskComment { Task = _ownTask, AuthorId = _user.Id, Content = "Hai", IsBlocker = true };
        _db.AddRange(predecessor, first, second);
        await _db.SaveChangesAsync();
        _db.TaskDependencies.Add(new TaskDependency { TaskId = _ownTask.Id, PredecessorTaskId = predecessor.Id });
        await _db.SaveChangesAsync();
        var editedAt = first.UpdatedAt;
        var service = new TaskCommentService(_db);
        Assert.True((await service.ResolveBlockerAsync(_ownTask.Id, first.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.True(await _db.TaskComments.AnyAsync(item => item.TaskId == _ownTask.Id && item.IsBlocker));
        Assert.False((await service.ResolveBlockerAsync(_ownTask.Id, first.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.True((await service.ResolveBlockerAsync(_ownTask.Id, second.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.False(await _db.TaskComments.AnyAsync(item => item.TaskId == _ownTask.Id && item.IsBlocker));
        Assert.Equal(editedAt, first.UpdatedAt);
        Assert.Equal("Một", first.Content);
        Assert.Equal(TaskStatus.InProgress, _ownTask.Status);
        Assert.Equal(35m, _ownTask.PercentComplete);
        Assert.Equal(stamp, _ownTask.ConcurrencyStamp);
        Assert.Single(await _db.TaskDependencies.ToListAsync());
        var logs = await _db.TaskAuditLogs.ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.Equal("BlockerResolved", log.Action);
            Assert.Equal("IsBlocker", log.FieldName);
            Assert.Equal("True", log.OldValue);
            Assert.Equal("False", log.NewValue);
        });
        Assert.Contains(logs, log => log.Reason == $"Bình luận #{first.Id}");
    }

    [Fact]
    public async Task ResolveBlocker_RejectsReadonlyOutsideScopeInactiveAndWrongTask()
    {
        _project.ProjectManagerId = _user.Id;
        var comment = new TaskComment { Task = _ownTask, AuthorId = _user.Id, Content = "Vướng mắc", IsBlocker = true };
        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync();
        var service = new TaskCommentService(_db);
        Assert.False((await service.ResolveBlockerAsync(_otherTask.Id, comment.Id, _user.Id, ["Project Manager"])).Succeeded);
        _project.Status = ProjectStatus.Completed;
        await _db.SaveChangesAsync();
        Assert.False((await service.ResolveBlockerAsync(_ownTask.Id, comment.Id, _user.Id, ["Project Manager"])).Succeeded);
        _project.Status = ProjectStatus.InProgress;
        _project.ProjectManagerId = null;
        await _db.SaveChangesAsync();
        Assert.False((await service.ResolveBlockerAsync(_ownTask.Id, comment.Id, _user.Id, ["Project Manager"])).Succeeded);
        _project.ProjectManagerId = _user.Id;
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.False((await service.ResolveBlockerAsync(_ownTask.Id, comment.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.True((await _db.TaskComments.AsNoTracking().SingleAsync()).IsBlocker);
        Assert.Empty(await _db.TaskAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("same", true)]
    [InlineData("other", false)]
    [InlineData("missing", false)]
    public async Task MaterialRequest_RequiresTaskInSameProject(string selection, bool allowed)
    {
        var result = await new MaterialService(_db).CreateRequestAsync(
            new MaterialRequestCreateViewModel
            {
                ProjectId = _project.Id,
                TaskId = TaskId(selection),
                Items = [new MaterialRequestLineViewModel { MaterialId = _material.Id, QuantityRequested = 1 }]
            },
            _user.Id);

        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(allowed ? 1 : 0, await _db.MaterialRequests.CountAsync());
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("same", true)]
    [InlineData("other", false)]
    [InlineData("missing", false)]
    public async Task EquipmentAssignment_RequiresTaskInSameProject(string selection, bool allowed)
    {
        var result = await new EquipmentService(_db).AssignAsync(
            new EquipmentAssignmentViewModel
            {
                EquipmentId = _equipment.Id,
                ProjectId = _project.Id,
                TaskId = TaskId(selection),
                AssignedToId = _user.Id
            },
            _user.Id);

        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(allowed ? 1 : 0, await _db.EquipmentAssignments.CountAsync());
        Assert.Equal(allowed ? EquipmentStatus.InUse : EquipmentStatus.Available, _equipment.Status);
    }

    [Fact]
    public async Task CreateTask_PersistsInitialHistoryWithGeneratedTaskId()
    {
        _project.ProjectManagerId = _user.Id;
        await _db.SaveChangesAsync();
        var result = await new TaskService(_db, new WbsService(_db)).CreateAsync(
            new TaskEntity { ProjectId = _project.Id, Title = "New task", PlannedEndDate = DateTime.UtcNow.AddDays(1) },
            [], _user.Id, false, ["Project Manager"]);
        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Task!.Id > 0);
        var history = await _db.TaskProgressHistories.SingleAsync();
        Assert.Equal(result.Task.Id, history.TaskId);
        Assert.Equal(TaskStatus.Pending, history.NewStatus);
    }

    [Fact]
    public async Task AdjustSchedule_SiteManagerChangesOnlyDatesAndAuditsBothFields()
    {
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 20;
        _ownTask.PlannedStartDate = DateTime.Today;
        _ownTask.PlannedEndDate = DateTime.Today.AddDays(10);
        await _db.SaveChangesAsync();
        var oldStamp = _ownTask.ConcurrencyStamp;
        var oldTitle = _ownTask.Title;
        var service = new TaskService(_db, new WbsService(_db));
        var result = await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today.AddDays(1), DateTime.Today.AddDays(15),
            oldStamp, "Thay đổi kế hoạch thi công", _user.Id, ["Site Manager"]);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(oldTitle, _ownTask.Title);
        Assert.Equal(TaskStatus.InProgress, _ownTask.Status);
        Assert.Equal(20m, _ownTask.PercentComplete);
        Assert.NotEqual(oldStamp, _ownTask.ConcurrencyStamp);
        var logs = await _db.TaskAuditLogs.OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.Equal("ScheduleUpdated", log.Action);
            Assert.Equal(_user.Id, log.PerformedById);
            Assert.Equal("Thay đổi kế hoạch thi công", log.Reason);
        });
        Assert.Equal(DateTime.Today.ToString("O"), logs[0].OldValue);
        Assert.Equal(DateTime.Today.AddDays(1).ToString("O"), logs[0].NewValue);
        Assert.Equal(DateTime.Today.AddDays(10).ToString("O"), logs[1].OldValue);
        Assert.Equal(DateTime.Today.AddDays(15).ToString("O"), logs[1].NewValue);
        var stale = await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today, DateTime.Today.AddDays(20),
            oldStamp, "Lý do điều chỉnh", _user.Id, ["Site Manager"]);
        Assert.False(stale.Succeeded);
        Assert.Contains("tải lại", stale.Error);
        Assert.Equal(2, await _db.TaskAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Cancelled, false)]
    [InlineData(ProjectStatus.InProgress, true)]
    public async Task AdjustSchedule_ReadOnlyProjectIsRejected(ProjectStatus status, bool archived)
    {
        _project.Status = status;
        _project.IsArchived = archived;
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 20;
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        await _db.SaveChangesAsync();
        var oldEnd = _ownTask.PlannedEndDate;
        var result = await new TaskService(_db, new WbsService(_db)).AdjustScheduleAsync(
            _ownTask.Id, DateTime.Today, DateTime.Today.AddDays(20), _ownTask.ConcurrencyStamp,
            "Điều chỉnh kế hoạch", _user.Id, ["Site Manager"]);
        Assert.False(result.Succeeded);
        Assert.Equal(oldEnd, _ownTask.PlannedEndDate);
        Assert.Empty(await _db.TaskAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AdjustSchedule_RejectsOutsideScopeInvalidInputAndClosedTasks()
    {
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 20;
        _otherTask.Status = TaskStatus.InProgress;
        _otherTask.PercentComplete = 20;
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        await _db.SaveChangesAsync();
        var service = new TaskService(_db, new WbsService(_db));
        Assert.False((await service.AdjustScheduleAsync(_otherTask.Id, DateTime.Today, DateTime.Today.AddDays(2),
            _otherTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, ["Site Manager"])).Succeeded);
        Assert.False((await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today.AddDays(2), DateTime.Today,
            _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, ["Site Manager"])).Succeeded);
        Assert.False((await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today, DateTime.Today.AddDays(2),
            _ownTask.ConcurrencyStamp, "  ", _user.Id, ["Site Manager"])).Succeeded);
        Assert.False((await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today, DateTime.Today.AddDays(2),
            _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, ["Worker"])).Succeeded);
        _ownTask.Status = TaskStatus.Completed;
        _ownTask.PercentComplete = 100;
        await _db.SaveChangesAsync();
        Assert.False((await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today, DateTime.Today.AddDays(2),
            _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, ["Site Manager"])).Succeeded);
        _ownTask.Status = TaskStatus.Cancelled;
        await _db.SaveChangesAsync();
        Assert.False((await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today, DateTime.Today.AddDays(2),
            _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, ["Site Manager"])).Succeeded);
        _ownTask.Status = TaskStatus.Pending;
        _ownTask.PercentComplete = 0;
        await _db.SaveChangesAsync();
        Assert.False((await service.AdjustScheduleAsync(_ownTask.Id, DateTime.Today, DateTime.Today.AddDays(2),
            _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, ["Site Manager"])).Succeeded);
        Assert.Empty(await _db.TaskAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AdjustSchedule_RejectsInactiveAndMismatchedClientAndInvalidModelOutsideScope()
    {
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 20;
        _otherTask.Status = TaskStatus.InProgress;
        _otherTask.PercentComplete = 20;
        var membership = new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true };
        _db.ProjectMembers.Add(membership);
        await _db.SaveChangesAsync();
        var controller = TasksController("Site Manager");
        controller.ModelState.AddModelError("Reason", "Invalid");
        var model = new TaskScheduleViewModel { Id = _otherTask.Id };
        Assert.IsType<NotFoundResult>(await controller.AdjustSchedule(_otherTask.Id, model, default));
        var service = new TaskService(_db, new WbsService(_db));
        async Task<bool> TryUpdate(string[] roles) => (await service.AdjustScheduleAsync(_ownTask.Id,
            DateTime.Today, DateTime.Today.AddDays(20), _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, roles)).Succeeded;
        _project.ClientName = "Client A";
        _user.OrganizationName = "Client B";
        await _db.SaveChangesAsync();
        Assert.False(await TryUpdate(["Site Manager", "Client Representative"]));
        membership.IsActive = false;
        _user.OrganizationName = "Client A";
        await _db.SaveChangesAsync();
        Assert.False(await TryUpdate(["Site Manager"]));
        Assert.False(await TryUpdate(["Site Manager", "Client Representative"]));
        membership.IsActive = true;
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.False(await TryUpdate(["Site Manager"]));
        Assert.Empty(await _db.TaskAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", true)]
    [InlineData("Supervising Engineer", false)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", false)]
    public async Task AdjustSchedule_RespectsEightRoleMatrix(string role, bool allowed)
    {
        _project.ClientName = _user.OrganizationName = "Client A";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 20;
        await _db.SaveChangesAsync();
        var access = await TaskAccess.LoadAsync(_db, _user.Id, [role]);
        Assert.NotNull(access);
        Assert.Equal(allowed, access.CanAdjustSchedule(_ownTask));
        if (role == "Site Manager") Assert.False(access.CanEditTask(_project.Id));
        var result = await new TaskService(_db, new WbsService(_db)).AdjustScheduleAsync(_ownTask.Id,
            DateTime.Today, DateTime.Today.AddDays(20), _ownTask.ConcurrencyStamp, "Lý do hợp lệ", _user.Id, [role]);
        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public async Task Dependency_RejectsCyclesAndCrossProjectLinks()
    {
        _project.ProjectManagerId = _user.Id;
        var third = new TaskEntity { ProjectId = _project.Id, Title = "Third" };
        _db.Tasks.Add(third);
        await _db.SaveChangesAsync();
        var service = new TaskDependencyService(_db);
        Assert.True((await service.AddAsync(_ownTask.Id, third.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.False((await service.AddAsync(third.Id, _ownTask.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.False((await service.AddAsync(third.Id, _otherTask.Id, _user.Id, ["Project Manager"])).Succeeded);
        Assert.Single(await _db.TaskDependencies.ToListAsync());
        Assert.Single(await _db.TaskAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Comment_WorkerMustBeAssignedAndClientCannotPost()
    {
        _project.ClientName = _user.OrganizationName = "Client";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        await _db.SaveChangesAsync();
        var service = new TaskCommentService(_db);
        Assert.False((await service.AddAsync(_ownTask.Id, "Blocked", true, _user.Id, ["Worker"])).Succeeded);
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        Assert.True((await service.AddAsync(_ownTask.Id, "Blocked", true, _user.Id, ["Worker"])).Succeeded);
        Assert.False((await service.AddAsync(_ownTask.Id, "Client comment", false, _user.Id, ["Client Representative"])).Succeeded);
        Assert.True((await _db.TaskComments.SingleAsync()).IsBlocker);
    }

    [Fact]
    public async Task ProgressFilter_PaginatesAndFiltersInScopedSqliteQuery()
    {
        _project.ProjectManagerId = _user.Id;
        for (var index = 0; index < 25; index++)
            _db.Tasks.Add(new TaskEntity
            {
                ProjectId = _project.Id, Title = $"Needle {index}",
                Priority = TaskPriority.High, PlannedEndDate = DateTime.Today.AddDays(-1)
            });
        await _db.SaveChangesAsync();
        var access = await TaskAccess.LoadAsync(_db, _user.Id, ["Project Manager"]);
        var service = new ProgressService(_db);
        var filter = new ProgressFilterViewModel
        {
            ProjectId = _project.Id, Priority = TaskPriority.High, Keyword = "Needle",
            OnlyOverdue = true, Page = 2
        };
        var result = await service.GetOverviewAsync(filter, access!);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(5, result.Tasks.Count);
        Assert.All(result.Tasks, task => Assert.Equal(_project.Id, task.ProjectId));
        Assert.Single(await service.GetChartAsync(_user.Id, ["Project Manager"], filter: filter));
    }

    [Fact]
    public async Task Progress_RejectsStaleConcurrencyStampWithoutChanges()
    {
        _project.ProjectManagerId = _user.Id;
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        var stamp = _ownTask.ConcurrencyStamp;
        var service = new ProgressService(_db);
        Assert.True((await service.UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress, null,
            _user.Id, ["Project Manager"], concurrencyStamp: stamp)).Succeeded);
        var stale = await service.UpdateAsync(_ownTask.Id, 20, TaskStatus.InProgress, null,
            _user.Id, ["Project Manager"], concurrencyStamp: stamp);
        Assert.False(stale.Succeeded);
        Assert.Contains("tải lại", stale.Error);
        Assert.Equal(10, _ownTask.PercentComplete);
        Assert.Single(await _db.TaskProgressHistories.ToListAsync());
    }

    [Fact]
    public async Task Progress_DetectsConcurrentWriteFromAnotherContext()
    {
        _project.ProjectManagerId = _user.Id;
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        var stale = _ownTask.ConcurrencyStamp;
        await using var other = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection).Options);
        var first = await new ProgressService(other).UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress,
            null, _user.Id, ["Project Manager"], concurrencyStamp: stale);
        Assert.True(first.Succeeded, first.Error);
        var second = await new ProgressService(_db).UpdateAsync(_ownTask.Id, 20, TaskStatus.InProgress,
            null, _user.Id, ["Project Manager"], concurrencyStamp: stale);
        Assert.False(second.Succeeded);
        Assert.Contains("tải lại", second.Error);
        Assert.Equal(10, (await other.Tasks.AsNoTracking().SingleAsync(item => item.Id == _ownTask.Id)).PercentComplete);
    }

    [Theory]
    [InlineData("Admin", true, true, true, true)]
    [InlineData("Project Director", true, true, true, true)]
    [InlineData("Project Manager", true, true, true, true)]
    [InlineData("Site Manager", true, false, true, true)]
    [InlineData("Supervising Engineer", false, false, true, true)]
    [InlineData("Worker", false, false, true, true)]
    [InlineData("Materials Staff", false, false, true, false)]
    [InlineData("Client Representative", false, false, false, false)]
    public async Task TrackingRoleMatrix_EnforcesDependenciesCancellationCommentsAndAudit(
        string role, bool dependencies, bool cancel, bool comment, bool audit)
    {
        _project.ClientName = _user.OrganizationName = "Client";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        var access = await TaskAccess.LoadAsync(_db, _user.Id, [role]);
        Assert.NotNull(access);
        Assert.Equal(dependencies, access.CanManageDependencies(_project.Id));
        Assert.Equal(cancel, access.CanCancelOrReopen(_project.Id));
        Assert.Equal(comment, access.CanComment(_ownTask));
        Assert.Equal(audit, access.CanViewAudit);
        _project.IsArchived = true;
        await _db.SaveChangesAsync();
        access = await TaskAccess.LoadAsync(_db, _user.Id, [role]);
        Assert.False(access!.CanManageDependencies(_project.Id));
        Assert.False(access.CanCancelOrReopen(_project.Id));
        Assert.False(access.CanComment(_ownTask));
    }

    [Fact]
    public async Task TaskEdit_SubmitsInspectionAndProgressAtomically()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.RequiresInspection = true;
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 50;
        await _db.SaveChangesAsync();
        var edit = CopyTask(_ownTask, _project.Id);
        edit.RequiresInspection = true;
        edit.Status = TaskStatus.PendingInspection;
        edit.PercentComplete = 100;
        var result = await new TaskService(_db, new WbsService(_db))
            .UpdateAsync(edit, [], _user.Id, false, ["Project Manager"]);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(TaskStatus.PendingInspection, _ownTask.Status);
        Assert.Equal(100, _ownTask.PercentComplete);
        Assert.Equal(InspectionResult.Pending, (await _db.InspectionRequests.SingleAsync()).Result);
        Assert.Single(await _db.TaskProgressHistories.ToListAsync());
    }

    [Fact]
    public async Task Dependency_BlocksProgressUntilPredecessorCompleted()
    {
        _project.ProjectManagerId = _user.Id;
        var predecessor = new TaskEntity { ProjectId = _project.Id, Title = "Predecessor", PlannedEndDate = DateTime.Today.AddDays(10) };
        _db.Tasks.Add(predecessor);
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        Assert.True((await new TaskDependencyService(_db).AddAsync(
            _ownTask.Id, predecessor.Id, _user.Id, ["Project Manager"])).Succeeded);
        var progress = new ProgressService(_db);
        var blocked = await progress.UpdateAsync(_ownTask.Id, 20, TaskStatus.InProgress, null,
            _user.Id, ["Project Manager"], concurrencyStamp: _ownTask.ConcurrencyStamp);
        Assert.False(blocked.Succeeded);
        Assert.Contains("tiền nhiệm", blocked.Error);
        predecessor.Status = TaskStatus.Completed;
        predecessor.PercentComplete = 100;
        await _db.SaveChangesAsync();
        var allowed = await progress.UpdateAsync(_ownTask.Id, 20, TaskStatus.InProgress, null,
            _user.Id, ["Project Manager"], concurrencyStamp: _ownTask.ConcurrencyStamp);
        Assert.True(allowed.Succeeded, allowed.Error);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", false)]
    [InlineData("Supervising Engineer", false)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", false)]
    public async Task RestoreCancelled_RespectsRoleMatrix(string role, bool allowed)
    {
        _project.ClientName = _user.OrganizationName = "Client";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        _ownTask.Status = TaskStatus.Cancelled;
        _ownTask.CancelReason = "Lý do hủy công việc";
        await _db.SaveChangesAsync();
        var result = await new ProgressService(_db).RestoreCancelledAsync(_ownTask.Id, TaskStatus.Pending,
            "Mở lại", null, _ownTask.ConcurrencyStamp, _user.Id, [role]);
        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(allowed ? TaskStatus.Pending : TaskStatus.Cancelled, _ownTask.Status);
    }

    [Theory]
    [InlineData(0, TaskStatus.Pending)]
    [InlineData(35, TaskStatus.InProgress)]
    public async Task RestoreCancelled_PreservesProgressHistoryReasonAndBlockers(int percent, TaskStatus target)
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.Status = TaskStatus.Cancelled;
        _ownTask.PercentComplete = percent;
        _ownTask.ActualStartDate = percent > 0 ? DateTime.Today.AddDays(-2) : null;
        _ownTask.CancelReason = "Lý do hủy ban đầu";
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        var predecessor = new TaskEntity { ProjectId = _project.Id, Title = "Chưa hoàn thành", PlannedEndDate = DateTime.Today.AddDays(10) };
        _db.Tasks.Add(predecessor);
        await _db.SaveChangesAsync();
        _db.TaskDependencies.Add(new TaskDependency { TaskId = _ownTask.Id, PredecessorTaskId = predecessor.Id });
        _db.TaskComments.Add(new TaskComment { TaskId = _ownTask.Id, AuthorId = _user.Id,
            Content = "Vướng mắc", IsBlocker = true, ProgressSnapshot = percent });
        await _db.SaveChangesAsync();
        var oldStamp = _ownTask.ConcurrencyStamp;
        var service = new ProgressService(_db);
        var result = await service.RestoreCancelledAsync(_ownTask.Id, target, "Khôi phục", null,
            oldStamp, _user.Id, ["Project Manager"]);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(target, _ownTask.Status);
        Assert.Equal(percent, _ownTask.PercentComplete);
        Assert.Equal("Lý do hủy ban đầu", _ownTask.CancelReason);
        Assert.NotEqual(oldStamp, _ownTask.ConcurrencyStamp);
        var history = await _db.TaskProgressHistories.SingleAsync();
        Assert.Equal(TaskStatus.Cancelled, history.PreviousStatus);
        Assert.Equal(target, history.NewStatus);
        Assert.Equal(percent, history.PreviousPercentComplete);
        Assert.Equal(percent, history.NewPercentComplete);
        Assert.Equal("Khôi phục", history.Note);
        var audit = await _db.TaskAuditLogs.SingleAsync(item => item.Action == "StatusChanged");
        Assert.Equal("Cancelled", audit.OldValue);
        Assert.Equal(target.ToString(), audit.NewValue);
        Assert.Equal("Khôi phục", audit.Reason);
        Assert.Equal(_user.Id, audit.PerformedById);
        Assert.Single(await _db.TaskDependencies.ToListAsync());
        Assert.True((await _db.TaskComments.SingleAsync()).IsBlocker);
        var update = await service.UpdateAsync(_ownTask.Id, 40, TaskStatus.InProgress, null,
            _user.Id, ["Project Manager"], concurrencyStamp: _ownTask.ConcurrencyStamp);
        Assert.False(update.Succeeded);
        Assert.Contains("tiền nhiệm", update.Error);
    }

    [Fact]
    public async Task RestoreCancelled_RejectsInvalidInputsScopesAndConcurrency()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.Status = TaskStatus.Cancelled;
        _ownTask.PercentComplete = 20;
        await _db.SaveChangesAsync();
        var service = new ProgressService(_db);
        async Task<(bool Succeeded, string? Error)> Restore(TaskStatus? status, string? reason = "Lý do",
            Guid? stamp = null, int? taskId = null, string[]? roles = null) =>
            await service.RestoreCancelledAsync(taskId ?? _ownTask.Id, status, reason, null,
                stamp ?? _ownTask.ConcurrencyStamp, _user.Id, roles ?? ["Project Manager"]);
        Assert.False((await Restore(TaskStatus.InProgress, "  ")).Succeeded);
        Assert.False((await Restore(TaskStatus.Pending)).Succeeded);
        Assert.False((await Restore(TaskStatus.Completed)).Succeeded);
        Assert.False((await Restore(null)).Succeeded);
        Assert.Contains("tải lại", (await Restore(TaskStatus.InProgress, stamp: Guid.NewGuid())).Error);
        Assert.False((await Restore(TaskStatus.InProgress, taskId: _otherTask.Id)).Succeeded);
        _ownTask.PercentComplete = 100;
        await _db.SaveChangesAsync();
        Assert.Contains("100%", (await Restore(TaskStatus.InProgress)).Error);
        _ownTask.PercentComplete = 20;
        _ownTask.Status = TaskStatus.InProgress;
        await _db.SaveChangesAsync();
        Assert.False((await Restore(TaskStatus.InProgress)).Succeeded);
        _ownTask.Status = TaskStatus.Cancelled;
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.False((await Restore(TaskStatus.InProgress)).Succeeded);
        Assert.Empty(await _db.TaskAuditLogs.ToListAsync());
        Assert.Empty(await _db.TaskProgressHistories.ToListAsync());
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Cancelled, false)]
    [InlineData(ProjectStatus.InProgress, true)]
    public async Task RestoreCancelled_RejectsReadOnlyProject(ProjectStatus status, bool archived)
    {
        _project.ProjectManagerId = _user.Id;
        _project.Status = status;
        _project.IsArchived = archived;
        _ownTask.Status = TaskStatus.Cancelled;
        await _db.SaveChangesAsync();
        var result = await new ProgressService(_db).RestoreCancelledAsync(_ownTask.Id, TaskStatus.Pending,
            "Lý do", null, _ownTask.ConcurrencyStamp, _user.Id, ["Project Manager"]);
        Assert.False(result.Succeeded);
        Assert.Contains("Dự án", result.Error);
        Assert.Equal(TaskStatus.Cancelled, _ownTask.Status);
    }

    [Fact]
    public async Task RestoreCancelled_RejectsArchivedWbsAndRequiresDelayReason()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.Status = TaskStatus.Cancelled;
        _ownTask.PlannedStartDate = DateTime.Today.AddDays(-10);
        _ownTask.PlannedEndDate = DateTime.Today.AddDays(-1);
        var phase = new ProjectPhase { ProjectId = _project.Id, Name = "Phase", SequenceOrder = 1 };
        var item = new WorkItem { ProjectId = _project.Id, ProjectPhase = phase, WbsCode = "R-1",
            Name = "Archived", Level = 1, IsArchived = true };
        _db.WorkItems.Add(item);
        _ownTask.WorkItem = item;
        await _db.SaveChangesAsync();
        var service = new ProgressService(_db);
        var result = await service.RestoreCancelledAsync(_ownTask.Id, TaskStatus.Pending, "Lý do", null,
            _ownTask.ConcurrencyStamp, _user.Id, ["Project Manager"]);
        Assert.Contains("WBS", result.Error);
        item.IsArchived = false;
        await _db.SaveChangesAsync();
        result = await service.RestoreCancelledAsync(_ownTask.Id, TaskStatus.Pending, "Lý do", null,
            _ownTask.ConcurrencyStamp, _user.Id, ["Project Manager"]);
        Assert.Contains("lý do chậm", result.Error);
        result = await service.RestoreCancelledAsync(_ownTask.Id, TaskStatus.Pending, "Lý do", "Chậm do thời tiết",
            _ownTask.ConcurrencyStamp, _user.Id, ["Project Manager"]);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Chậm do thời tiết", (await _db.TaskProgressHistories.SingleAsync()).DelayReason);
    }

    [Fact]
    public async Task LegacyCompletedPendingInspection_PassedPreservesCompletion()
    {
        _ownTask.RequiresInspection = true;
        _ownTask.Status = TaskStatus.Completed;
        _ownTask.PercentComplete = 100;
        _ownTask.ActualEndDate = DateTime.UtcNow;
        var inspection = new InspectionRequest { ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id };
        _db.InspectionRequests.Add(inspection);
        await _db.SaveChangesAsync();
        var end = _ownTask.ActualEndDate;
        var result = await new QualityService(_db).RecordInspectionAsync(
            inspection.Id, new InspectionResultViewModel { Result = InspectionResult.Passed }, _user.Id, ["Admin"]);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(TaskStatus.Completed, _ownTask.Status);
        Assert.Equal(end, _ownTask.ActualEndDate);
        Assert.Equal(InspectionResult.Passed, inspection.Result);
        Assert.Single(await _db.InspectionHistories.ToListAsync());
    }

    [Fact]
    public async Task UpdateTask_RejectsProjectTransferAndParentCycle()
    {
        _project.ProjectManagerId = _user.Id;
        var child = new TaskEntity { ProjectId = _project.Id, ParentTaskId = _ownTask.Id, Title = "Con" };
        _db.Tasks.Add(child);
        await _db.SaveChangesAsync();
        var service = new TaskService(_db, new WbsService(_db));

        var transfer = await service.UpdateAsync(
            CopyTask(_ownTask, _otherTask.ProjectId), [], _user.Id, true, ["Project Director"]);
        var cycleModel = CopyTask(_ownTask, _project.Id);
        cycleModel.ParentTaskId = child.Id;
        var cycle = await service.UpdateAsync(
            cycleModel, [], _user.Id, false, ["Project Manager"]);

        Assert.False(transfer.Succeeded);
        Assert.False(cycle.Succeeded);
        Assert.Equal(_project.Id, _ownTask.ProjectId);
        Assert.Null(_ownTask.ParentTaskId);
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, false, true)]
    [InlineData(ProjectStatus.Cancelled, false, true)]
    [InlineData(ProjectStatus.InProgress, true, true)]
    [InlineData(ProjectStatus.InProgress, false, false)]
    public async Task UpdateTask_RejectsReadOnlyProjectOrInactiveUser(
        ProjectStatus status, bool archived, bool active)
    {
        _project.ProjectManagerId = _user.Id;
        _project.Status = status;
        _project.IsArchived = archived;
        _user.IsActive = active;
        await _db.SaveChangesAsync();

        var result = await new TaskService(_db, new WbsService(_db)).UpdateAsync(
            CopyTask(_ownTask, _project.Id), [], _user.Id, true, ["Admin"]);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateTask_AdminWithClientRoleRequiresMembershipAndMatchingOrganization()
    {
        _project.ClientName = "Khách hàng";
        _user.OrganizationName = "Khách hàng khác";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        await _db.SaveChangesAsync();

        var result = await new TaskService(_db, new WbsService(_db)).UpdateAsync(
            CopyTask(_ownTask, _project.Id), [], _user.Id, true, ["Admin", "Client Representative"]);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateTask_PreservesExistingAssignmentTimestamp()
    {
        _project.ProjectManagerId = _user.Id;
        var assignedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id, AssignedAt = assignedAt });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await new TaskService(_db, new WbsService(_db)).UpdateAsync(
            CopyTask(_ownTask, _project.Id), [_user.Id], _user.Id, false, ["Project Manager"]);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(assignedAt, (await _db.TaskAssignments.SingleAsync()).AssignedAt);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UpdateTask_CanRemoveInspectionRequirementOnlyBeforeFirstRequest(
        bool hasRequest, bool allowed)
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.RequiresInspection = true;
        if (hasRequest)
        {
            _db.InspectionRequests.Add(new InspectionRequest
            {
                ProjectId = _project.Id,
                TaskId = _ownTask.Id,
                RequestedById = _user.Id
            });
        }
        await _db.SaveChangesAsync();

        var result = await new TaskService(_db, new WbsService(_db)).UpdateAsync(
            CopyTask(_ownTask, _project.Id), [], _user.Id, false, ["Project Manager"]);

        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(!allowed, _ownTask.RequiresInspection);
    }

    [Theory]
    [InlineData("Admin", true, true, true)]
    [InlineData("Project Director", true, true, true)]
    [InlineData("Project Manager", true, true, true)]
    [InlineData("Site Manager", false, true, true)]
    [InlineData("Supervising Engineer", false, true, true)]
    [InlineData("Worker", false, true, false)]
    [InlineData("Materials Staff", false, false, false)]
    [InlineData("Client Representative", false, false, false)]
    public async Task EightRoleMatrix_EnforcesTaskProgressAndInspection(
        string role, bool canManage, bool canProgress, bool canQuality)
    {
        _project.ClientName = "Khách hàng";
        _user.OrganizationName = " khách hàng ";
        _ownTask.RequiresInspection = true;
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();

        var roles = new[] { role };
        var tasks = new TaskService(_db, new WbsService(_db));
        var visible = await tasks.GetVisibleAsync(_user.Id, roles);
        Assert.Contains(visible, item => item.Id == _ownTask.Id);
        Assert.Equal(role == "Admin", visible.Any(item => item.Id == _otherTask.Id));

        var create = await tasks.CreateAsync(
            new TaskEntity { ProjectId = _project.Id, Title = "Mới", PlannedEndDate = DateTime.UtcNow.AddDays(10) },
            [], _user.Id, role == "Admin", roles);
        Assert.Equal(canManage, create.Succeeded);

        var progress = await new ProgressService(_db).UpdateAsync(
            _ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp);
        Assert.Equal(canProgress, progress.Succeeded);
        var quality = await new QualityService(_db).CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles);
        Assert.Equal(canQuality, quality.Succeeded);
    }

    [Theory]
    [InlineData("Worker")]
    [InlineData("Supervising Engineer")]
    public async Task Progress_AssignmentAndScopeAreBothRequired(string role)
    {
        var roles = new[] { role };
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _otherTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        var service = new ProgressService(_db);

        var visible = await service.GetVisibleTasksAsync(_user.Id, roles);
        Assert.Equal(role != "Worker", visible.Any(item => item.Id == _ownTask.Id));
        Assert.DoesNotContain(visible, item => item.Id == _otherTask.Id);
        Assert.False((await service.UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
        Assert.False((await service.UpdateAsync(_otherTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        Assert.True((await service.UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp)).Succeeded);
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Cancelled, false)]
    [InlineData(ProjectStatus.InProgress, true)]
    public async Task ReadOnlyProjects_AreVisibleButRejectProgressAndInspection(ProjectStatus status, bool archived)
    {
        _project.Status = status;
        _project.IsArchived = archived;
        _ownTask.RequiresInspection = true;
        await _db.SaveChangesAsync();
        var roles = new[] { "Admin" };
        var progress = new ProgressService(_db);
        Assert.Contains(await progress.GetVisibleTasksAsync(_user.Id, roles), item => item.Id == _ownTask.Id);
        Assert.False((await progress.UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
        Assert.False((await new QualityService(_db).CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
    }

    [Fact]
    public async Task MultiRoleClient_RequiresActiveMembershipAndMatchingOrganizationEvenForAdmin()
    {
        _project.ClientName = "CÔNG TY A";
        _user.OrganizationName = " công ty a ";
        var membership = new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true };
        _db.ProjectMembers.Add(membership);
        await _db.SaveChangesAsync();
        var roles = new[] { "Admin", "Client Representative" };
        var access = await TaskAccess.LoadAsync(_db, _user.Id, roles);
        Assert.NotNull(access);
        Assert.True(access.CanEditTask(_project.Id));
        Assert.True(access.CanWriteQuality(_project.Id));
        Assert.False(access.CanView(_otherTask.ProjectId));

        _user.OrganizationName = "khác";
        await _db.SaveChangesAsync();
        Assert.False((await TaskAccess.LoadAsync(_db, _user.Id, roles))!.CanView(_project.Id));
        _user.OrganizationName = "công ty a";
        membership.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.False((await TaskAccess.LoadAsync(_db, _user.Id, roles))!.CanView(_project.Id));
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.Null(await TaskAccess.LoadAsync(_db, _user.Id, roles));
    }

    [Fact]
    public async Task MultiRole_CombinesProgressRightsWithoutRequiringWorkerAssignment()
    {
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 5;
        await _db.SaveChangesAsync();
        var result = await new ProgressService(_db).UpdateAsync(
            _ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, ["Worker", "Project Manager"],
            concurrencyStamp: _ownTask.ConcurrencyStamp);
        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Dashboard_ProjectDirectorSeesOnlyScopedTaskCounts()
    {
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        await _db.SaveChangesAsync();
        var access = await TaskAccess.LoadAsync(_db, _user.Id, ["Project Director"]);
        Assert.NotNull(access);
        var summary = await new DashboardService(_db).GetSummaryAsync(access);
        Assert.Equal(1, summary.ProjectCount);
        Assert.Equal(1, summary.OpenTaskCount);
    }

    [Theory]
    [InlineData(TaskStatus.Pending, 0, true)]
    [InlineData(TaskStatus.Pending, 1, false)]
    [InlineData(TaskStatus.InProgress, 0, false)]
    [InlineData(TaskStatus.InProgress, 99.9, true)]
    [InlineData(TaskStatus.InProgress, 100, false)]
    [InlineData(TaskStatus.Completed, 99, false)]
    [InlineData(TaskStatus.Completed, 100, true)]
    [InlineData(TaskStatus.Assigned, 0, true)]
    [InlineData(TaskStatus.Assigned, 100, false)]
    [InlineData(TaskStatus.PendingInspection, 100, true)]
    [InlineData(TaskStatus.PendingInspection, 99, false)]
    [InlineData(TaskStatus.NeedsRework, 100, false)]
    [InlineData(TaskStatus.NeedsRework, 30, true)]
    [InlineData(TaskStatus.Cancelled, 100, true)]
    [InlineData(TaskStatus.Assigned, -0.01, false)]
    [InlineData(TaskStatus.Cancelled, 101, false)]
    public async Task Lifecycle_ValidatesStatusAndPercent(TaskStatus status, double percent, bool valid)
    {
        if (status == TaskStatus.NeedsRework)
            _db.InspectionRequests.Add(new InspectionRequest
            {
                ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id, Result = InspectionResult.Failed
            });
        await _db.SaveChangesAsync();
        _ownTask.Status = status switch
        {
            TaskStatus.Completed or TaskStatus.Cancelled => TaskStatus.InProgress,
            _ => status
        };
        _ownTask.PercentComplete = (decimal)percent;
        _ownTask.RequiresInspection = status == TaskStatus.PendingInspection;
        var error = await new TaskLifecycle(_db).ValidateChangeAsync(
            _ownTask, status, (decimal)percent, 1, null, "Lý do hủy kiểm thử",
            inspectionRequested: status == TaskStatus.PendingInspection);
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public async Task InvalidStatusPercent_IsRejectedByEditAndProgressWithoutWrites()
    {
        _project.ProjectManagerId = _user.Id;
        await _db.SaveChangesAsync();
        var edit = CopyTask(_ownTask, _project.Id);
        edit.Status = TaskStatus.Pending;
        edit.PercentComplete = 50;
        var roles = new[] { "Project Manager" };
        var update = await new TaskService(_db, new WbsService(_db))
            .UpdateAsync(edit, [], _user.Id, false, roles);
        var progress = await new ProgressService(_db)
            .UpdateAsync(_ownTask.Id, 20, TaskStatus.Completed, null, _user.Id, roles);
        Assert.False(update.Succeeded);
        Assert.False(progress.Succeeded);
        Assert.Equal(TaskStatus.Pending, _ownTask.Status);
        Assert.Equal(0, _ownTask.PercentComplete);
        Assert.Empty(await _db.TaskProgressHistories.ToListAsync());
    }

    [Fact]
    public async Task Lifecycle_TracksChangesAcrossEditProgressAndInspection()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.RequiresInspection = true;
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        var roles = new[] { "Project Manager" };
        var edit = CopyTask(_ownTask, _project.Id);
        edit.RequiresInspection = true;
        edit.Status = TaskStatus.InProgress;
        edit.PercentComplete = 20;
        Assert.True((await new TaskService(_db, new WbsService(_db))
            .UpdateAsync(edit, [_user.Id], _user.Id, false, roles)).Succeeded);
        Assert.NotNull(_ownTask.ActualStartDate);
        var progress = new ProgressService(_db);
        Assert.True((await progress.UpdateAsync(_ownTask.Id, 40, TaskStatus.InProgress, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp)).Succeeded);
        var quality = new QualityService(_db);
        Assert.True((await quality.CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
        var inspection = await _db.InspectionRequests.SingleAsync();
        Assert.False((await progress.UpdateAsync(_ownTask.Id, 100, TaskStatus.Completed, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp)).Succeeded);
        Assert.True((await quality.RecordInspectionAsync(inspection.Id,
            new InspectionResultViewModel { Result = InspectionResult.Passed }, _user.Id, roles)).Succeeded);
        Assert.Equal(100, _ownTask.PercentComplete);
        Assert.NotNull(_ownTask.ActualEndDate);
        Assert.False((await quality.CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
        Assert.True((await progress.UpdateAsync(_ownTask.Id, 80, TaskStatus.InProgress, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp, reason: "Điều chỉnh sau hoàn thành", reopen: true)).Succeeded);
        Assert.Null(_ownTask.ActualEndDate);
        Assert.Equal(5, await _db.TaskProgressHistories.CountAsync());

        Assert.True((await progress.UpdateAsync(_ownTask.Id, 100, TaskStatus.Completed, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp)).Succeeded);
        Assert.True((await progress.UpdateAsync(_ownTask.Id, 80, TaskStatus.InProgress, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp, reason: "Điều chỉnh sau hoàn thành", reopen: true)).Succeeded);
        Assert.True((await quality.CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
        Assert.False((await progress.UpdateAsync(_ownTask.Id, 100, TaskStatus.Completed, null, _user.Id, roles,
            concurrencyStamp: _ownTask.ConcurrencyStamp)).Succeeded);
    }

    [Fact]
    public async Task FailedInspectionAt100_RequiresNewPercentAndRecordsHistory()
    {
        _ownTask.RequiresInspection = true;
        _ownTask.Status = TaskStatus.Completed;
        _ownTask.PercentComplete = 100;
        _ownTask.ActualEndDate = DateTime.UtcNow;
        var inspection = new InspectionRequest { ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id };
        _db.InspectionRequests.Add(inspection);
        await _db.SaveChangesAsync();
        var service = new QualityService(_db);
        var model = new InspectionResultViewModel { Result = InspectionResult.Failed, CorrectiveAction = "Sửa" };
        Assert.False((await service.RecordInspectionAsync(inspection.Id, model, _user.Id, ["Admin"])).Succeeded);
        model.NewPercentComplete = 100;
        Assert.False((await service.RecordInspectionAsync(inspection.Id, model, _user.Id, ["Admin"])).Succeeded);
        model.NewPercentComplete = 80;
        Assert.True((await service.RecordInspectionAsync(inspection.Id, model, _user.Id, ["Admin"])).Succeeded);
        Assert.Equal(TaskStatus.NeedsRework, _ownTask.Status);
        Assert.Equal(80, _ownTask.PercentComplete);
        Assert.Null(_ownTask.ActualEndDate);
        var history = await _db.TaskProgressHistories.SingleAsync();
        Assert.Equal(100, history.PreviousPercentComplete);
        Assert.Equal(80, history.NewPercentComplete);
        Assert.False((await service.RecordInspectionAsync(inspection.Id, model, _user.Id, ["Admin"])).Succeeded);
    }

    [Theory]
    [InlineData(InspectionResult.Passed, true)]
    [InlineData(InspectionResult.Failed, true)]
    [InlineData(InspectionResult.Failed, false)]
    public async Task OlderPendingInspection_CanBeRecordedWithoutChangingTask(InspectionResult result, bool requiresInspection)
    {
        _ownTask.RequiresInspection = requiresInspection;
        _ownTask.Status = TaskStatus.Completed;
        _ownTask.PercentComplete = 100;
        var end = DateTime.UtcNow;
        _ownTask.ActualEndDate = end;
        var old = new InspectionRequest
        {
            ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id,
            RequestedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var latest = new InspectionRequest
        {
            ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id,
            Result = InspectionResult.Passed
        };
        _db.InspectionRequests.AddRange(old, latest);
        await _db.SaveChangesAsync();
        var recorded = await new QualityService(_db).RecordInspectionAsync(old.Id,
            new InspectionResultViewModel { Result = result, CorrectiveAction = "Sửa" }, _user.Id, ["Admin"]);
        Assert.True(recorded.Succeeded, recorded.Error);
        Assert.Equal(result, old.Result);
        Assert.Equal(TaskStatus.Completed, _ownTask.Status);
        Assert.Equal(100, _ownTask.PercentComplete);
        Assert.Equal(end, _ownTask.ActualEndDate);
        Assert.Empty(await _db.TaskProgressHistories.ToListAsync());
        Assert.Single(await _db.InspectionHistories.ToListAsync());
    }

    [Fact]
    public async Task EditInvalidModel_ChecksActualProjectAndReadOnlyBeforeReturningForm()
    {
        var membership = new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = false };
        _db.ProjectMembers.Add(membership);
        await _db.SaveChangesAsync();
        var controller = TasksController("Project Manager");
        controller.ModelState.AddModelError("Title", "Thiếu tên");
        var model = new TaskEditViewModel { Id = _ownTask.Id, ProjectId = _project.Id };
        Assert.IsType<NotFoundResult>(await controller.Edit(_ownTask.Id, model, default));
        membership.IsActive = true;
        _project.Status = ProjectStatus.Completed;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.Edit(_ownTask.Id, model, default));
        _project.Status = ProjectStatus.InProgress;
        await _db.SaveChangesAsync();
        Assert.IsType<ViewResult>(await controller.Edit(_ownTask.Id, model, default));
        model.ProjectId = _otherTask.ProjectId;
        Assert.IsType<BadRequestResult>(await controller.Edit(_ownTask.Id, model, default));
    }

    [Fact]
    public async Task ProgressInvalidModel_ChecksRealTaskScopeAssignmentAndActiveUser()
    {
        var member = new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true };
        _db.ProjectMembers.Add(member);
        await _db.SaveChangesAsync();
        var controller = new ProgressController(new ProgressService(_db), UserManager(), _db)
        {
            ControllerContext = Context("Worker")
        };
        controller.ModelState.AddModelError("PercentComplete", "Lỗi");
        var model = new ProgressUpdateViewModel { TaskId = _ownTask.Id };
        Assert.IsType<NotFoundResult>(await controller.Update(model, default));
        member.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.Update(model, default));
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.Update(model, default));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", true)]
    [InlineData("Supervising Engineer", true)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", false)]
    public async Task RecordInspection_EnforcesEightRoleMatrix(string role, bool allowed)
    {
        _user.OrganizationName = _project.ClientName = "Client";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _ownTask.RequiresInspection = true;
        _ownTask.Status = TaskStatus.PendingInspection;
        _ownTask.PercentComplete = 100;
        var inspection = new InspectionRequest { ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id };
        _db.InspectionRequests.Add(inspection);
        await _db.SaveChangesAsync();
        var result = await new QualityService(_db).RecordInspectionAsync(
            inspection.Id, new InspectionResultViewModel { Result = InspectionResult.Passed }, _user.Id, [role]);
        Assert.Equal(allowed, result.Succeeded);
        Assert.Equal(allowed ? InspectionResult.Passed : InspectionResult.Pending, inspection.Result);
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Cancelled, false)]
    [InlineData(ProjectStatus.InProgress, true)]
    public async Task RecordInspection_RejectsReadOnlyProject(ProjectStatus status, bool archived)
    {
        _project.Status = status;
        _project.IsArchived = archived;
        _ownTask.RequiresInspection = true;
        var inspection = new InspectionRequest { ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id };
        _db.InspectionRequests.Add(inspection);
        await _db.SaveChangesAsync();
        var result = await new QualityService(_db).RecordInspectionAsync(
            inspection.Id, new InspectionResultViewModel { Result = InspectionResult.Passed }, _user.Id, ["Admin"]);
        Assert.False(result.Succeeded);
        Assert.Equal(InspectionResult.Pending, inspection.Result);
        Assert.Empty(await _db.TaskProgressHistories.ToListAsync());
    }

    [Fact]
    public async Task InspectionWrites_RejectOutsideScopeInactiveUserAndMismatchedMultiRole()
    {
        _ownTask.RequiresInspection = true;
        var inspection = new InspectionRequest { ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id };
        _db.InspectionRequests.Add(inspection);
        await _db.SaveChangesAsync();
        var quality = new QualityService(_db);
        var result = new InspectionResultViewModel { Result = InspectionResult.Passed };
        Assert.False((await quality.RecordInspectionAsync(inspection.Id, result, _user.Id, ["Project Director"])).Succeeded);
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _project.ClientName = "A";
        _user.OrganizationName = "B";
        await _db.SaveChangesAsync();
        Assert.False((await quality.RecordInspectionAsync(inspection.Id, result, _user.Id, ["Admin", "Client Representative"])).Succeeded);
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.False((await quality.RecordInspectionAsync(inspection.Id, result, _user.Id, ["Admin"])).Succeeded);
        Assert.Equal(InspectionResult.Pending, inspection.Result);
        Assert.Empty(await _db.TaskProgressHistories.ToListAsync());
    }

    [Fact]
    public async Task CreateForm_OptionsAreRestrictedToSelectedWritableProject()
    {
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        var outsider = new ApplicationUser { Id = "outside-user", UserName = "outside-user" };
        _db.Users.Add(outsider);
        await _db.SaveChangesAsync();
        var controller = TasksController("Project Manager");
        Assert.IsType<ViewResult>(await controller.Create(_project.Id, default));
        var projects = Assert.IsAssignableFrom<IEnumerable<Project>>(controller.ViewData["Projects"]);
        var tasks = Assert.IsAssignableFrom<IEnumerable<TaskEntity>>(controller.ViewData["Tasks"]);
        var users = Assert.IsAssignableFrom<IEnumerable<ApplicationUser>>(controller.ViewData["Users"]);
        Assert.Equal([_project.Id], projects.Select(project => project.Id));
        Assert.Equal([_ownTask.Id], tasks.Select(task => task.Id));
        Assert.Equal([_user.Id], users.Select(user => user.Id));
        _project.Status = ProjectStatus.Completed;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.Create(_project.Id, default));
    }

    [Fact]
    public async Task QualityIndex_ClientSeesOnlyInspections_MultiRoleGetsUnionWithinScope()
    {
        _project.ClientName = "Client A";
        _user.OrganizationName = " client a ";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        _db.Incidents.Add(new Incident { ProjectId = _project.Id, Title = "Incident", Description = "D", ReportedById = _user.Id });
        _db.RfiRequests.Add(new RfiRequest { ProjectId = _project.Id, Subject = "RFI", Question = "Q", CreatedById = _user.Id });
        _db.InspectionRequests.Add(new InspectionRequest { ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id });
        _db.InspectionRequests.Add(new InspectionRequest { ProjectId = _otherTask.ProjectId, TaskId = _otherTask.Id, RequestedById = _user.Id });
        await _db.SaveChangesAsync();

        var client = QualityController("Client Representative");
        var clientModel = Assert.IsType<QualityIndexViewModel>(Assert.IsType<ViewResult>(await client.Index(default)).Model);
        Assert.False(clientModel.CanManageQuality);
        Assert.Empty(clientModel.Incidents);
        Assert.Empty(clientModel.Rfis);
        Assert.Single(clientModel.Inspections);
        Assert.Empty(clientModel.WritableProjectIds);
        var union = QualityController("Client Representative", "Project Director");
        var unionModel = Assert.IsType<QualityIndexViewModel>(Assert.IsType<ViewResult>(await union.Index(default)).Model);
        Assert.True(unionModel.CanManageQuality);
        Assert.Single(unionModel.Incidents);
        Assert.Single(unionModel.Rfis);
        Assert.Single(unionModel.Inspections);
        Assert.Contains(_project.Id, unionModel.WritableProjectIds);
        _user.OrganizationName = "không khớp";
        await _db.SaveChangesAsync();
        var deniedModel = Assert.IsType<QualityIndexViewModel>(Assert.IsType<ViewResult>(await union.Index(default)).Model);
        Assert.Empty(deniedModel.Inspections);
        Assert.Empty(deniedModel.Incidents);
    }

    [Fact]
    public async Task Controllers_UseCurrentDatabaseRolesRatherThanStaleAdminClaim()
    {
        _project.ClientName = _user.OrganizationName = "Client";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        await _db.SaveChangesAsync();
        var controller = TasksController("Client Representative");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, _user.Id), new Claim(ClaimTypes.Role, "Admin")], "test"));
        Assert.IsType<ForbidResult>(await controller.Create(_project.Id, default));
    }

    [Fact]
    public async Task PassedLatest_AllowsTaskEditCompletion_AndNewFailedRevokesIt()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.RequiresInspection = true;
        _ownTask.Status = TaskStatus.InProgress;
        _ownTask.PercentComplete = 25;
        _db.InspectionRequests.Add(new InspectionRequest
        {
            ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id,
            Result = InspectionResult.Passed, RequestedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await _db.SaveChangesAsync();
        var roles = new[] { "Project Manager" };
        var edit = CopyTask(_ownTask, _project.Id);
        edit.RequiresInspection = true;
        edit.Status = TaskStatus.Completed;
        edit.PercentComplete = 100;
        var service = new TaskService(_db, new WbsService(_db));
        Assert.True((await service.UpdateAsync(edit, [], _user.Id, false, roles)).Succeeded);
        _db.InspectionRequests.Add(new InspectionRequest
        {
            ProjectId = _project.Id, TaskId = _ownTask.Id, RequestedById = _user.Id,
            Result = InspectionResult.Failed, RequestedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        Assert.False((await service.UpdateAsync(edit, [], _user.Id, false, roles)).Succeeded);
    }

    [Fact]
    public async Task Completion_OpenTasksAreScopedBeforeLimitAndRespectClientAndInactive()
    {
        _project.ProjectManagerId = _user.Id;
        _project.ClientName = "Client A";
        _user.OrganizationName = "client a";
        _db.ProjectMembers.Add(new ProjectMember { ProjectId = _project.Id, UserId = _user.Id, IsActive = true });
        for (var i = 0; i < 101; i++)
            _db.Tasks.Add(new TaskEntity { ProjectId = _otherTask.ProjectId, Title = $"Outside {i}", PlannedEndDate = DateTime.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();
        var service = new CompletionService(_db);
        var scoped = await service.GetOpenTasksAsync(_user.Id, ["Project Director"]);
        Assert.Contains(scoped!, task => task.Id == _ownTask.Id);
        Assert.DoesNotContain(scoped!, task => task.ProjectId == _otherTask.ProjectId);
        var client = await service.GetOpenTasksAsync(_user.Id, ["Admin", "Client Representative"]);
        Assert.Single(client!);
        _user.OrganizationName = "Other";
        await _db.SaveChangesAsync();
        Assert.Empty((await service.GetOpenTasksAsync(_user.Id, ["Admin", "Client Representative"]))!);
        _user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.Null(await service.GetOpenTasksAsync(_user.Id, ["Admin"]));
    }

    [Fact]
    public async Task TaskIndex_FiltersWithinScopeAndRejectsInvalidEnum()
    {
        _project.ProjectManagerId = _user.Id;
        _ownTask.Priority = TaskPriority.High;
        _ownTask.Status = TaskStatus.Assigned;
        _ownTask.Description = "Mô tả cần tìm";
        await _db.SaveChangesAsync();
        var controller = TasksController("Project Manager");
        var result = await controller.Index(_project.Id, TaskStatus.Assigned, TaskPriority.High, "CẦN TÌM", default);
        var model = Assert.IsType<TaskListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Single(model.Tasks);
        Assert.Equal(_ownTask.Id, model.Tasks.Single().Id);
        Assert.DoesNotContain(model.Projects, project => project.Id == _otherTask.ProjectId);
        result = await controller.Index(_otherTask.ProjectId, null, null, null, default);
        Assert.Empty(Assert.IsType<TaskListViewModel>(Assert.IsType<ViewResult>(result).Model).Tasks);
        Assert.IsType<BadRequestObjectResult>(await controller.Index(null, (TaskStatus)999, null, null, default));
    }

    [Fact]
    public void TaskForm_EnforcesEntityTextLimits()
    {
        var model = new TaskEditViewModel { Title = new string('a', 201), Description = new string('b', 2001) };
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, new System.ComponentModel.DataAnnotations.ValidationContext(model), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Title)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.Description)));
    }

    private UserManager<ApplicationUser> UserManager()
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(_db);
        return new UserManager<ApplicationUser>(
            store, Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private ControllerContext Context(params string[] roles)
    {
        _db.UserRoles.RemoveRange(_db.UserRoles.Where(item => item.UserId == _user.Id));
        foreach (var roleName in roles)
        {
            var role = _db.Roles.SingleOrDefault(item => item.Name == roleName);
            if (role is null)
            {
                role = new IdentityRole { Name = roleName, NormalizedName = roleName.ToUpperInvariant() };
                _db.Roles.Add(role);
            }
            _db.UserRoles.Add(new IdentityUserRole<string> { UserId = _user.Id, RoleId = role.Id });
        }
        _db.SaveChanges();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, _user.Id) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
    }

    private TasksController TasksController(params string[] roles) =>
        new(new TaskService(_db, new WbsService(_db)), new WbsService(_db), UserManager(), _db)
        { ControllerContext = Context(roles) };

    private QualityController QualityController(params string[] roles) =>
        new(_db, new QualityService(_db), UserManager())
        { ControllerContext = Context(roles) };

    private static TaskEntity CopyTask(TaskEntity task, int projectId) => new()
    {
        Id = task.Id,
        ProjectId = projectId,
        Title = task.Title,
        PlannedStartDate = task.PlannedStartDate,
        PlannedEndDate = task.PlannedEndDate,
        Status = task.Status,
        PercentComplete = task.PercentComplete,
        ConcurrencyStamp = task.ConcurrencyStamp
    };

    private int? TaskId(string selection) => selection switch
    {
        "none" => null,
        "same" => _ownTask.Id,
        "other" => _otherTask.Id,
        "missing" => int.MaxValue,
        _ => throw new ArgumentOutOfRangeException(nameof(selection))
    };
}
