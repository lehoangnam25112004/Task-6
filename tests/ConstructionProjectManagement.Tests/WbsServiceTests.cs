using System.Security.Claims;
using ConstructionProjectManagement.Controllers;
using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace ConstructionProjectManagement.Tests;

public sealed class WbsServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ApplicationDbContext _db = null!;
    private WbsService _service = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        await _db.Database.MigrateAsync();
        _service = new WbsService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task PermissionMatrix_EnforcesProjectScopeAndAllEightRoles()
    {
        var cases = new (string Role, bool CanManage, bool CanUpdateStatus, bool CanArchive, bool CanLink)[]
        {
            ("Admin", false, false, true, false),
            ("Project Director", true, true, true, false),
            ("Project Manager", true, true, true, true),
            ("Site Manager", false, true, false, true),
            ("Supervising Engineer", false, false, false, true),
            ("Materials Staff", false, false, false, true),
            ("Worker", false, false, false, false),
            ("Client Representative", false, false, false, false)
        };

        foreach (var (role, canManage, canUpdateStatus, canArchive, canLink) in cases)
        {
            var user = NewUser($"wbs-{role.Replace(" ", "-")}", "Khách hàng");
            _db.Users.Add(user);
            var project = await AddProjectAsync($"WBS-{_db.Projects.Count() + 1}", "Khách hàng");
            if (role != RoleNames.Admin)
            {
                _db.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = project.Id,
                    UserId = user.Id,
                    IsActive = true
                });
            }

            var phase = await AddPhaseAsync(project.Id, "Giai đoạn 1");
            var item = await AddItemAsync(project.Id, phase.Id, "1", "Hạng mục 1");
            await _db.SaveChangesAsync();

            var roles = new[] { role };
            Assert.True((await _service.GetProjectTreeAsync(project.Id, user.Id, roles)).Succeeded);

            var newPhase = await _service.CreatePhaseAsync(
                new ProjectPhase { ProjectId = project.Id, Name = "Giai đoạn mới" },
                user.Id,
                roles);
            Assert.Equal(canManage, newPhase.Succeeded);
            if (!canManage)
            {
                Assert.Equal(WbsFailureKind.Forbidden, newPhase.Failure);
            }

            var status = await _service.UpdateWorkItemStatusAsync(
                item.Id,
                WbsItemStatus.InProgress,
                user.Id,
                roles);
            Assert.Equal(canUpdateStatus, status.Succeeded);

            var link = await _service.CanLinkWorkItemToTaskAsync(
                project.Id,
                item.Id,
                null,
                user.Id,
                roles);
            Assert.Equal(canLink, link.Succeeded);

            var archivedView = await _service.GetProjectTreeAsync(
                project.Id,
                user.Id,
                roles,
                includeArchived: true);
            Assert.Equal(canArchive, archivedView.Succeeded);

            var archive = await _service.ArchiveWorkItemAsync(item.Id, user.Id, roles);
            Assert.Equal(canArchive, archive.Succeeded);
        }
    }

    [Fact]
    public async Task ProjectAccess_RejectsInactiveUserAndInactiveMembership()
    {
        var user = NewUser("wbs-inactive");
        var project = await AddProjectAsync("WBS-INACTIVE");
        _db.Users.Add(user);
        _db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            IsActive = false
        });
        await _db.SaveChangesAsync();

        var inactiveMembership = await _service.GetProjectTreeAsync(
            project.Id,
            user.Id,
            ["Project Director"]);
        Assert.Equal(WbsFailureKind.NotFound, inactiveMembership.Failure);

        var member = await _db.ProjectMembers.SingleAsync();
        member.IsActive = true;
        user.IsActive = false;
        await _db.SaveChangesAsync();

        var inactiveUser = await _service.GetProjectTreeAsync(
            project.Id,
            user.Id,
            ["Project Director"]);
        Assert.Equal(WbsFailureKind.Forbidden, inactiveUser.Failure);

        user.IsActive = true;
        var otherProject = await AddProjectAsync("WBS-OTHER-SCOPE");
        await _db.SaveChangesAsync();
        var crossProjectRead = await _service.GetProjectTreeAsync(
            otherProject.Id,
            user.Id,
            ["Project Director"]);
        var crossProjectWrite = await _service.CreatePhaseAsync(
            new ProjectPhase { ProjectId = otherProject.Id, Name = "Out of scope" },
            user.Id,
            ["Project Director"]);
        var unknownRole = await _service.GetProjectTreeAsync(
            project.Id,
            user.Id,
            ["Unrecognized role"]);

        Assert.Equal(WbsFailureKind.NotFound, crossProjectRead.Failure);
        Assert.Equal(WbsFailureKind.NotFound, crossProjectWrite.Failure);
        Assert.Equal(WbsFailureKind.Forbidden, unknownRole.Failure);
    }

    [Fact]
    public async Task ClientRepresentative_MustMatchProjectOrganization()
    {
        var user = NewUser("wbs-client", "Different Organization");
        _db.Users.Add(user);
        var project = await AddProjectAsync("WBS-CLIENT", " Client Organization ");
        _db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var denied = await _service.GetProjectTreeAsync(
            project.Id,
            user.Id,
            ["Client Representative"]);
        Assert.Equal(WbsFailureKind.NotFound, denied.Failure);

        user.OrganizationName = "client organization";
        await _db.SaveChangesAsync();
        var allowed = await _service.GetProjectTreeAsync(
            project.Id,
            user.Id,
            ["Client Representative"]);
        Assert.True(allowed.Succeeded);
    }

    [Fact]
    public async Task ProjectList_SearchesProjectAndNestedActiveItemsInTreeOrder()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        project.ProjectCode = "PROJECT-SEARCH";
        project.Name = "Công trình tìm kiếm";
        phase.SequenceOrder = 2;
        var earlierPhase = await AddPhaseAsync(project.Id, "Giai đoạn trước");
        earlierPhase.SequenceOrder = 1;
        var laterRoot = await AddItemAsync(project.Id, phase.Id, "WBS-LATER", "Mục sau", sequence: 2);
        var firstRoot = await AddItemAsync(project.Id, phase.Id, "WBS-FIRST", "Mục trước", sequence: 1);
        var child = await AddItemAsync(project.Id, phase.Id, "WBS-FIRST.01", "Móng công trình", firstRoot.Id);
        var hidden = await AddItemAsync(project.Id, phase.Id, "WBS-HIDDEN", "Mục lưu trữ");
        await _db.SaveChangesAsync();
        Assert.True((await _service.ArchiveWorkItemAsync(hidden.Id, user.Id, ["Project Manager"])).Succeeded);

        foreach (var term in new[] { "project-search", "tìm kiếm", "wbs-first.01", "móng công trình" })
        {
            var result = await _service.GetVisibleProjectTreesAsync(user.Id, ["Project Manager"], term);
            Assert.True(result.Succeeded, result.Error);
            var tree = Assert.Single(result.Value!);
            Assert.Equal(new[] { earlierPhase.Id, phase.Id }, tree.Phases.Select(item => item.Phase.Id));
            Assert.Equal(new[] { firstRoot.Id, laterRoot.Id }, tree.Phases[1].RootItems.Select(item => item.Item.Id));
            Assert.Equal(child.Id, Assert.Single(tree.Phases[1].RootItems[0].Children).Item.Id);
        }

        var hiddenSearch = await _service.GetVisibleProjectTreesAsync(user.Id, ["Project Manager"], "WBS-HIDDEN");
        Assert.True(hiddenSearch.Succeeded);
        Assert.Empty(hiddenSearch.Value!);
    }

    [Fact]
    public async Task ProjectList_UsesWbsRolesMembershipAndClientOrganization()
    {
        var user = NewUser("wbs-list-user", "Khách hàng A");
        _db.Users.Add(user);
        var allowed = await AddProjectAsync("LIST-ALLOWED", "Khách hàng A");
        var inactive = await AddProjectAsync("LIST-INACTIVE", "Khách hàng A");
        var mismatch = await AddProjectAsync("LIST-MISMATCH", "Khách hàng B");
        var archived = await AddProjectAsync("LIST-ARCHIVED", "Khách hàng A");
        archived.IsArchived = true;
        _db.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = allowed.Id, UserId = user.Id, IsActive = true },
            new ProjectMember { ProjectId = inactive.Id, UserId = user.Id, IsActive = false },
            new ProjectMember { ProjectId = mismatch.Id, UserId = user.Id, IsActive = true },
            new ProjectMember { ProjectId = archived.Id, UserId = user.Id, IsActive = true });
        await _db.SaveChangesAsync();

        foreach (var role in RoleNames.All)
        {
            var result = await _service.GetVisibleProjectTreesAsync(user.Id, [role], null);
            Assert.True(result.Succeeded, result.Error);
            var ids = result.Value!.Select(tree => tree.Project.Id).ToArray();
            Assert.Contains(allowed.Id, ids);
            Assert.DoesNotContain(archived.Id, ids);
            Assert.Equal(role == RoleNames.Admin, ids.Contains(inactive.Id));
            Assert.Equal(role != "Client Representative", ids.Contains(mismatch.Id));
        }

        var unknown = await _service.GetVisibleProjectTreesAsync(user.Id, ["Unknown"], null);
        Assert.Equal(WbsFailureKind.Forbidden, unknown.Failure);
        user.IsActive = false;
        await _db.SaveChangesAsync();
        var inactiveUser = await _service.GetVisibleProjectTreesAsync(user.Id, [RoleNames.Admin], null);
        Assert.Equal(WbsFailureKind.Forbidden, inactiveUser.Failure);
    }

    [Fact]
    public async Task AdminRole_DeniesBusinessEditsEvenWhenAnotherRoleIsPresent()
    {
        var user = NewUser("wbs-admin-multi-role");
        _db.Users.Add(user);
        var project = await AddProjectAsync("WBS-ADMIN-MULTI");
        var phase = await AddPhaseAsync(project.Id, "Phase");
        var item = await AddItemAsync(project.Id, phase.Id, "1", "Item");

        var roles = new[] { RoleNames.Admin, "Project Manager" };
        var create = await _service.CreatePhaseAsync(
            new ProjectPhase { ProjectId = project.Id, Name = "Not allowed" },
            user.Id,
            roles);
        var status = await _service.UpdateWorkItemStatusAsync(
            item.Id,
            WbsItemStatus.Completed,
            user.Id,
            roles);
        var archive = await _service.ArchiveWorkItemAsync(item.Id, user.Id, roles);

        Assert.Equal(WbsFailureKind.Forbidden, create.Failure);
        Assert.Equal(WbsFailureKind.Forbidden, status.Failure);
        Assert.True(archive.Succeeded);
    }

    [Fact]
    public async Task InvalidPhasePost_ChecksActualPhaseScopeUserAndProjectState()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var other = await AddProjectAsync("WBS-PHASE-OTHER");
        var otherPhase = await AddPhaseAsync(other.Id, "Other phase");
        var controller = CreateController(user, "Project Manager");
        controller.ModelState.AddModelError("Name", "Invalid");
        var model = new ProjectPhaseViewModel { Id = phase.Id, ProjectId = project.Id };

        Assert.IsType<ViewResult>(await controller.EditPhase(phase.Id, model, default));
        Assert.IsType<NotFoundResult>(await controller.EditPhase(
            otherPhase.Id, new ProjectPhaseViewModel { Id = otherPhase.Id, ProjectId = project.Id }, default));
        Assert.IsType<BadRequestResult>(await controller.EditPhase(
            phase.Id, new ProjectPhaseViewModel { Id = phase.Id, ProjectId = other.Id }, default));

        var member = await _db.ProjectMembers.SingleAsync(m => m.ProjectId == project.Id);
        member.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.EditPhase(phase.Id, model, default));
        member.IsActive = true;
        user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.EditPhase(phase.Id, model, default));
        user.IsActive = true;
        project.Status = ProjectStatus.Completed;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.EditPhase(phase.Id, model, default));
    }

    [Fact]
    public async Task InvalidItemPost_UsesActualItemInsteadOfSubmittedProject()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var item = await AddItemAsync(project.Id, phase.Id, "WBS-OWN", "Own item");
        var other = await AddProjectAsync("WBS-ITEM-OTHER");
        var otherPhase = await AddPhaseAsync(other.Id, "Other phase");
        var otherItem = await AddItemAsync(other.Id, otherPhase.Id, "WBS-OTHER", "Other item");
        var controller = CreateController(user, "Project Manager");
        controller.ModelState.AddModelError("Name", "Invalid");
        var model = new WorkItemViewModel { Id = item.Id, ProjectId = project.Id };

        Assert.IsType<ViewResult>(await controller.EditItem(item.Id, model, default));
        Assert.IsType<NotFoundResult>(await controller.EditItem(
            otherItem.Id, new WorkItemViewModel { Id = otherItem.Id, ProjectId = project.Id }, default));
        Assert.IsType<NotFoundResult>(await controller.EditItem(
            int.MaxValue, new WorkItemViewModel { Id = int.MaxValue, ProjectId = project.Id }, default));
        Assert.IsType<BadRequestResult>(await controller.EditItem(
            item.Id, new WorkItemViewModel { Id = item.Id, ProjectId = other.Id }, default));

        var member = await _db.ProjectMembers.SingleAsync(m => m.ProjectId == project.Id);
        member.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.EditItem(item.Id, model, default));
        member.IsActive = true;
        user.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.EditItem(item.Id, model, default));
        user.IsActive = true;
        project.Status = ProjectStatus.Cancelled;
        await _db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await controller.EditItem(item.Id, model, default));
    }

    [Fact]
    public async Task InvalidEditPosts_EnforceClientOrganizationForMultipleRoles()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager", "Client A");
        var item = await AddItemAsync(project.Id, phase.Id, "WBS-CLIENT-EDIT", "Item");
        user.OrganizationName = "Client B";
        await _db.SaveChangesAsync();
        var controller = CreateController(user, "Project Manager", "Client Representative");
        controller.ModelState.AddModelError("Name", "Invalid");

        Assert.IsType<NotFoundResult>(await controller.EditPhase(
            phase.Id, new ProjectPhaseViewModel { Id = phase.Id, ProjectId = project.Id }, default));
        Assert.IsType<NotFoundResult>(await controller.EditItem(
            item.Id, new WorkItemViewModel { Id = item.Id, ProjectId = project.Id }, default));
    }

    [Fact]
    public async Task CreateFormsAndStatusControls_RespectAdminPrecedence()
    {
        var (user, project, _) = await CreateMemberAndPhaseAsync("Project Manager");
        var manager = CreateController(user, "Project Manager");
        Assert.IsType<ViewResult>(await manager.CreatePhase(project.Id, default));
        Assert.IsType<ViewResult>(await manager.CreateItem(project.Id, default));

        var adminManager = CreateController(user, RoleNames.Admin, "Project Manager");
        Assert.IsType<ForbidResult>(await adminManager.CreatePhase(project.Id, default));
        Assert.IsType<ForbidResult>(await adminManager.CreateItem(project.Id, default));
        var view = Assert.IsType<ViewResult>(await adminManager.Index(project.Id, false, null, default));
        var model = Assert.IsType<WbsIndexViewModel>(view.Model);
        Assert.False(model.CanManageStructure);
        Assert.False(model.CanUpdateStatus);
        Assert.True(model.CanViewArchived);
        Assert.True(model.CanArchiveRestore);
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Cancelled, false)]
    [InlineData(ProjectStatus.InProgress, true)]
    public async Task ReadOnlyProjects_AllowArchivedViewButNotWrite(ProjectStatus status, bool archived)
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var item = await AddItemAsync(project.Id, phase.Id, "WBS-ARCHIVED-VIEW", "Archived");
        Assert.True((await _service.ArchiveWorkItemAsync(item.Id, user.Id, ["Project Manager"])).Succeeded);
        project.Status = status;
        project.IsArchived = archived;
        await _db.SaveChangesAsync();

        foreach (var role in new[] { RoleNames.Admin, "Project Director", "Project Manager" })
        {
            var controller = CreateController(user, role);
            var result = Assert.IsType<ViewResult>(await controller.Index(project.Id, true, null, default));
            var model = Assert.IsType<WbsIndexViewModel>(result.Model);
            Assert.True(model.CanViewArchived);
            Assert.False(model.CanArchiveRestore);
            Assert.False(model.CanManageStructure);
            Assert.False(model.CanUpdateStatus);
            Assert.True(Assert.Single(model.Tree.Phases.Single().RootItems).Item.IsArchived);
            Assert.IsType<ForbidResult>(await controller.RestoreItem(item.Id, project.Id, default));
        }

        var site = CreateController(user, "Site Manager");
        var siteView = Assert.IsType<ViewResult>(await site.Index(project.Id, false, null, default));
        Assert.False(Assert.IsType<WbsIndexViewModel>(siteView.Model).CanViewArchived);
        Assert.IsType<ForbidResult>(await site.Index(project.Id, true, null, default));
    }

    [Fact]
    public async Task ArchivedOrCompletedProject_IsReadOnlyForWbsChanges()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var item = await AddItemAsync(project.Id, phase.Id, "1", "Item");
        project.Status = ProjectStatus.Completed;
        await _db.SaveChangesAsync();

        var tree = await _service.GetProjectTreeAsync(project.Id, user.Id, ["Project Manager"]);
        var create = await _service.CreatePhaseAsync(
            new ProjectPhase { ProjectId = project.Id, Name = "Not allowed" },
            user.Id,
            ["Project Manager"]);
        var status = await _service.UpdateWorkItemStatusAsync(
            item.Id,
            WbsItemStatus.InProgress,
            user.Id,
            ["Project Manager"]);
        var archive = await _service.ArchiveWorkItemAsync(item.Id, user.Id, ["Project Manager"]);

        Assert.True(tree.Succeeded);
        Assert.True(tree.Value!.IsReadOnly);
        Assert.Equal(WbsFailureKind.Forbidden, create.Failure);
        Assert.Equal(WbsFailureKind.Forbidden, status.Failure);
        Assert.Equal(WbsFailureKind.Forbidden, archive.Failure);
    }

    [Fact]
    public async Task Tree_IsRecursiveOrderedAndCountsLinkedTasks()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var laterRoot = await AddItemAsync(project.Id, phase.Id, "2", "Root 2", sequence: 2);
        var firstRoot = await AddItemAsync(project.Id, phase.Id, "1", "Root 1", sequence: 1);
        var child = await AddItemAsync(project.Id, phase.Id, "1.1", "Child", firstRoot.Id, sequence: 2);
        await AddItemAsync(project.Id, phase.Id, "1.2", "Child 2", firstRoot.Id, sequence: 1);
        _db.Tasks.Add(new TaskEntity
        {
            ProjectId = project.Id,
            WorkItemId = child.Id,
            Title = "Task linked to WBS",
            PlannedStartDate = new DateTime(2026, 1, 1),
            PlannedEndDate = new DateTime(2026, 1, 2)
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetProjectTreeAsync(project.Id, user.Id, ["Project Manager"]);

        Assert.True(result.Succeeded);
        var roots = result.Value!.Phases.Single().RootItems;
        Assert.Equal([firstRoot.Id, laterRoot.Id], roots.Select(node => node.Item.Id));
        Assert.Equal(1, roots[0].Depth);
        Assert.Equal([ "1.2", "1.1" ], roots[0].Children.Select(node => node.Item.WbsCode));
        Assert.Equal(2, roots[0].Children[1].Depth);
        Assert.Equal(1, roots[0].Children[1].LinkedTaskCount);
    }

    [Fact]
    public async Task UpdateWorkItem_RecalculatesLevelsForMovedSubtree()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var firstRoot = await AddItemAsync(project.Id, phase.Id, "A", "A");
        var oldParent = await AddItemAsync(project.Id, phase.Id, "A.1", "Old parent", firstRoot.Id);
        var movedItem = await AddItemAsync(project.Id, phase.Id, "A.1.1", "Moved", oldParent.Id);
        var descendant = await AddItemAsync(project.Id, phase.Id, "A.1.1.1", "Descendant", movedItem.Id);
        var newParent = await AddItemAsync(project.Id, phase.Id, "A.2", "New parent", firstRoot.Id);
        var deeperParent = await AddItemAsync(project.Id, phase.Id, "A.2.1", "Deeper parent", newParent.Id);

        var result = await _service.UpdateWorkItemAsync(
            new WorkItem
            {
                Id = movedItem.Id,
                ProjectId = project.Id,
                ProjectPhaseId = phase.Id,
                ParentWorkItemId = deeperParent.Id,
                WbsCode = "A.2.1.1",
                Name = "Moved"
            },
            user.Id,
            ["Project Manager"]);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(4, (await _db.WorkItems.SingleAsync(item => item.Id == movedItem.Id)).Level);
        Assert.Equal(5, (await _db.WorkItems.SingleAsync(item => item.Id == descendant.Id)).Level);
    }

    [Fact]
    public async Task UpdateWorkItem_RejectsMovingANodeUnderItsDescendant()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var root = await AddItemAsync(project.Id, phase.Id, "1", "Root");
        var child = await AddItemAsync(project.Id, phase.Id, "1.1", "Child", root.Id);

        var result = await _service.UpdateWorkItemAsync(
            new WorkItem
            {
                Id = root.Id,
                ProjectId = project.Id,
                ProjectPhaseId = phase.Id,
                ParentWorkItemId = child.Id,
                WbsCode = "1",
                Name = "Root"
            },
            user.Id,
            ["Project Manager"]);

        Assert.Equal(WbsFailureKind.Validation, result.Failure);
        Assert.Contains("hậu duệ", result.Error);
        Assert.Null((await _db.WorkItems.SingleAsync(item => item.Id == root.Id)).ParentWorkItemId);
    }

    [Fact]
    public async Task WorkItemCreation_RejectsCaseInsensitiveDuplicateCodesAndCrossProjectPhase()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        await AddItemAsync(project.Id, phase.Id, "A-1", "Existing");
        var anotherProject = await AddProjectAsync("WBS-OTHER");
        var otherPhase = await AddPhaseAsync(anotherProject.Id, "Other phase");

        var duplicate = await _service.CreateWorkItemAsync(
            new WorkItem
            {
                ProjectId = project.Id,
                ProjectPhaseId = phase.Id,
                WbsCode = " a-1 ",
                Name = "Duplicate"
            },
            user.Id,
            ["Project Manager"]);
        var wrongPhase = await _service.CreateWorkItemAsync(
            new WorkItem
            {
                ProjectId = project.Id,
                ProjectPhaseId = otherPhase.Id,
                WbsCode = "B-1",
                Name = "Wrong project phase"
            },
            user.Id,
            ["Project Manager"]);

        Assert.Equal(WbsFailureKind.Validation, duplicate.Failure);
        Assert.Contains("Mã WBS", duplicate.Error);
        Assert.Equal(WbsFailureKind.Validation, wrongPhase.Failure);
        Assert.Contains("cùng dự án", wrongPhase.Error);
    }

    [Fact]
    public async Task ArchiveRestore_PreservesSeparateArchiveBatchesAndTaskLinks()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        phase.Status = ProjectPhaseStatus.Completed;
        var root = await AddItemAsync(project.Id, phase.Id, "1", "Root", status: WbsItemStatus.InProgress);
        var child = await AddItemAsync(project.Id, phase.Id, "1.1", "Child", root.Id);
        var task = new TaskEntity
        {
            ProjectId = project.Id,
            WorkItemId = child.Id,
            Title = "Linked task",
            PlannedStartDate = new DateTime(2026, 1, 1),
            PlannedEndDate = new DateTime(2026, 1, 2)
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        Assert.True((await _service.ArchiveWorkItemAsync(child.Id, user.Id, ["Project Manager"])).Succeeded);
        Assert.True((await _service.ArchivePhaseAsync(phase.Id, user.Id, ["Project Manager"])).Succeeded);
        Assert.True((await _service.RestorePhaseAsync(phase.Id, user.Id, ["Project Manager"])).Succeeded);

        var restoredPhase = await _db.ProjectPhases.SingleAsync(item => item.Id == phase.Id);
        Assert.Equal(ProjectPhaseStatus.Completed, restoredPhase.Status);
        Assert.False(restoredPhase.IsArchived);
        Assert.False((await _db.WorkItems.SingleAsync(item => item.Id == root.Id)).IsArchived);
        Assert.True((await _db.WorkItems.SingleAsync(item => item.Id == child.Id)).IsArchived);
        Assert.Equal(child.Id, (await _db.Tasks.SingleAsync(item => item.Id == task.Id)).WorkItemId);

        var existingLink = await _service.CanLinkWorkItemToTaskAsync(
            project.Id,
            child.Id,
            task.Id,
            user.Id,
            ["Project Manager"]);
        var newLink = await _service.CanLinkWorkItemToTaskAsync(
            project.Id,
            child.Id,
            null,
            user.Id,
            ["Project Manager"]);
        Assert.True(existingLink.Succeeded);
        Assert.Equal(WbsFailureKind.Validation, newLink.Failure);

        Assert.True((await _service.RestoreWorkItemAsync(child.Id, user.Id, ["Project Manager"])).Succeeded);
        var restoredChild = await _db.WorkItems.SingleAsync(item => item.Id == child.Id);
        Assert.False(restoredChild.IsArchived);
        Assert.Equal(WbsItemStatus.NotStarted, restoredChild.Status);
    }

    [Fact]
    public async Task TaskUpdate_PreservesExistingArchivedWbsLink()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        project.ProjectManagerId = user.Id;
        var archivedItem = await AddItemAsync(project.Id, phase.Id, "1", "Archived WBS");
        archivedItem.IsArchived = true;
        archivedItem.Status = WbsItemStatus.Archived;
        archivedItem.StatusBeforeArchive = WbsItemStatus.OnHold;
        archivedItem.ArchiveGroupId = Guid.NewGuid();
        var linkedTask = new TaskEntity
        {
            ProjectId = project.Id,
            WorkItemId = archivedItem.Id,
            Title = "Task with existing archived WBS",
            PlannedStartDate = new DateTime(2026, 1, 1),
            PlannedEndDate = new DateTime(2026, 1, 2)
        };
        _db.Tasks.Add(linkedTask);
        await _db.SaveChangesAsync();

        var service = new TaskService(_db, _service);
        var result = await service.UpdateAsync(
            new TaskEntity
            {
                Id = linkedTask.Id,
                ProjectId = project.Id,
                WorkItemId = archivedItem.Id,
                ConcurrencyStamp = linkedTask.ConcurrencyStamp,
                Title = "Task updated without changing its WBS link",
                PlannedStartDate = new DateTime(2026, 1, 1),
                PlannedEndDate = new DateTime(2026, 1, 2)
            },
            [],
            user.Id,
            isAdmin: false,
            ["Project Manager"],
            delayReason: "Lý do chậm tiến độ kiểm thử");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            archivedItem.Id,
            (await _db.Tasks.AsNoTracking().SingleAsync(item => item.Id == linkedTask.Id)).WorkItemId);
    }

    [Fact]
    public async Task Tree_RejectsCyclicLegacyDataInsteadOfReturningAnIncompleteTree()
    {
        var (user, project, phase) = await CreateMemberAndPhaseAsync("Project Manager");
        var first = await AddItemAsync(project.Id, phase.Id, "1", "First");
        var second = await AddItemAsync(project.Id, phase.Id, "1.1", "Second", first.Id);
        first.ParentWorkItemId = second.Id;
        await _db.SaveChangesAsync();

        var result = await _service.GetProjectTreeAsync(project.Id, user.Id, ["Project Manager"]);

        Assert.Equal(WbsFailureKind.Validation, result.Failure);
        Assert.Contains("vòng lặp", result.Error);
    }

    [Fact]
    public async Task Migration_MapsLegacyProjectPhaseStatusesAndArchivedState()
    {
        const string previousMigration = "20261121150000_Task03DocumentManagementCompletion";
        await _db.Database.GetService<IMigrator>().MigrateAsync(previousMigration);
        var project = await AddProjectAsync("WBS-MIGRATION");
        var legacyStatuses = new[] { "Planned", "In Progress", "Completed", "Archived", "Unknown" };
        for (var index = 0; index < legacyStatuses.Length; index++)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO ProjectPhases (ProjectId, Name, SequenceOrder, Status) VALUES ({project.Id}, {legacyStatuses[index]}, {index}, {legacyStatuses[index]})");
        }

        await _db.Database.MigrateAsync();

        var phases = await _db.ProjectPhases
            .AsNoTracking()
            .Where(item => item.ProjectId == project.Id)
            .OrderBy(item => item.SequenceOrder)
            .ToListAsync();
        Assert.Equal(
            [
                ProjectPhaseStatus.NotStarted,
                ProjectPhaseStatus.InProgress,
                ProjectPhaseStatus.Completed,
                ProjectPhaseStatus.Archived,
                ProjectPhaseStatus.NotStarted
            ],
            phases.Select(item => item.Status));
        Assert.False(phases[0].IsArchived);
        Assert.True(phases[3].IsArchived);
        Assert.Equal(ProjectPhaseStatus.NotStarted, phases[3].StatusBeforeArchive);
    }

    private WbsController CreateController(ApplicationUser user, params string[] roles)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(_db);
        var userManager = new UserManager<ApplicationUser>(
            store, Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return new WbsController(_service, userManager)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private async Task<(ApplicationUser User, Project Project, ProjectPhase Phase)> CreateMemberAndPhaseAsync(
        string role,
        string? organizationName = null)
    {
        var user = NewUser($"wbs-user-{Guid.NewGuid():N}", organizationName);
        _db.Users.Add(user);
        var project = await AddProjectAsync($"WBS-{Guid.NewGuid():N}", organizationName);
        if (role != RoleNames.Admin)
        {
            _db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = project.Id,
                UserId = user.Id,
                IsActive = true
            });
        }

        var phase = await AddPhaseAsync(project.Id, "Phase");
        await _db.SaveChangesAsync();
        return (user, project, phase);
    }

    private async Task<Project> AddProjectAsync(string code, string? clientName = null)
    {
        var project = new Project
        {
            ProjectCode = code,
            Name = code,
            ClientName = clientName,
            StartDate = new DateTime(2026, 1, 1),
            ExpectedEndDate = new DateTime(2027, 1, 1),
            Budget = 0
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<ProjectPhase> AddPhaseAsync(int projectId, string name)
    {
        var phase = new ProjectPhase { ProjectId = projectId, Name = name };
        _db.ProjectPhases.Add(phase);
        await _db.SaveChangesAsync();
        return phase;
    }

    private async Task<WorkItem> AddItemAsync(
        int projectId,
        int phaseId,
        string code,
        string name,
        int? parentId = null,
        int sequence = 0,
        WbsItemStatus status = WbsItemStatus.NotStarted)
    {
        var item = new WorkItem
        {
            ProjectId = projectId,
            ProjectPhaseId = phaseId,
            ParentWorkItemId = parentId,
            WbsCode = code,
            Name = name,
            Level = parentId.HasValue
                ? (await _db.WorkItems.SingleAsync(candidate => candidate.Id == parentId.Value)).Level + 1
                : 1,
            SequenceOrder = sequence,
            Status = status
        };
        _db.WorkItems.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    private static ApplicationUser NewUser(string userName, string? organizationName = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = $"{userName}@test.invalid",
        NormalizedEmail = $"{userName}@test.invalid".ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString(),
        OrganizationName = organizationName,
        IsActive = true
    };
}
