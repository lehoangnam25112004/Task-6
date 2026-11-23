using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services.Interfaces;
using ConstructionProjectManagement.ViewModels;
using Microsoft.EntityFrameworkCore;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services;

public sealed class QualityService(ApplicationDbContext dbContext) : IQualityService
{
    public async Task<(bool Succeeded, string? Error)> CreateIncidentAsync(
        IncidentCreateViewModel model, string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null || !access.CanWriteQuality(model.ProjectId))
            return (false, "Bạn không có quyền ghi trên dự án này.");
        var error = await ValidateProjectTaskAsync(model.ProjectId, model.TaskId, cancellationToken);
        if (error is not null) return (false, error);
        dbContext.Incidents.Add(new Incident
        {
            ProjectId = model.ProjectId, TaskId = model.TaskId, Title = model.Title.Trim(),
            Description = model.Description.Trim(), Priority = model.Priority,
            AssignedToId = model.AssignedToId, ReportedById = userId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> CreateRfiAsync(
        RfiCreateViewModel model, string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null || !access.CanWriteQuality(model.ProjectId))
            return (false, "Bạn không có quyền ghi trên dự án này.");
        var error = await ValidateProjectTaskAsync(model.ProjectId, model.TaskId, cancellationToken);
        if (error is not null) return (false, error);
        dbContext.RfiRequests.Add(new RfiRequest
        {
            ProjectId = model.ProjectId, TaskId = model.TaskId, Subject = model.Subject.Trim(),
            Question = model.Question.Trim(), CreatedById = userId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> RespondRfiAsync(
        int id, RfiRespondViewModel model, string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var rfi = await dbContext.RfiRequests.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rfi is null || rfi.Status == RfiStatus.Closed) return (false, "RFI không tồn tại hoặc đã đóng.");
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null || !access.CanWriteQuality(rfi.ProjectId)) return (false, "Bạn không có quyền ghi trên dự án này.");
        rfi.Response = model.Response.Trim();
        rfi.Status = RfiStatus.Answered;
        rfi.RespondedById = userId;
        rfi.RespondedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> CreateInspectionAsync(
        InspectionCreateViewModel model, string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var task = await dbContext.Tasks.SingleOrDefaultAsync(item => item.Id == model.TaskId, cancellationToken);
        if (task is null || !task.RequiresInspection || task.Status == TaskStatusModel.Cancelled)
            return (false, "Công việc không hợp lệ hoặc không yêu cầu nghiệm thu.");
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null || !access.CanWriteQuality(task.ProjectId))
            return (false, "Bạn không có quyền ghi trên dự án này.");
        if (task.PercentComplete == 100 || task.Status == TaskStatusModel.Completed)
            return (false, "Hãy hạ phần trăm công việc xuống dưới 100 trước khi yêu cầu nghiệm thu mới.");
        var pending = await dbContext.InspectionRequests.AnyAsync(
            item => item.TaskId == model.TaskId && item.Result == InspectionResult.Pending, cancellationToken);
        if (pending) return (false, "Công việc đã có yêu cầu nghiệm thu đang chờ xử lý.");
        dbContext.InspectionRequests.Add(new InspectionRequest
        {
            ProjectId = task.ProjectId, TaskId = task.Id, RequestedById = userId
        });
        new TaskLifecycle(dbContext).Apply(task, TaskStatusModel.PendingInspection, task.PercentComplete, userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> RecordInspectionAsync(
        int id, InspectionResultViewModel model, string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        if (model.Result is not (InspectionResult.Passed or InspectionResult.Failed))
            return (false, "Phải chọn kết quả đạt hoặc không đạt.");
        if (model.Result == InspectionResult.Failed
            && string.IsNullOrWhiteSpace(model.CorrectiveAction))
        {
            return (false, "Nghiệm thu không đạt phải có nội dung khắc phục.");
        }
        var inspection = await dbContext.InspectionRequests
            .Include(item => item.Task)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (inspection is null) return (false, "Không tìm thấy yêu cầu nghiệm thu.");
        var access = await TaskAccess.LoadAsync(dbContext, userId, roles, cancellationToken);
        if (access is null || inspection.ProjectId != inspection.Task.ProjectId ||
            !access.CanWriteQuality(inspection.Task.ProjectId))
            return (false, "Bạn không có quyền ghi trên dự án này.");
        if (inspection.Result != InspectionResult.Pending)
            return (false, "Chỉ được ghi kết quả cho yêu cầu đang chờ.");
        var latestId = await dbContext.InspectionRequests.AsNoTracking()
            .Where(item => item.TaskId == inspection.TaskId)
            .OrderByDescending(item => item.RequestedAt).ThenByDescending(item => item.Id)
            .Select(item => item.Id).FirstAsync(cancellationToken);
        if (latestId == inspection.Id && !inspection.Task.RequiresInspection)
            return (false, "Công việc không yêu cầu nghiệm thu.");
        if (latestId == inspection.Id && model.Result == InspectionResult.Failed && inspection.Task.PercentComplete == 100 &&
            (!model.NewPercentComplete.HasValue || model.NewPercentComplete.Value is < 0 or >= 100))
            return (false, "Nhập phần trăm hoàn thành mới từ 0 đến dưới 100 khi nghiệm thu không đạt.");
        inspection.Result = model.Result;
        inspection.ResultNotes = model.Notes?.Trim();
        inspection.CorrectiveAction = model.CorrectiveAction?.Trim();
        inspection.InspectedById = userId;
        inspection.InspectedAt = DateTime.UtcNow;
        inspection.History.Add(new InspectionHistory
        {
            Result = model.Result, Notes = model.Notes?.Trim(), InspectedById = userId
        });
        if (latestId == inspection.Id)
        {
            new TaskLifecycle(dbContext).Apply(
                inspection.Task,
                model.Result == InspectionResult.Passed ? TaskStatusModel.Completed : TaskStatusModel.NeedsRework,
                model.Result == InspectionResult.Passed ? 100 :
                    inspection.Task.PercentComplete == 100 ? model.NewPercentComplete!.Value : inspection.Task.PercentComplete,
                userId);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    private async Task<string?> ValidateProjectTaskAsync(
        int projectId, int? taskId, CancellationToken cancellationToken)
    {
        var projectExists = await dbContext.Projects.AnyAsync(
            project => project.Id == projectId && !project.IsArchived, cancellationToken);
        if (!projectExists) return "Dự án không hợp lệ.";
        if (taskId.HasValue && !await dbContext.Tasks.AnyAsync(
                task => task.Id == taskId && task.ProjectId == projectId, cancellationToken))
            return "Công việc không thuộc dự án.";
        return null;
    }
}
