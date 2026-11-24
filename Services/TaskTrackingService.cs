using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.ViewModels;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;

namespace ConstructionProjectManagement.Services;

public sealed class TaskTrackingService(ApplicationDbContext db)
{
    public async System.Threading.Tasks.Task<TaskDetailsViewModel> GetDetailsAsync(
        TaskEntity task, TaskAccess access, CancellationToken cancellationToken = default)
    {
        var canView = access.CanViewTrackingTask(task);
        var canManage = canView && access.CanManageDependencies(task.ProjectId) &&
            task.Status is not (Models.TaskStatus.Completed or Models.TaskStatus.Cancelled);
        var comments = canView ? await db.TaskComments.AsNoTracking().Include(item => item.Author)
            .Where(item => item.TaskId == task.Id).OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken) : [];
        var authorIds = comments.Select(item => item.AuthorId).Distinct().ToArray();
        var roles = await (from userRole in db.UserRoles.AsNoTracking()
                           join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                           where authorIds.Contains(userRole.UserId)
                           select new { userRole.UserId, role.Name }).ToListAsync(cancellationToken);
        return new TaskDetailsViewModel
        {
            Task = task,
            CanEdit = access.CanEditTask(task.ProjectId) &&
                task.Status is not (Models.TaskStatus.Completed or Models.TaskStatus.Cancelled),
            CanAdjustSchedule = access.CanAdjustSchedule(task),
            CanRestoreCancelled = canView && access.CanCancelOrReopen(task.ProjectId) &&
                task.Status == Models.TaskStatus.Cancelled,
            CanViewTracking = canView,
            CanViewAudit = canView && access.CanViewAudit,
            CanComment = canView && access.CanComment(task),
            CanManageDependencies = canManage,
            IsAdmin = access.Roles.Contains(RoleNames.Admin, StringComparer.Ordinal),
            UserId = access.UserId,
            History = canView ? await db.TaskProgressHistories.AsNoTracking().Include(item => item.UpdatedBy)
                .Where(item => item.TaskId == task.Id).OrderByDescending(item => item.UpdatedAt).ToListAsync(cancellationToken) : [],
            Audit = canView && access.CanViewAudit ? await db.TaskAuditLogs.AsNoTracking().Include(item => item.PerformedBy)
                .Where(item => item.TaskId == task.Id).OrderByDescending(item => item.PerformedAt).ToListAsync(cancellationToken) : [],
            Comments = comments,
            ResolvableBlockerCommentIds = comments
                .Where(comment => comment.IsBlocker && access.CanResolveBlocker(task, comment.AuthorId))
                .Select(comment => comment.Id).ToHashSet(),
            CommentAuthorRoles = roles.GroupBy(item => item.UserId)
                .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(item => item.Name))),
            Dependencies = canView ? await db.TaskDependencies.AsNoTracking().Include(item => item.PredecessorTask)
                .Where(item => item.TaskId == task.Id).ToListAsync(cancellationToken) : [],
            Predecessors = canManage ? await db.Tasks.AsNoTracking()
                .Where(item => item.ProjectId == task.ProjectId && item.Id != task.Id)
                .OrderBy(item => item.Title).ToListAsync(cancellationToken) : []
        };
    }
}
