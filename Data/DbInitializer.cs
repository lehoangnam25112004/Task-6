using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Task = System.Threading.Tasks.Task;

namespace ConstructionProjectManagement.Data;

public static class DbInitializer
{
    public static async Task SeedRolesAsync(
        RoleManager<IdentityRole> roleManager,
        CancellationToken cancellationToken = default)
    {
        foreach (var roleName in RoleNames.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Không thể tạo vai trò '{roleName}': {errors}");
            }
        }
    }

    public static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var email = configuration["CPMS_ADMIN_EMAIL"];
        var password = configuration["CPMS_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Bỏ qua seed Admin: cần cấu hình CPMS_ADMIN_EMAIL và CPMS_ADMIN_PASSWORD.");
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = "Quản trị hệ thống",
                IsActive = true
            };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Không thể tạo tài khoản Admin: {errors}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, RoleNames.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Admin);
            if (!roleResult.Succeeded)
            {
                var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Không thể gán vai trò Admin: {errors}");
            }
        }
    }

    public static async Task ApplyMigrationsAsync(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.Any(migration => migration.EndsWith("_AddTask06TrackingExtensions", StringComparison.Ordinal)) &&
            !string.Equals(configuration["CPMS_ALLOW_TASK06_MIGRATION"], "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Migration TASK-06 chưa được cho phép. Sao lưu và kiểm tra dữ liệu trước khi đặt CPMS_ALLOW_TASK06_MIGRATION=true.");
        }
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public static async Task SeedSampleDataAsync(
        ApplicationDbContext dbContext,
        IProjectService projectService,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!bool.TryParse(configuration["CPMS_SEED_SAMPLE_DATA"], out var enabled) || !enabled)
        {
            return;
        }

        const string sampleCode = "DEMO-001";
        if (await dbContext.Projects.AnyAsync(project => project.ProjectCode == sampleCode, cancellationToken))
        {
            return;
        }

        var adminEmail = configuration["CPMS_ADMIN_EMAIL"];
        var admin = string.IsNullOrWhiteSpace(adminEmail)
            ? null
            : await dbContext.Users.SingleOrDefaultAsync(
                user => user.Email == adminEmail,
                cancellationToken);

        var project = new Project
        {
            ProjectCode = sampleCode,
            Name = "Dự án mẫu kiểm thử",
            Location = "Hà Nội",
            ClientName = "Khách hàng mẫu",
            StartDate = DateTime.UtcNow.Date,
            ExpectedEndDate = DateTime.UtcNow.Date.AddMonths(6),
            Budget = 1_000_000_000m,
        };
        project.Phases.Add(new ProjectPhase
        {
            Name = "Chuẩn bị mặt bằng",
            SequenceOrder = 1
        });

        var managerId = admin is not null &&
                        await dbContext.Employees.AnyAsync(
                            employee => employee.UserId == admin.Id && employee.IsActive,
                            cancellationToken)
            ? admin.Id
            : null;
        var result = await projectService.CreateAsync(
            project,
            managerId,
            null,
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Không thể tạo dự án mẫu: {result.Error}");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
