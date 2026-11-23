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
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var access = await TaskAccess.LoadAsync(dbContext, user.Id, roles, cancellationToken);
        if (access is null) return Forbid();
        var tasks = await progressService.GetVisibleTasksAsync(user.Id, roles, cancellationToken);
        return View(new ProgressIndexViewModel
        {
            Tasks = tasks,
            UpdatableTaskIds = tasks.Where(access.CanUpdateProgress).Select(task => task.Id).ToHashSet()
        });
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
        if (task is null || !access.CanView(task.ProjectId)) return NotFound();
        if (!access.CanUpdateProgress(task)) return Forbid();
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
            cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? "Đã cập nhật tiến độ." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ChartData(CancellationToken cancellationToken)
    {
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
            cancellationToken);
        return Json(data);
    }

}
