using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services;

public sealed class TaskService(
    ApplicationDbContext dbContext,
    IWbsService wbsService) : ITaskService
{
    public async System.Threading.Tasks.Task<IReadOnlyCollection<TaskEntity>> GetVisibleAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null) return [];
        return await VisibleQuery(access)
            .OrderBy(task => task.PlannedEndDate)
            .ToListAsync(cancellationToken);
    }

    public async System.Threading.Tasks.Task<TaskEntity?> GetByIdAsync(
        int id,
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        return access is null ? null : await VisibleQuery(access)
            .SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
    }

    public async System.Threading.Tasks.Task<(bool Succeeded, string? Error, TaskEntity? Task)> CreateAsync(
        TaskEntity task,
        IEnumerable<string> assigneeIds,
        string userId,
        bool isAdmin,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default,
        string? delayReason = null)
    {
        var validationError = await ValidateAsync(
            task,
            assigneeIds,
            userId,
            isAdmin,
            roles,
            existingTaskId: null,
            existingProjectId: null,
            existingWorkItemId: null,
            cancellationToken);
        if (validationError is not null)
        {
            return (false, validationError, null);
        }
        var lifecycle = new TaskLifecycle(dbContext);
        validationError = await lifecycle.ValidateChangeAsync(task, task.Status, task.PercentComplete,
            assigneeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count(),
            delayReason, task.CancelReason, creation: true, cancellationToken: cancellationToken);
        if (validationError is not null) return (false, validationError, null);

        task.Title = task.Title.Trim();
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        task.Assignments = BuildAssignments(assigneeIds);
        task.ActualStartDate = null;
        task.ActualEndDate = null;
        lifecycle.Apply(task, task.Status, task.PercentComplete, userId, delayReason, recordInitial: true);
        var audit = new TaskAuditService(dbContext);
        audit.Add(task, userId, "Created");
        foreach (var assignment in task.Assignments)
            audit.Add(task, userId, "AssigneeChanged", "UserId", newValue: assignment.UserId);
        if (task.WorkItemId.HasValue)
            audit.Add(task, userId, "WbsChanged", nameof(task.WorkItemId), newValue: task.WorkItemId.ToString());
        if (task.ParentTaskId.HasValue)
            audit.Add(task, userId, "ParentChanged", nameof(task.ParentTaskId), newValue: task.ParentTaskId.ToString());
        dbContext.Tasks.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null, task);
    }

    public async System.Threading.Tasks.Task<(bool Succeeded, string? Error)> UpdateAsync(
        TaskEntity task,
        IEnumerable<string> assigneeIds,
        string userId,
        bool isAdmin,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default,
        string? delayReason = null,
        string? scheduleReason = null)
    {
        var existing = await dbContext.Tasks
            .Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == task.Id, cancellationToken);
        if (existing is null)
        {
            return (false, "Không tìm thấy công việc.");
        }
        var validationError = await ValidateAsync(
            task,
            assigneeIds,
            userId,
            isAdmin,
            roles,
            existing.Id,
            existing.ProjectId,
            existing.WorkItemId,
            cancellationToken);
        if (validationError is not null)
        {
            return (false, validationError);
        }
        if (existing.ConcurrencyStamp != task.ConcurrencyStamp)
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        var proposedStatus = task.Status == TaskStatusModel.InProgress && task.PercentComplete == 100 && !task.RequiresInspection
            ? TaskStatusModel.Completed : task.Status;
        var scheduleChanged = existing.PlannedStartDate != task.PlannedStartDate || existing.PlannedEndDate != task.PlannedEndDate;
        if (existing.PercentComplete > 0 && scheduleChanged &&
            (string.IsNullOrWhiteSpace(scheduleReason) || scheduleReason.Trim().Length > 500))
            return (false, "Vui lòng nhập lý do điều chỉnh kế hoạch, tối đa 500 ký tự.");
        var validationTask = new TaskEntity
        {
            Id = existing.Id, ProjectId = existing.ProjectId, Title = existing.Title,
            Status = existing.Status, PercentComplete = existing.PercentComplete,
            PlannedEndDate = task.PlannedEndDate, RequiresInspection = task.RequiresInspection
        };
        var createInspection = proposedStatus == TaskStatusModel.PendingInspection &&
            existing.Status != TaskStatusModel.PendingInspection;
        if (createInspection && await dbContext.InspectionRequests.AnyAsync(
            item => item.TaskId == existing.Id && item.Result == InspectionResult.Pending, cancellationToken))
            return (false, "Công việc đã có yêu cầu nghiệm thu đang chờ xử lý.");
        validationError = await new TaskLifecycle(dbContext).ValidateChangeAsync(validationTask, proposedStatus,
            task.PercentComplete, assigneeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count(),
            delayReason, task.CancelReason, inspectionRequested: createInspection, cancellationToken: cancellationToken);
        if (validationError is not null) return (false, validationError);

        if (createInspection)
            dbContext.InspectionRequests.Add(new InspectionRequest
            {
                ProjectId = existing.ProjectId, TaskId = existing.Id, RequestedById = userId
            });
        var audit = new TaskAuditService(dbContext);
        void Change(string action, string field, string? before, string? after)
        {
            if (before != after) audit.Add(existing, userId, action, field, before, after,
                action == "ScheduleUpdated" ? scheduleReason?.Trim() : null);
        }
        Change("MetadataUpdated", nameof(task.Title), existing.Title, task.Title.Trim());
        Change("MetadataUpdated", nameof(task.Description), existing.Description, task.Description?.Trim());
        Change("MetadataUpdated", nameof(task.Priority), existing.Priority.ToString(), task.Priority.ToString());
        Change("WbsChanged", nameof(task.WorkItemId), existing.WorkItemId?.ToString(), task.WorkItemId?.ToString());
        Change("ParentChanged", nameof(task.ParentTaskId), existing.ParentTaskId?.ToString(), task.ParentTaskId?.ToString());
        AuditScheduleChanges(existing, task.PlannedStartDate, task.PlannedEndDate, userId, scheduleReason);
        Change("MetadataUpdated", nameof(task.RequiresInspection), existing.RequiresInspection.ToString(), task.RequiresInspection.ToString());

        existing.WorkItemId = task.WorkItemId;
        existing.ParentTaskId = task.ParentTaskId;
        existing.Title = task.Title.Trim();
        existing.Description = task.Description?.Trim();
        existing.Priority = task.Priority;
        existing.PlannedStartDate = task.PlannedStartDate;
        existing.PlannedEndDate = task.PlannedEndDate;
        existing.RequiresInspection = task.RequiresInspection;
        if (proposedStatus == TaskStatusModel.Cancelled)
        {
            existing.CancelReason = task.CancelReason?.Trim();
            audit.Add(existing, userId, "Cancelled", reason: existing.CancelReason);
        }
        new TaskLifecycle(dbContext).Apply(existing, proposedStatus, task.PercentComplete, userId, delayReason,
            note: proposedStatus == TaskStatusModel.Cancelled ? existing.CancelReason : null);

        var assignees = assigneeIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        dbContext.TaskAssignments.RemoveRange(
            existing.Assignments.Where(assignment => !assignees.Contains(assignment.UserId)));
        var existingAssignees = existing.Assignments.Select(assignment => assignment.UserId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var removed in existingAssignees.Except(assignees))
            audit.Add(existing, userId, "AssigneeChanged", "UserId", oldValue: removed);
        foreach (var added in assignees.Except(existingAssignees))
            audit.Add(existing, userId, "AssigneeChanged", "UserId", newValue: added);
        foreach (var assignee in assignees.Where(id => !existingAssignees.Contains(id)))
        {
            existing.Assignments.Add(new TaskAssignment { UserId = assignee });
        }
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        }
        return (true, null);
    }

    public async System.Threading.Tasks.Task<(bool Succeeded, string? Error)> AdjustScheduleAsync(
        int taskId, DateTime plannedStartDate, DateTime plannedEndDate, Guid concurrencyStamp,
        string? reason, string userId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        var task = await dbContext.Tasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (access is null || task is null || !access.CanAdjustSchedule(task))
            return (false, "Không tìm thấy công việc hoặc bạn không có quyền điều chỉnh kế hoạch.");
        if (task.ConcurrencyStamp != concurrencyStamp)
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        if (plannedStartDate == default || plannedEndDate == default || plannedEndDate < plannedStartDate)
            return (false, "Ngày kết thúc không được trước ngày bắt đầu và cả hai ngày đều bắt buộc.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return (false, "Vui lòng nhập lý do điều chỉnh kế hoạch, tối đa 500 ký tự.");
        if (task.PlannedStartDate == plannedStartDate && task.PlannedEndDate == plannedEndDate)
            return (false, "Ngày kế hoạch chưa thay đổi.");

        AuditScheduleChanges(task, plannedStartDate, plannedEndDate, userId, reason);
        task.PlannedStartDate = plannedStartDate;
        task.PlannedEndDate = plannedEndDate;
        task.UpdatedAt = DateTime.UtcNow;
        task.ConcurrencyStamp = Guid.NewGuid();
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "Công việc vừa được người khác cập nhật. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.");
        }
        return (true, null);
    }

    private void AuditScheduleChanges(TaskEntity task, DateTime start, DateTime end, string userId, string? reason)
    {
        var audit = new TaskAuditService(dbContext);
        if (task.PlannedStartDate != start)
            audit.Add(task, userId, "ScheduleUpdated", nameof(task.PlannedStartDate),
                task.PlannedStartDate.ToString("O"), start.ToString("O"), reason?.Trim());
        if (task.PlannedEndDate != end)
            audit.Add(task, userId, "ScheduleUpdated", nameof(task.PlannedEndDate),
                task.PlannedEndDate.ToString("O"), end.ToString("O"), reason?.Trim());
    }

    private IQueryable<TaskEntity> VisibleQuery(TaskAccess access)
    {
        var projectIds = access.ProjectIds;
        return dbContext.Tasks
            .AsNoTracking()
            .Include(task => task.Project)
            .Include(task => task.WorkItem)
            .Include(task => task.Assignments)
                .ThenInclude(assignment => assignment.User)
            .Where(task => projectIds.Contains(task.ProjectId));
    }

    private async Task<string?> ValidateAsync(
        TaskEntity task,
        IEnumerable<string> assigneeIds,
        string userId,
        bool isAdmin,
        IReadOnlyCollection<string> roles,
        int? existingTaskId,
        int? existingProjectId,
        int? existingWorkItemId,
        CancellationToken cancellationToken)
    {
        if (existingProjectId.HasValue && task.ProjectId != existingProjectId.Value)
        {
            return "Không được thay đổi dự án của công việc.";
        }

        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null || !access.CanEditTask(task.ProjectId))
            return "Bạn không có quyền ghi trên dự án này.";

        if (task.PlannedEndDate < task.PlannedStartDate)
        {
            return "Ngày kết thúc không được trước ngày bắt đầu.";
        }

        if (!Enum.IsDefined(task.Priority))
            return "Mức ưu tiên không hợp lệ.";

        var project = await dbContext.Projects
            .Include(item => item.Members)
            .SingleOrDefaultAsync(item => item.Id == task.ProjectId, cancellationToken);
        if (project is null)
        {
            return "Dự án không hợp lệ.";
        }

        if (existingTaskId.HasValue && !task.RequiresInspection &&
            await dbContext.Tasks.AnyAsync(
                item => item.Id == existingTaskId.Value && item.RequiresInspection, cancellationToken) &&
            await dbContext.InspectionRequests.AnyAsync(
                item => item.TaskId == existingTaskId.Value, cancellationToken))
        {
            return "Không thể bỏ yêu cầu nghiệm thu sau khi đã tạo yêu cầu nghiệm thu.";
        }

        if (task.WorkItemId.HasValue)
        {
            var workItem = await dbContext.WorkItems
                .SingleOrDefaultAsync(item => item.Id == task.WorkItemId, cancellationToken);
            if (workItem is null || workItem.ProjectId != task.ProjectId)
            {
                return "WBS phải thuộc cùng dự án.";
            }

            if (task.WorkItemId != existingWorkItemId)
            {
                var linkResult = await wbsService.CanLinkWorkItemToTaskAsync(
                    task.ProjectId,
                    task.WorkItemId,
                    existingProjectId == task.ProjectId ? existingTaskId : null,
                    userId,
                    roles,
                    cancellationToken);
                if (!linkResult.Succeeded)
                {
                    return linkResult.Error ?? "Bạn không có quyền gắn hạng mục WBS này.";
                }
            }
        }
        else if (existingWorkItemId.HasValue)
        {
            var linkResult = await wbsService.CanLinkWorkItemToTaskAsync(
                task.ProjectId,
                null,
                existingProjectId == task.ProjectId ? existingTaskId : null,
                userId,
                roles,
                cancellationToken);
            if (!linkResult.Succeeded)
            {
                return linkResult.Error ?? "Bạn không có quyền thay đổi liên kết WBS.";
            }
        }

        if (task.ParentTaskId.HasValue)
        {
            var nextParentId = task.ParentTaskId;
            var visited = new HashSet<int>();
            while (nextParentId.HasValue)
            {
                if (nextParentId == existingTaskId || !visited.Add(nextParentId.Value))
                {
                    return "Công việc cha không được tạo vòng lặp.";
                }

                var parent = await dbContext.Tasks.AsNoTracking()
                    .Where(item => item.Id == nextParentId.Value)
                    .Select(item => new { item.ProjectId, item.ParentTaskId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (parent is null || parent.ProjectId != task.ProjectId)
                {
                    return "Công việc cha phải thuộc cùng dự án.";
                }

                nextParentId = parent.ParentTaskId;
            }
        }

        var ids = assigneeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var activeMemberIds = dbContext.ProjectMembers
            .Where(member => member.ProjectId == project.Id && member.IsActive)
            .Select(member => member.UserId);
        var eligibleIds = await dbContext.Users
            .Where(user => ids.Contains(user.Id) &&
                           user.IsActive &&
                           (user.Id == project.ProjectManagerId ||
                            activeMemberIds.Contains(user.Id)))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        return eligibleIds.Count == ids.Count
            ? null
            : "Chỉ được phân công nhân sự đang hoạt động và thuộc dự án.";
    }

    private static List<TaskAssignment> BuildAssignments(IEnumerable<string> assigneeIds)
    {
        return assigneeIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Select(id => new TaskAssignment { UserId = id })
            .ToList();
    }
}
