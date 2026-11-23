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
        CancellationToken cancellationToken = default)
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

        task.Title = task.Title.Trim();
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        task.Assignments = BuildAssignments(assigneeIds);
        task.ActualStartDate = null;
        task.ActualEndDate = null;
        new TaskLifecycle(dbContext).Apply(task, task.Status, task.PercentComplete, userId);
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
        CancellationToken cancellationToken = default)
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

        existing.WorkItemId = task.WorkItemId;
        existing.ParentTaskId = task.ParentTaskId;
        existing.Title = task.Title.Trim();
        existing.Description = task.Description?.Trim();
        existing.Priority = task.Priority;
        existing.PlannedStartDate = task.PlannedStartDate;
        existing.PlannedEndDate = task.PlannedEndDate;
        existing.RequiresInspection = task.RequiresInspection;
        new TaskLifecycle(dbContext).Apply(existing, task.Status, task.PercentComplete, userId);

        var assignees = assigneeIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        dbContext.TaskAssignments.RemoveRange(
            existing.Assignments.Where(assignment => !assignees.Contains(assignment.UserId)));
        var existingAssignees = existing.Assignments.Select(assignment => assignment.UserId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var assignee in assignees.Where(id => !existingAssignees.Contains(id)))
        {
            existing.Assignments.Add(new TaskAssignment { UserId = assignee });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
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

        var lifecycleError = await new TaskLifecycle(dbContext).ValidateAsync(
            existingTaskId, task.RequiresInspection, task.Status, task.PercentComplete, cancellationToken);
        if (lifecycleError is not null) return lifecycleError;

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
