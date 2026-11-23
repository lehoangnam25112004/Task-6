using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionProjectManagement.Controllers;

[Authorize]
public class DashboardController(
    DashboardService dashboardService,
    UserManager<ConstructionProjectManagement.Models.ApplicationUser> userManager,
    ApplicationDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var access = await TaskAccess.LoadAsync(dbContext, user.Id, (await userManager.GetRolesAsync(user)).ToArray(), cancellationToken);
        if (access is null) return Forbid();
        return View(await dashboardService.GetSummaryAsync(access, cancellationToken));
    }
}
