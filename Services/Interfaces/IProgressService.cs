using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.ViewModels;
using Task = System.Threading.Tasks.Task;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.Services.Interfaces;

public interface IProgressService
{
    Task<ProgressIndexViewModel> GetOverviewAsync(
        ProgressFilterViewModel filter, TaskAccess access, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TaskEntity>> GetVisibleTasksAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? Error)> UpdateAsync(
        int taskId,
        decimal percentComplete,
        TaskStatusModel status,
        string? delayReason,
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default,
        Guid? concurrencyStamp = null,
        string? reason = null,
        bool reopen = false);

    Task<(bool Succeeded, string? Error)> RestoreCancelledAsync(
        int taskId, TaskStatusModel? status, string? reason, string? delayReason, Guid? concurrencyStamp,
        string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ProgressChartPoint>> GetChartAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default,
        ProgressFilterViewModel? filter = null);
}

public sealed record ProgressChartPoint(string Label, decimal Planned, decimal Actual);
