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
            _ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles);
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
        Assert.Contains(visible, item => item.Id == _ownTask.Id);
        Assert.DoesNotContain(visible, item => item.Id == _otherTask.Id);
        Assert.False((await service.UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
        Assert.False((await service.UpdateAsync(_otherTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
        _db.TaskAssignments.Add(new TaskAssignment { TaskId = _ownTask.Id, UserId = _user.Id });
        await _db.SaveChangesAsync();
        Assert.True((await service.UpdateAsync(_ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
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
        await _db.SaveChangesAsync();
        var result = await new ProgressService(_db).UpdateAsync(
            _ownTask.Id, 10, TaskStatus.InProgress, null, _user.Id, ["Worker", "Project Manager"]);
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
    [InlineData(TaskStatus.InProgress, 0, true)]
    [InlineData(TaskStatus.InProgress, 99.9, true)]
    [InlineData(TaskStatus.InProgress, 100, false)]
    [InlineData(TaskStatus.Completed, 99, false)]
    [InlineData(TaskStatus.Completed, 100, true)]
    [InlineData(TaskStatus.Assigned, 100, true)]
    [InlineData(TaskStatus.PendingInspection, 100, true)]
    [InlineData(TaskStatus.NeedsRework, 100, true)]
    [InlineData(TaskStatus.Cancelled, 100, true)]
    [InlineData(TaskStatus.Assigned, -0.01, false)]
    [InlineData(TaskStatus.Cancelled, 101, false)]
    public async Task Lifecycle_ValidatesStatusAndPercent(TaskStatus status, double percent, bool valid)
    {
        var error = await new TaskLifecycle(_db).ValidateAsync(_ownTask.Id, false, status, (decimal)percent);
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
        await _db.SaveChangesAsync();
        var roles = new[] { "Project Manager" };
        var edit = CopyTask(_ownTask, _project.Id);
        edit.RequiresInspection = true;
        edit.Status = TaskStatus.InProgress;
        edit.PercentComplete = 20;
        Assert.True((await new TaskService(_db, new WbsService(_db))
            .UpdateAsync(edit, [], _user.Id, false, roles)).Succeeded);
        Assert.NotNull(_ownTask.ActualStartDate);
        var progress = new ProgressService(_db);
        Assert.True((await progress.UpdateAsync(_ownTask.Id, 40, TaskStatus.InProgress, null, _user.Id, roles)).Succeeded);
        var quality = new QualityService(_db);
        Assert.True((await quality.CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
        var inspection = await _db.InspectionRequests.SingleAsync();
        Assert.False((await progress.UpdateAsync(_ownTask.Id, 100, TaskStatus.Completed, null, _user.Id, roles)).Succeeded);
        Assert.True((await quality.RecordInspectionAsync(inspection.Id,
            new InspectionResultViewModel { Result = InspectionResult.Passed }, _user.Id, roles)).Succeeded);
        Assert.Equal(100, _ownTask.PercentComplete);
        Assert.NotNull(_ownTask.ActualEndDate);
        Assert.False((await quality.CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
        Assert.True((await progress.UpdateAsync(_ownTask.Id, 80, TaskStatus.Assigned, null, _user.Id, roles)).Succeeded);
        Assert.Null(_ownTask.ActualEndDate);
        Assert.Equal(5, await _db.TaskProgressHistories.CountAsync());

        Assert.True((await progress.UpdateAsync(_ownTask.Id, 100, TaskStatus.Completed, null, _user.Id, roles)).Succeeded);
        Assert.True((await progress.UpdateAsync(_ownTask.Id, 80, TaskStatus.Assigned, null, _user.Id, roles)).Succeeded);
        Assert.True((await quality.CreateInspectionAsync(
            new InspectionCreateViewModel { TaskId = _ownTask.Id }, _user.Id, roles)).Succeeded);
        Assert.False((await progress.UpdateAsync(_ownTask.Id, 100, TaskStatus.Completed, null, _user.Id, roles)).Succeeded);
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
        Assert.IsType<ForbidResult>(await controller.Update(model, default));
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
        PercentComplete = task.PercentComplete
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
