using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services;

public sealed class ProgressService(ApplicationDbContext dbContext) : IProgressService
{
    public async Task<IReadOnlyCollection<TaskEntity>> GetVisibleTasksAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return [];
        var query = VisibleQuery(access);
        return await query
            .OrderBy(task => task.PlannedEndDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string? Error)> UpdateAsync(
        int taskId,
        decimal percentComplete,
        TaskStatusModel status,
        string? delayReason,
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return (false, "Tài khoản không có quyền truy cập.");
        var task = await VisibleQuery(access)
            .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null || !access.CanUpdateProgress(task))
        {
            return (false, "Không tìm thấy công việc hoặc bạn không có quyền cập nhật.");
        }

        var lifecycle = new TaskLifecycle(dbContext);
        var error = await lifecycle.ValidateAsync(task.Id, task.RequiresInspection, status, percentComplete, cancellationToken);
        if (error is not null) return (false, error);

        var isOverdue = task.PlannedEndDate.Date < DateTime.UtcNow.Date &&
                        percentComplete < 100;
        if (isOverdue && string.IsNullOrWhiteSpace(delayReason))
        {
            return (false, "Công việc chậm tiến độ phải có nguyên nhân.");
        }

        lifecycle.Apply(task, status, percentComplete, userId, delayReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<IReadOnlyCollection<ProgressChartPoint>> GetChartAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return [];
        var tasks = await VisibleQuery(access)
            .ToListAsync(cancellationToken);

        return tasks
            .GroupBy(task => task.Project.Name)
            .Select(group => new ProgressChartPoint(
                group.Key,
                group.Average(task => CalculatePlannedPercent(task, DateTime.UtcNow.Date)),
                group.Average(task => task.PercentComplete)))
            .ToList();
    }

    private IQueryable<TaskEntity> VisibleQuery(TaskAccess access)
    {
        var projectIds = access.ProjectIds;
        return dbContext.Tasks
            .Include(task => task.Project)
            .Include(task => task.Assignments)
            .Where(task => projectIds.Contains(task.ProjectId));
    }

    private static decimal CalculatePlannedPercent(TaskEntity task, DateTime today)
    {
        if (today <= task.PlannedStartDate.Date)
        {
            return 0;
        }

        if (today >= task.PlannedEndDate.Date)
        {
            return 100;
        }

        var totalDays = (task.PlannedEndDate.Date - task.PlannedStartDate.Date).TotalDays;
        var elapsedDays = (today - task.PlannedStartDate.Date).TotalDays;
        return totalDays <= 0 ? 100 : (decimal)(elapsedDays / totalDays * 100);
    }
}
