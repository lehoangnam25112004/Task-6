using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace ConstructionProjectManagement.Services;

public sealed class TaskCommentService(ApplicationDbContext db)
{
    public async Task<(bool Succeeded, string? Error)> AddAsync(
        int taskId, string? content, bool isBlocker, string userId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(db, userId, roles, cancellationToken);
        var task = await db.Tasks.Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (access is null || task is null || !access.CanComment(task))
            return (false, "Bạn không có quyền bình luận về công việc này.");
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000)
            return (false, "Nội dung bình luận phải có từ 1 đến 2000 ký tự.");
        db.TaskComments.Add(new TaskComment
        {
            Task = task, AuthorId = userId, Content = text, IsBlocker = isBlocker,
            ProgressSnapshot = task.PercentComplete
        });
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> EditAsync(
        int id, string? content, string userId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var comment = await db.TaskComments.Include(item => item.Task).ThenInclude(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var access = await TaskAccess.LoadAsync(db, userId, roles, cancellationToken);
        if (comment is null || access is null || !access.CanComment(comment.Task) ||
            !(roles.Contains(RoleNames.Admin, StringComparer.Ordinal) ||
              comment.AuthorId == userId && DateTime.UtcNow <= comment.CreatedAt.AddMinutes(15)))
            return (false, "Bạn không có quyền sửa bình luận này.");
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000)
            return (false, "Nội dung bình luận phải có từ 1 đến 2000 ký tự.");
        comment.Content = text;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> ResolveBlockerAsync(
        int taskId, int commentId, string userId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var comment = await db.TaskComments.Include(item => item.Task).ThenInclude(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == commentId && item.TaskId == taskId, cancellationToken);
        var access = await TaskAccess.LoadAsync(db, userId, roles, cancellationToken);
        if (comment is null || access is null || !access.CanResolveBlocker(comment.Task, comment.AuthorId))
            return (false, "Bạn không có quyền giải quyết vướng mắc này.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await db.TaskComments.Where(item => item.Id == commentId && item.TaskId == taskId && item.IsBlocker)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsBlocker, false), cancellationToken);
        if (updated == 0) return (false, "Bình luận này không còn được đánh dấu vướng mắc.");
        await db.Entry(comment).ReloadAsync(cancellationToken);
        new TaskAuditService(db).Add(comment.Task, userId, "BlockerResolved", nameof(TaskComment.IsBlocker),
            bool.TrueString, bool.FalseString, $"Bình luận #{commentId}");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, null);
    }
}
