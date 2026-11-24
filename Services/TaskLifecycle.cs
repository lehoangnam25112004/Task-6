using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services;

public sealed class TaskLifecycle(ApplicationDbContext db)
{
    public async Task<string?> ValidateChangeAsync(
        TaskEntity task, TaskStatusModel status, decimal percent, int assigneeCount,
        string? delayReason, string? reason, bool creation = false,
        bool inspectionRequested = false, InspectionResult? inspectionResult = null,
        bool reopen = false, bool legacyInspectionCorrection = false,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status) || percent is < 0 or > 100)
            return "Trạng thái hoặc phần trăm hoàn thành không hợp lệ.";
        if (creation && status is not (TaskStatusModel.Pending or TaskStatusModel.Assigned))
            return "Công việc mới phải ở trạng thái chờ xử lý hoặc đã phân công.";
        if (!creation && task.Status is (TaskStatusModel.Completed or TaskStatusModel.Cancelled) &&
            !reopen && !legacyInspectionCorrection)
            return "Công việc đã hoàn thành hoặc hủy không thể sửa trực tiếp.";
        if (reopen && (task.Status != TaskStatusModel.Completed ||
            status is not (TaskStatusModel.InProgress or TaskStatusModel.NeedsRework)))
            return "Chỉ được mở lại công việc đã hoàn thành về đang thực hiện hoặc cần làm lại.";

        if (!creation && !reopen && !legacyInspectionCorrection && task.Status != status)
        {
            var allowed = task.Status switch
            {
                TaskStatusModel.Pending => status is TaskStatusModel.Assigned or TaskStatusModel.InProgress or TaskStatusModel.Cancelled,
                TaskStatusModel.Assigned => status is TaskStatusModel.InProgress or TaskStatusModel.Cancelled,
                TaskStatusModel.InProgress => status is TaskStatusModel.Completed or TaskStatusModel.PendingInspection or TaskStatusModel.Cancelled,
                TaskStatusModel.PendingInspection => status is TaskStatusModel.Completed or TaskStatusModel.NeedsRework,
                TaskStatusModel.NeedsRework => status is TaskStatusModel.InProgress or TaskStatusModel.PendingInspection or TaskStatusModel.Cancelled,
                _ => false
            };
            if (!allowed) return "Không được chuyển công việc giữa các trạng thái này.";
        }

        if (status == TaskStatusModel.Pending && percent != 0 ||
            status == TaskStatusModel.Assigned && (percent != 0 || assigneeCount == 0) ||
            status == TaskStatusModel.InProgress && (percent <= 0 || percent >= 100) ||
            status == TaskStatusModel.PendingInspection && (percent != 100 || !task.RequiresInspection) ||
            status == TaskStatusModel.NeedsRework && percent >= 100 ||
            status == TaskStatusModel.Completed && percent != 100)
            return "Trạng thái, phần trăm hoàn thành hoặc người phụ trách không phù hợp.";
        if (!creation && task.Status == TaskStatusModel.Pending && status == TaskStatusModel.InProgress && assigneeCount == 0)
            return "Phải phân công người phụ trách trước khi bắt đầu.";
        if (status == TaskStatusModel.Cancelled && (percent != task.PercentComplete || !ValidReason(reason)))
            return "Khi hủy phải giữ nguyên phần trăm và nhập lý do từ 10 đến 500 ký tự.";
        if (reopen && !ValidReason(reason))
            return "Lý do mở lại phải có từ 10 đến 500 ký tự.";
        if (task.PlannedEndDate.Date < DateTime.Today && percent < 100 &&
            status is not (TaskStatusModel.Completed or TaskStatusModel.Cancelled) && !ValidReason(delayReason))
            return "Công việc quá hạn phải có lý do chậm từ 10 đến 500 ký tự.";
        if (delayReason?.Trim().Length > 500)
            return "Lý do chậm không được vượt quá 500 ký tự.";

        if (task.Id != 0 && status != TaskStatusModel.Cancelled &&
            (percent > 0 || status is TaskStatusModel.InProgress or TaskStatusModel.PendingInspection or TaskStatusModel.Completed) &&
            await db.TaskDependencies.AnyAsync(item => item.TaskId == task.Id &&
                item.PredecessorTask.Status != TaskStatusModel.Completed, cancellationToken))
            return "Công việc bị chặn bởi tiền nhiệm chưa hoàn thành.";

        var latest = inspectionResult ?? (task.Id == 0 ? null : await db.InspectionRequests.AsNoTracking()
            .Where(item => item.TaskId == task.Id)
            .OrderByDescending(item => item.RequestedAt).ThenByDescending(item => item.Id)
            .Select(item => (InspectionResult?)item.Result).FirstOrDefaultAsync(cancellationToken));
        if (status == TaskStatusModel.PendingInspection && !inspectionRequested && latest != InspectionResult.Pending)
            return "Công việc phải có yêu cầu nghiệm thu đang chờ.";
        if (status == TaskStatusModel.NeedsRework && latest != InspectionResult.Failed)
            return "Công việc cần kết quả nghiệm thu không đạt mới nhất.";
        if (status == TaskStatusModel.Completed && task.RequiresInspection && latest != InspectionResult.Passed)
            return "Công việc cần kết quả nghiệm thu đạt mới nhất trước khi hoàn thành.";
        return null;
    }

    private static bool ValidReason(string? reason) => reason?.Trim().Length is >= 10 and <= 500;

    public void Apply(TaskEntity task, TaskStatusModel status, decimal percent, string userId,
        string? delayReason = null, bool recordInitial = false, string? note = null)
    {
        var now = DateTime.UtcNow;
        if (recordInitial || task.Status != status || task.PercentComplete != percent)
        {
            if (!recordInitial && task.Status != status)
                new TaskAuditService(db).Add(task, userId, "StatusChanged", nameof(task.Status),
                    task.Status.ToString(), status.ToString(), note?.Trim());
            if (!recordInitial && task.PercentComplete != percent)
                new TaskAuditService(db).Add(task, userId, "ProgressUpdated", nameof(task.PercentComplete),
                    task.PercentComplete.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    percent.ToString(System.Globalization.CultureInfo.InvariantCulture), note?.Trim());
            db.TaskProgressHistories.Add(new TaskProgressHistory
            {
                Task = task,
                PreviousStatus = task.Status,
                NewStatus = status,
                PreviousPercentComplete = task.PercentComplete,
                NewPercentComplete = percent,
                DelayReason = delayReason?.Trim(),
                Note = note?.Trim(),
                UpdatedById = userId,
                UpdatedAt = now
            });
        }
        task.Status = status;
        task.PercentComplete = percent;
        task.UpdatedAt = now;
        task.ConcurrencyStamp = Guid.NewGuid();
        if (status != TaskStatusModel.Cancelled)
        {
            if (percent > 0 && !task.ActualStartDate.HasValue)
                task.ActualStartDate = now;
            if (status == TaskStatusModel.Completed)
                task.ActualEndDate ??= now;
            else
                task.ActualEndDate = null;
            if (status is TaskStatusModel.Pending or TaskStatusModel.Assigned)
                task.ActualStartDate = null;
        }
    }
}
