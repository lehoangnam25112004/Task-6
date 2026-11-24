using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.Services.Interfaces;
using ConstructionProjectManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConstructionProjectManagement.Controllers;

[Authorize]
public class ProgressController(
    IProgressService progressService,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index([FromQuery] ProgressFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || filter.Page < 1 ||
            (filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value)) ||
            (filter.Priority.HasValue && !Enum.IsDefined(filter.Priority.Value)))
            return BadRequest("Bộ lọc không hợp lệ.");
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        return View(await progressService.GetOverviewAsync(filter, access, cancellationToken));
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var task = await dbContext.Tasks.AsNoTracking()
            .Include(item => item.Project).Include(item => item.WorkItem)
            .Include(item => item.Assignments).ThenInclude(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (task is null || !access.CanViewTrackingTask(task)) return NotFound();
        return View("~/Views/Tasks/Details.cshtml",
            await new TaskTrackingService(dbContext).GetDetailsAsync(task, access, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDependency(int taskId, int predecessorId, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var result = await new TaskDependencyService(dbContext).AddAsync(taskId, predecessorId, user.Id, roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã thêm phụ thuộc." : result.Error;
        return RedirectToAction(nameof(Details), new { id = taskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDependency(int taskId, int dependencyId, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await dbContext.TaskDependencies.AnyAsync(item => item.Id == dependencyId && item.TaskId == taskId, cancellationToken))
            return NotFound();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var result = await new TaskDependencyService(dbContext).RemoveAsync(dependencyId, user.Id, roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã xóa phụ thuộc." : result.Error;
        return RedirectToAction(nameof(Details), new { id = taskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int taskId, string? content, bool isBlocker, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var result = await new TaskCommentService(dbContext).AddAsync(taskId, content, isBlocker, user.Id, roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã thêm bình luận." : result.Error;
        return RedirectToAction(nameof(Details), new { id = taskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditComment(int taskId, int commentId, string? content, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await dbContext.TaskComments.AnyAsync(item => item.Id == commentId && item.TaskId == taskId, cancellationToken))
            return NotFound();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var result = await new TaskCommentService(dbContext).EditAsync(commentId, content, user.Id, roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã sửa bình luận." : result.Error;
        return RedirectToAction(nameof(Details), new { id = taskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveBlocker(int taskId, int commentId, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var comment = await dbContext.TaskComments.AsNoTracking()
            .Include(item => item.Task).ThenInclude(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == commentId && item.TaskId == taskId, cancellationToken);
        if (comment is null || !access.CanViewTrackingTask(comment.Task)) return NotFound();
        if (!access.CanResolveBlocker(comment.Task, comment.AuthorId)) return Forbid();
        var result = await new TaskCommentService(dbContext).ResolveBlockerAsync(
            taskId, commentId, user.Id, roles, cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã giải quyết vướng mắc." : result.Error;
        return RedirectToAction("Details", "Tasks", new { id = taskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        ProgressUpdateViewModel model,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var task = await dbContext.Tasks.AsNoTracking().Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == model.TaskId, cancellationToken);
        if (task is null || !access.CanViewTrackingTask(task)) return NotFound();
        if (!access.CanUpdateProgress(task)) return Forbid();
        if ((model.Status == Models.TaskStatus.Cancelled || model.Reopen) && !access.CanCancelOrReopen(task.ProjectId))
            return Forbid();
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Dữ liệu tiến độ không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var result = await progressService.UpdateAsync(
            model.TaskId,
            model.PercentComplete,
            model.Status,
            model.DelayReason,
            user.Id,
            roles,
            cancellationToken,
            model.ConcurrencyStamp,
            model.Reason,
            model.Reopen);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã cập nhật tiến độ." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreCancelled(RestoreCancelledTaskViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null || !access.CanManageTasks) return Forbid();
        var task = await dbContext.Tasks.AsNoTracking().Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == model.TaskId, cancellationToken);
        if (task is null || !access.CanViewTrackingTask(task)) return NotFound();

        var result = ModelState.IsValid
            ? await progressService.RestoreCancelledAsync(model.TaskId, model.Status, model.Reason,
                model.DelayReason, model.ConcurrencyStamp, user.Id, roles, cancellationToken)
            : (Succeeded: false, Error: "Dữ liệu khôi phục không hợp lệ.");
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã khôi phục công việc." : result.Error;
        return model.ReturnToDetails
            ? RedirectToAction("Details", "Tasks", new { id = model.TaskId })
            : RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ChartData([FromQuery] ProgressFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || (filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value)) ||
            (filter.Priority.HasValue && !Enum.IsDefined(filter.Priority.Value)))
            return BadRequest("Bộ lọc không hợp lệ.");
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        if (await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken) is null) return Forbid();
        var data = await progressService.GetChartAsync(
            user.Id,
            roles,
            cancellationToken,
            filter);
        return Json(data);
    }

}
