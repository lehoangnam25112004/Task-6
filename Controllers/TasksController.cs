using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.Services.Interfaces;
using ConstructionProjectManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace ConstructionProjectManagement.Controllers;

[Authorize]
public class TasksController(
    ITaskService taskService,
    IWbsService wbsService,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(int? projectId, Models.TaskStatus? status, TaskPriority? priority, string? keyword, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || (status.HasValue && !Enum.IsDefined(status.Value)) ||
            (priority.HasValue && !Enum.IsDefined(priority.Value)))
            return BadRequest("Bộ lọc không hợp lệ.");
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var tasks = await taskService.GetVisibleAsync(user.Id, roles, cancellationToken);
        var query = tasks.AsEnumerable();
        if (projectId.HasValue) query = query.Where(task => task.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(task => task.Status == status.Value);
        if (priority.HasValue) query = query.Where(task => task.Priority == priority.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            query = query.Where(task => task.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (task.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var projectIds = access.ProjectIds;
        var projects = await dbContext.Projects.AsNoTracking().Where(project => projectIds.Contains(project.Id))
            .OrderBy(project => project.Name).ToListAsync(cancellationToken);
        var filteredTasks = query.ToList();
        var trackingTaskIds = filteredTasks.Where(access.CanViewTrackingTask).Select(task => task.Id).ToArray();
        var blockerTaskIds = await dbContext.TaskComments.AsNoTracking()
            .Where(comment => trackingTaskIds.Contains(comment.TaskId) && comment.IsBlocker)
            .Select(comment => comment.TaskId).Distinct().ToListAsync(cancellationToken);
        return View(new TaskListViewModel
        {
            Tasks = filteredTasks, BlockerCommentTaskIds = blockerTaskIds.ToHashSet(),
            Projects = projects, ProjectId = projectId, Status = status,
            Priority = priority, Keyword = keyword,
            CanCreate = access.CanManageTasks && access.WritableProjectIds.Length > 0
        });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var task = await taskService.GetByIdAsync(id, user.Id, roles, cancellationToken);
        return task is null ? NotFound() : View(await new TaskTrackingService(dbContext).GetDetailsAsync(task, access, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> AdjustSchedule(int id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var task = await taskService.GetByIdAsync(id, user.Id, roles, cancellationToken);
        if (task is null) return NotFound();
        if (!access.CanAdjustSchedule(task)) return Forbid();
        ViewData["TaskTitle"] = task.Title;
        return View(new TaskScheduleViewModel
        {
            Id = task.Id, ConcurrencyStamp = task.ConcurrencyStamp,
            PlannedStartDate = task.PlannedStartDate, PlannedEndDate = task.PlannedEndDate
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustSchedule(int id, TaskScheduleViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var task = await taskService.GetByIdAsync(id, user.Id, roles, cancellationToken);
        if (task is null) return NotFound();
        if (!access.CanAdjustSchedule(task)) return Forbid();
        ViewData["TaskTitle"] = task.Title;
        if (!ModelState.IsValid || !model.PlannedStartDate.HasValue || !model.PlannedEndDate.HasValue || !model.ConcurrencyStamp.HasValue)
            return View(model);
        var result = await taskService.AdjustScheduleAsync(id, model.PlannedStartDate.Value, model.PlannedEndDate.Value,
            model.ConcurrencyStamp.Value, model.Reason, user.Id, roles, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }
        TempData["SuccessMessage"] = "Đã điều chỉnh kế hoạch.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Create(int? projectId, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, (await userManager.GetRolesAsync(user)).ToArray(), cancellationToken);
        if (access is null || !access.CanManageTasks || (projectId.HasValue && !access.CanEditTask(projectId.Value))) return Forbid();
        var model = new TaskEditViewModel { ProjectId = projectId ?? 0 };
        await LoadOptionsAsync(model, currentTask: null, access, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TaskEditViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, (await userManager.GetRolesAsync(user)).ToArray(), cancellationToken);
        if (access is null || !access.CanManageTasks || (model.ProjectId > 0 && !access.CanEditTask(model.ProjectId))) return Forbid();
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(model, currentTask: null, access, cancellationToken);
            return View(model);
        }

        var result = await taskService.CreateAsync(
            ToEntity(model),
            model.AssigneeIds,
            user.Id,
            access.Roles.Contains(RoleNames.Admin),
            access.Roles,
            cancellationToken,
            model.DelayReason);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadOptionsAsync(model, currentTask: null, access, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = "Đã tạo công việc.";
        return RedirectToAction(nameof(Details), new { id = result.Task!.Id });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var task = await taskService.GetByIdAsync(id, user.Id, roles, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }
        if (!access.CanEditTask(task.ProjectId)) return Forbid();

        var model = ToViewModel(task);
        await LoadOptionsAsync(model, task, access, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        TaskEditViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var currentTask = await taskService.GetByIdAsync(id, user.Id, roles, cancellationToken);
        if (currentTask is null) return NotFound();
        if (!access.CanEditTask(currentTask.ProjectId)) return Forbid();
        if (model.ProjectId != currentTask.ProjectId) return BadRequest();
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(model, currentTask, access, cancellationToken);
            return View(model);
        }

        var result = await taskService.UpdateAsync(
            ToEntity(model, id),
            model.AssigneeIds,
            user.Id,
            roles.Contains(RoleNames.Admin),
            roles,
            cancellationToken,
            model.DelayReason,
            model.ScheduleReason);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadOptionsAsync(model, currentTask, access, cancellationToken);
            return View(model);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadOptionsAsync(
        TaskEditViewModel model,
        TaskEntity? currentTask,
        TaskAccess access,
        CancellationToken cancellationToken)
    {
        var projectIds = access.WritableProjectIds;
        ViewBag.Projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .OrderBy(project => project.Name)
            .ToListAsync(cancellationToken);
        var user = await userManager.GetUserAsync(User);
        var roles = access.Roles;
        var canLinkWbs = roles.Contains("Project Manager", StringComparer.Ordinal) ||
                         roles.Contains("Site Manager", StringComparer.Ordinal) ||
                         roles.Contains("Supervising Engineer", StringComparer.Ordinal) ||
                         roles.Contains("Materials Staff", StringComparer.Ordinal);
        IReadOnlyCollection<WorkItem> workItems = [];
        string? wbsMessage = null;
        if (user is not null && access.CanEditTask(model.ProjectId) && canLinkWbs)
        {
            var result = await wbsService.GetTaskWorkItemsAsync(
                model.ProjectId,
                currentTask?.Id,
                user.Id,
                roles,
                cancellationToken);
            if (result.Succeeded)
            {
                workItems = result.Value!;
            }
            else
            {
                canLinkWbs = false;
                wbsMessage = result.Failure == WbsFailureKind.Forbidden
                    ? "Dự án chỉ đọc hoặc vai trò hiện tại không được phép gắn WBS."
                    : "Không có quyền xem các hạng mục WBS của dự án này.";
            }
        }

        if (currentTask?.ProjectId == model.ProjectId &&
            currentTask.WorkItem is not null &&
            workItems.All(item => item.Id != currentTask.WorkItem.Id))
        {
            workItems = workItems.Append(currentTask.WorkItem).ToList();
        }

        ViewBag.WorkItems = workItems;
        ViewBag.CanLinkWbs = canLinkWbs;
        ViewBag.WbsMessage = wbsMessage;
        ViewBag.Tasks = await dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.ProjectId == model.ProjectId && projectIds.Contains(task.ProjectId) && task.Id != model.Id)
            .OrderBy(task => task.Title)
            .ToListAsync(cancellationToken);
        var project = await dbContext.Projects.AsNoTracking()
            .Where(item => item.Id == model.ProjectId && projectIds.Contains(item.Id))
            .Select(item => new { item.Id, item.ProjectManagerId })
            .SingleOrDefaultAsync(cancellationToken);
        ViewBag.Users = project is null
            ? []
            : await dbContext.Users.AsNoTracking()
                .Where(user => user.IsActive &&
                    (user.Id == project.ProjectManagerId || dbContext.ProjectMembers.Any(member =>
                        member.ProjectId == project.Id && member.UserId == user.Id && member.IsActive)))
                .OrderBy(user => user.FullName)
                .ToListAsync(cancellationToken);
    }

    private static TaskEntity ToEntity(TaskEditViewModel model, int? id = null)
    {
        return new TaskEntity
        {
            Id = id ?? 0,
            ProjectId = model.ProjectId,
            WorkItemId = model.WorkItemId,
            ParentTaskId = model.ParentTaskId,
            Title = model.Title,
            Description = model.Description,
            Priority = model.Priority,
            PlannedStartDate = model.PlannedStartDate,
            PlannedEndDate = model.PlannedEndDate,
            ActualStartDate = model.ActualStartDate,
            ActualEndDate = model.ActualEndDate,
            Status = model.Status,
            PercentComplete = model.PercentComplete,
            RequiresInspection = model.RequiresInspection,
            ConcurrencyStamp = model.ConcurrencyStamp,
            CancelReason = model.CancelReason
        };
    }

    private static TaskEditViewModel ToViewModel(TaskEntity task)
    {
        return new TaskEditViewModel
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            WorkItemId = task.WorkItemId,
            ParentTaskId = task.ParentTaskId,
            Title = task.Title,
            Description = task.Description,
            Priority = task.Priority,
            PlannedStartDate = task.PlannedStartDate,
            PlannedEndDate = task.PlannedEndDate,
            ActualStartDate = task.ActualStartDate,
            ActualEndDate = task.ActualEndDate,
            Status = task.Status,
            PercentComplete = task.PercentComplete,
            RequiresInspection = task.RequiresInspection,
            ConcurrencyStamp = task.ConcurrencyStamp,
            CancelReason = task.CancelReason,
            AssigneeIds = task.Assignments.Select(assignment => assignment.UserId).ToList()
        };
    }
}
