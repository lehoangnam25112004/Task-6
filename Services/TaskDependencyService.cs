using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace ConstructionProjectManagement.Services;

public sealed class TaskDependencyService(ApplicationDbContext db)
{
    public async Task<(bool Succeeded, string? Error)> AddAsync(
        int taskId, int predecessorId, string userId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var access = await TaskAccess.LoadAsync(db, userId, roles, cancellationToken);
        var task = await db.Tasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (access is null || task is null || !access.CanManageDependencies(task.ProjectId))
            return (false, "Bạn không có quyền quản lý phụ thuộc của công việc này.");
        if (task.Status is Models.TaskStatus.Completed or Models.TaskStatus.Cancelled)
            return (false, "Không thể thay đổi phụ thuộc của công việc đã hoàn thành hoặc hủy.");
        if (taskId == predecessorId)
            return (false, "Công việc không thể phụ thuộc chính nó.");
        var predecessor = await db.Tasks.SingleOrDefaultAsync(item => item.Id == predecessorId, cancellationToken);
        if (predecessor is null || predecessor.ProjectId != task.ProjectId)
            return (false, "Công việc tiền nhiệm phải thuộc cùng dự án.");
        var edges = await db.TaskDependencies.AsNoTracking()
            .Where(item => item.Task.ProjectId == task.ProjectId)
            .Select(item => new { item.TaskId, item.PredecessorTaskId }).ToListAsync(cancellationToken);
        if (edges.Any(edge => edge.TaskId == taskId && edge.PredecessorTaskId == predecessorId))
            return (false, "Phụ thuộc này đã tồn tại.");
        var pending = new Stack<int>();
        var visited = new HashSet<int>();
        pending.Push(predecessorId);
        while (pending.TryPop(out var current))
        {
            if (current == taskId) return (false, "Phụ thuộc tạo vòng lặp.");
            if (!visited.Add(current)) continue;
            foreach (var edge in edges.Where(edge => edge.TaskId == current))
                pending.Push(edge.PredecessorTaskId);
        }
        db.TaskDependencies.Add(new TaskDependency
        {
            TaskId = taskId, PredecessorTaskId = predecessorId, CreatedById = userId
        });
        new TaskAuditService(db).Add(task, userId, "DependencyAdded", "PredecessorTaskId", newValue: predecessorId.ToString());
        task.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> RemoveAsync(
        int dependencyId, string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var dependency = await db.TaskDependencies.Include(item => item.Task)
            .SingleOrDefaultAsync(item => item.Id == dependencyId, cancellationToken);
        var access = await TaskAccess.LoadAsync(db, userId, roles, cancellationToken);
        if (dependency is null || access is null || !access.CanManageDependencies(dependency.Task.ProjectId))
            return (false, "Bạn không có quyền quản lý phụ thuộc của công việc này.");
        if (dependency.Task.Status is Models.TaskStatus.Completed or Models.TaskStatus.Cancelled)
            return (false, "Không thể thay đổi phụ thuộc của công việc đã hoàn thành hoặc hủy.");
        new TaskAuditService(db).Add(dependency.Task, userId, "DependencyRemoved",
            "PredecessorTaskId", oldValue: dependency.PredecessorTaskId.ToString());
        dependency.Task.ConcurrencyStamp = Guid.NewGuid();
        db.TaskDependencies.Remove(dependency);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
