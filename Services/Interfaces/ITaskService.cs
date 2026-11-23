using ConstructionProjectManagement.Models;
using TaskEntity = ConstructionProjectManagement.Models.Task;

namespace ConstructionProjectManagement.Services.Interfaces;

public interface ITaskService
{
    System.Threading.Tasks.Task<IReadOnlyCollection<TaskEntity>> GetVisibleAsync(
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    System.Threading.Tasks.Task<TaskEntity?> GetByIdAsync(
        int id,
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    System.Threading.Tasks.Task<(bool Succeeded, string? Error, TaskEntity? Task)> CreateAsync(
        TaskEntity task,
        IEnumerable<string> assigneeIds,
        string userId,
        bool isAdmin,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    System.Threading.Tasks.Task<(bool Succeeded, string? Error)> UpdateAsync(
        TaskEntity task,
        IEnumerable<string> assigneeIds,
        string userId,
        bool isAdmin,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);
}
