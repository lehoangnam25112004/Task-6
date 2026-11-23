using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.Services.Interfaces;
using ConstructionProjectManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;
using TaskStatus = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Controllers;

[Authorize]
public class QualityController(
    ApplicationDbContext dbContext,
    IQualityService qualityService,
    UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanViewQuality) return Forbid();
        var projectIds = access.ProjectIds;
        var inspections = await dbContext.InspectionRequests.AsNoTracking()
            .Include(item => item.Project).Include(item => item.Task)
            .Include(item => item.InspectedBy)
            .Where(item => item.ProjectId == item.Task.ProjectId && projectIds.Contains(item.Task.ProjectId))
            .OrderByDescending(item => item.RequestedAt).ThenByDescending(item => item.Id)
            .Take(100).ToListAsync(cancellationToken);
        var taskIds = inspections.Select(item => item.TaskId).Distinct().ToArray();
        var inspectionOrder = await dbContext.InspectionRequests.AsNoTracking()
            .Where(item => taskIds.Contains(item.TaskId))
            .OrderByDescending(item => item.RequestedAt).ThenByDescending(item => item.Id)
            .Select(item => new { item.TaskId, item.Id }).ToListAsync(cancellationToken);
        return View(new QualityIndexViewModel
        {
            CanManageQuality = access.CanManageQuality,
            WritableProjectIds = access.CanManageQuality ? access.WritableProjectIds.ToHashSet() : new HashSet<int>(),
            Incidents = access.CanManageQuality ? await dbContext.Incidents.AsNoTracking()
                .Include(item => item.Project).Include(item => item.AssignedTo)
                .Where(item => projectIds.Contains(item.ProjectId))
                .OrderByDescending(item => item.CreatedAt).Take(100).ToListAsync(cancellationToken) : [],
            Rfis = access.CanManageQuality ? await dbContext.RfiRequests.AsNoTracking()
                .Include(item => item.Project).Include(item => item.CreatedBy)
                .Where(item => projectIds.Contains(item.ProjectId))
                .OrderByDescending(item => item.CreatedAt).Take(100).ToListAsync(cancellationToken) : [],
            Inspections = inspections,
            LatestInspectionIds = inspectionOrder.GroupBy(item => item.TaskId).Select(group => group.First().Id).ToHashSet()
        });
    }

    public async Task<IActionResult> CreateIncident(CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        await LoadOptionsAsync(access, cancellationToken);
        return View(new IncidentCreateViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateIncident(
        IncidentCreateViewModel model, CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality || (model.ProjectId > 0 && !access.CanWriteQuality(model.ProjectId))) return Forbid();
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(access, cancellationToken);
            return View(model);
        }
        var userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();
        var result = await qualityService.CreateIncidentAsync(model, userId, access.Roles, cancellationToken);
        return await HandleResult(result, nameof(CreateIncident), model, access, cancellationToken);
    }

    public async Task<IActionResult> CreateRfi(CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        await LoadOptionsAsync(access, cancellationToken);
        return View(new RfiCreateViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRfi(
        RfiCreateViewModel model, CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality || (model.ProjectId > 0 && !access.CanWriteQuality(model.ProjectId))) return Forbid();
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(access, cancellationToken);
            return View(model);
        }
        var userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();
        var result = await qualityService.CreateRfiAsync(model, userId, access.Roles, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadOptionsAsync(access, cancellationToken);
            return View(model);
        }
        TempData["SuccessMessage"] = "Đã tạo RFI.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> RespondRfi(int id, CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        var rfi = await dbContext.RfiRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rfi is null || !access.CanWriteQuality(rfi.ProjectId)) return NotFound();
        return View(new RfiRespondViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondRfi(
        int id, RfiRespondViewModel model, CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        var rfi = await dbContext.RfiRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rfi is null || !access.CanWriteQuality(rfi.ProjectId)) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();
        var result = await qualityService.RespondRfiAsync(id, model, userId, access.Roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã ghi nhận phản hồi RFI." : result.Error!;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> CreateInspection(CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        await LoadInspectionTasksAsync(access, cancellationToken);
        return View(new InspectionCreateViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInspection(
        InspectionCreateViewModel model, CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        if (model.TaskId > 0)
        {
            var task = await dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.Id == model.TaskId, cancellationToken);
            if (task is null || !access.CanWriteQuality(task.ProjectId)) return NotFound();
        }
        if (!ModelState.IsValid)
        {
            await LoadInspectionTasksAsync(access, cancellationToken);
            return View(model);
        }
        var userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();
        var result = await qualityService.CreateInspectionAsync(model, userId, access.Roles, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadInspectionTasksAsync(access, cancellationToken);
            return View(model);
        }
        TempData["SuccessMessage"] = "Đã tạo yêu cầu nghiệm thu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordInspection(
        int id, InspectionResultViewModel model, CancellationToken cancellationToken)
    {
        var access = await CurrentAccessAsync(cancellationToken);
        if (access is null || !access.CanManageQuality) return Forbid();
        var inspection = await dbContext.InspectionRequests.AsNoTracking().Include(item => item.Task)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (inspection is null || inspection.ProjectId != inspection.Task.ProjectId ||
            !access.CanWriteQuality(inspection.Task.ProjectId)) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Dữ liệu nghiệm thu không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }
        var userId = userManager.GetUserId(User);
        if (userId is null) return Challenge();
        var result = await qualityService.RecordInspectionAsync(id, model, userId, access.Roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã ghi nhận kết quả nghiệm thu." : result.Error!;
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> HandleResult(
        (bool Succeeded, string? Error) result, string action,
        IncidentCreateViewModel model, TaskAccess access, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadOptionsAsync(access, cancellationToken);
            return View(action, model);
        }
        TempData["SuccessMessage"] = "Đã tạo sự cố.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadOptionsAsync(TaskAccess access, CancellationToken cancellationToken)
    {
        var projectIds = access.WritableProjectIds;
        ViewBag.Projects = await dbContext.Projects.Where(project => projectIds.Contains(project.Id))
            .OrderBy(project => project.ProjectCode).ToListAsync(cancellationToken);
        ViewBag.Tasks = await dbContext.Tasks.Include(task => task.Project)
            .Where(task => projectIds.Contains(task.ProjectId) && task.Status != TaskStatus.Cancelled)
            .OrderBy(task => task.Title).ToListAsync(cancellationToken);
        ViewBag.Users = await dbContext.Users.Where(user => user.IsActive &&
            (dbContext.ProjectMembers.Any(member => projectIds.Contains(member.ProjectId) && member.UserId == user.Id && member.IsActive) ||
             dbContext.Projects.Any(project => projectIds.Contains(project.Id) && project.ProjectManagerId == user.Id)))
            .OrderBy(user => user.FullName).ToListAsync(cancellationToken);
    }

    private async Task LoadInspectionTasksAsync(TaskAccess access, CancellationToken cancellationToken)
    {
        var projectIds = access.WritableProjectIds;
        ViewBag.Tasks = await dbContext.Tasks.Include(task => task.Project)
            .Where(task => projectIds.Contains(task.ProjectId) && task.RequiresInspection &&
                task.Status != TaskStatus.Cancelled && task.Status != TaskStatus.Completed && task.PercentComplete < 100)
            .OrderBy(task => task.Title).ToListAsync(cancellationToken);
    }

    private async Task<TaskAccess?> CurrentAccessAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? null : await TaskAccess.LoadAsync(dbContext, user.Id, (await userManager.GetRolesAsync(user)).ToArray(), cancellationToken);
    }
}
