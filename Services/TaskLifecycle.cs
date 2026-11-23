using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services;

public sealed class TaskLifecycle(ApplicationDbContext db)
{
    public async Task<string?> ValidateAsync(
        int? taskId, bool requiresInspection, TaskStatusModel status, decimal percent,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status) || percent is < 0 or > 100)
            return "Trạng thái hoặc phần trăm hoàn thành không hợp lệ.";
        if ((status == TaskStatusModel.Pending && percent != 0) ||
            (status == TaskStatusModel.InProgress && percent >= 100) ||
            (status == TaskStatusModel.Completed && percent != 100))
            return "Trạng thái và phần trăm hoàn thành không phù hợp.";
        if (requiresInspection && percent == 100)
        {
            var latest = taskId.HasValue
                ? await db.InspectionRequests.AsNoTracking()
                    .Where(item => item.TaskId == taskId.Value)
                    .OrderByDescending(item => item.RequestedAt).ThenByDescending(item => item.Id)
                    .Select(item => (InspectionResult?)item.Result).FirstOrDefaultAsync(cancellationToken)
                : null;
            if (latest != InspectionResult.Passed)
                return "Công việc cần kết quả nghiệm thu đạt mới nhất trước khi hoàn thành 100%.";
        }
        return null;
    }

    public void Apply(TaskEntity task, TaskStatusModel status, decimal percent, string userId, string? delayReason = null)
    {
        var now = DateTime.UtcNow;
        if (task.Status != status || task.PercentComplete != percent)
        {
            db.TaskProgressHistories.Add(new TaskProgressHistory
            {
                TaskId = task.Id,
                PreviousStatus = task.Status,
                NewStatus = status,
                PreviousPercentComplete = task.PercentComplete,
                NewPercentComplete = percent,
                DelayReason = delayReason?.Trim(),
                UpdatedById = userId,
                UpdatedAt = now
            });
        }
        task.Status = status;
        task.PercentComplete = percent;
        task.UpdatedAt = now;
        if (percent > 0 && !task.ActualStartDate.HasValue)
            task.ActualStartDate = now;
        if (percent == 100)
            task.ActualEndDate ??= now;
        else
            task.ActualEndDate = null;
    }
}
