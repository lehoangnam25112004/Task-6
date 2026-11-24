using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services.Interfaces;
using ConstructionProjectManagement.ViewModels;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services;

public sealed class ProgressService(ApplicationDbContext dbContext) : IProgressService
{
    public async Task<ProgressIndexViewModel> GetOverviewAsync(
        ProgressFilterViewModel filter, TaskAccess access, CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;
        var visible = VisibleQuery(access).AsNoTracking();
        var visibleProjectIds = visible.Select(task => task.ProjectId).Distinct();
        var projects = await dbContext.Projects.AsNoTracking().Where(project => visibleProjectIds.Contains(project.Id))
            .OrderBy(project => project.Name).ToListAsync(cancellationToken);
        var visibleWorkItemIds = visible.Where(task => task.WorkItemId != null).Select(task => task.WorkItemId);
        var workItems = await dbContext.WorkItems.AsNoTracking()
            .Where(item => visibleWorkItemIds.Contains(item.Id) &&
                (!filter.ProjectId.HasValue || item.ProjectId == filter.ProjectId.Value))
            .OrderBy(item => item.WbsCode).ToListAsync(cancellationToken);
        var query = Filter(visible, filter);
        var count = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
        filter.Page = Math.Clamp(filter.Page, 1, pages);
        var tasks = await query.Include(task => task.WorkItem)
            .Include(task => task.Dependencies).ThenInclude(item => item.PredecessorTask)
            .OrderBy(task => task.PlannedEndDate).ThenBy(task => task.Id)
            .Skip((filter.Page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var taskIds = tasks.Select(task => task.Id).ToArray();
        var blockers = await dbContext.TaskComments.AsNoTracking()
            .Where(comment => taskIds.Contains(comment.TaskId) && comment.IsBlocker)
            .Select(comment => comment.TaskId).Distinct().ToListAsync(cancellationToken);
        return new ProgressIndexViewModel
        {
            Tasks = tasks, UpdatableTaskIds = tasks.Where(access.CanUpdateProgress).Select(task => task.Id).ToHashSet(),
            CancelableTaskIds = tasks.Where(task => access.CanCancelOrReopen(task.ProjectId)).Select(task => task.Id).ToHashSet(),
            Filter = filter, Projects = projects, WorkItems = workItems, TotalPages = pages, TotalCount = count,
            BlockerCommentTaskIds = blockers.ToHashSet()
        };
    }

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
        CancellationToken cancellationToken = default,
        Guid? concurrencyStamp = null,
        string? reason = null,
        bool reopen = false)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return (false, "Tài khoản không có quyền truy cập.");
        var task = await VisibleQuery(access)
            .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null || !access.CanViewTrackingTask(task) || !access.CanUpdateProgress(task))
        {
            return (false, "Không tìm thấy công việc hoặc bạn không có quyền cập nhật.");
        }
        if (!concurrencyStamp.HasValue || task.ConcurrencyStamp != concurrencyStamp.Value)
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        if ((status == TaskStatusModel.Cancelled || reopen) && !access.CanCancelOrReopen(task.ProjectId))
            return (false, "Bạn không có quyền hủy hoặc mở lại công việc.");

        var lifecycle = new TaskLifecycle(dbContext);
        if (status == TaskStatusModel.InProgress && percentComplete == 100 && !task.RequiresInspection)
            status = TaskStatusModel.Completed;
        var createInspection = status == TaskStatusModel.PendingInspection &&
            task.Status != TaskStatusModel.PendingInspection;
        if (createInspection && !access.CanWriteQuality(task.ProjectId))
            return (false, "Chỉ người có quyền nghiệm thu mới được gửi yêu cầu nghiệm thu.");
        if (createInspection && await dbContext.InspectionRequests.AnyAsync(
            item => item.TaskId == task.Id && item.Result == InspectionResult.Pending, cancellationToken))
            return (false, "Công việc đã có yêu cầu nghiệm thu đang chờ xử lý.");
        var error = await lifecycle.ValidateChangeAsync(task, status, percentComplete, task.Assignments.Count,
            delayReason, reason, inspectionRequested: createInspection, reopen: reopen, cancellationToken: cancellationToken);
        if (error is not null) return (false, error);

        if (createInspection)
        {
            dbContext.InspectionRequests.Add(new InspectionRequest
            {
                ProjectId = task.ProjectId, TaskId = task.Id, RequestedById = userId
            });
        }
        if (status == TaskStatusModel.Cancelled) task.CancelReason = reason?.Trim();
        if (status == TaskStatusModel.Cancelled || reopen)
            new TaskAuditService(dbContext).Add(task, userId, reopen ? "Reopened" : "Cancelled", reason: reason?.Trim());
        lifecycle.Apply(task, status, percentComplete, userId, delayReason, note: reason);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        }
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> RestoreCancelledAsync(
        int taskId, TaskStatusModel? status, string? reason, string? delayReason, Guid? concurrencyStamp,
        string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return (false, "Tài khoản không có quyền truy cập.");
        var task = await VisibleQuery(access).Include(item => item.WorkItem)
            .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null || !access.CanViewTrackingTask(task))
            return (false, "Không tìm thấy công việc hoặc công việc ngoài phạm vi dự án.");
        if (!access.CanManageTasks)
            return (false, "Bạn không có quyền khôi phục công việc đã hủy.");
        if (!access.CanCancelOrReopen(task.ProjectId))
            return (false, "Dự án đã lưu trữ, hoàn thành hoặc hủy; không thể khôi phục công việc.");
        if (task.Status != TaskStatusModel.Cancelled)
            return (false, "Chỉ có thể khôi phục công việc đã hủy.");
        if (task.WorkItem?.IsArchived == true)
            return (false, "Hạng mục WBS đã lưu trữ; không thể khôi phục công việc.");
        if (!concurrencyStamp.HasValue || task.ConcurrencyStamp != concurrencyStamp.Value)
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return (false, "Vui lòng nhập lý do khôi phục, tối đa 500 ký tự.");
        if (task.PercentComplete >= 100)
            return (false, "Không thể khôi phục công việc đã hủy ở mức 100% vì trạng thái đang thực hiện phải dưới 100%.");
        if (status is not TaskStatusModel target || task.PercentComplete < 0 ||
            (task.PercentComplete == 0 && target != TaskStatusModel.Pending) ||
            (task.PercentComplete > 0 && target != TaskStatusModel.InProgress))
            return (false, "Chọn Chờ xử lý nếu tiến độ là 0%, hoặc Đang thực hiện nếu tiến độ trên 0% và dưới 100%.");
        if (delayReason?.Trim().Length > 500)
            return (false, "Lý do chậm không được vượt quá 500 ký tự.");
        if (task.PlannedEndDate.Date < DateTime.Today && (delayReason?.Trim().Length ?? 0) < 10)
            return (false, "Công việc quá hạn phải có lý do chậm từ 10 đến 500 ký tự.");

        // Restoration preserves existing dependencies; subsequent progress changes remain blocked.
        new TaskAuditService(dbContext).Add(task, userId, "Reopened", reason: reason.Trim());
        new TaskLifecycle(dbContext).Apply(task, target, task.PercentComplete, userId, delayReason, note: reason);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        }
        return (true, null);
    }

    public async Task<IReadOnlyCollection<ProgressChartPoint>> GetChartAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default,
        ProgressFilterViewModel? filter = null)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return [];
        var tasks = await Filter(VisibleQuery(access), filter ?? new ProgressFilterViewModel())
            .ToListAsync(cancellationToken);

        return tasks
            .GroupBy(task => task.Project.Name)
            .Select(group => new ProgressChartPoint(
                group.Key,
                group.Average(task => CalculatePlannedPercent(task, DateTime.UtcNow.Date)),
                group.Average(task => task.PercentComplete)))
            .ToList();
    }

    private IQueryable<TaskEntity> Filter(IQueryable<TaskEntity> query, ProgressFilterViewModel filter)
    {
        if (filter.ProjectId.HasValue) query = query.Where(task => task.ProjectId == filter.ProjectId.Value);
        if (filter.WorkItemId.HasValue) query = query.Where(task => task.WorkItemId == filter.WorkItemId.Value);
        if (filter.Status.HasValue) query = query.Where(task => task.Status == filter.Status.Value);
        if (filter.Priority.HasValue) query = query.Where(task => task.Priority == filter.Priority.Value);
        if (filter.OnlyOverdue)
        {
            var today = DateTime.Today;
            query = query.Where(task => task.PlannedEndDate < today && task.PercentComplete < 100 &&
                task.Status != TaskStatusModel.Completed && task.Status != TaskStatusModel.Cancelled);
        }
        if (filter.OnlyBlocked)
            query = query.Where(task => dbContext.TaskDependencies.Any(item =>
                item.TaskId == task.Id && item.PredecessorTask.Status != TaskStatusModel.Completed));
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(task => task.Title.Contains(keyword) ||
                (task.Description != null && task.Description.Contains(keyword)));
        }
        return query;
    }

    private IQueryable<TaskEntity> VisibleQuery(TaskAccess access)
    {
        var projectIds = access.ProjectIds;
        var query = dbContext.Tasks
            .Include(task => task.Project)
            .Include(task => task.Assignments)
            .Where(task => projectIds.Contains(task.ProjectId));
        if (access.Roles.Contains("Worker", StringComparer.Ordinal) &&
            !access.Roles.Any(role => role != "Worker" && RoleNames.All.Contains(role, StringComparer.Ordinal)))
            query = query.Where(task => task.Assignments.Any(assignment => assignment.UserId == access.UserId));
        return query;
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
