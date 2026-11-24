using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using TaskEntity = ConstructionProjectManagement.Models.Task;

namespace ConstructionProjectManagement.Services;

public sealed class TaskAuditService(ApplicationDbContext db)
{
    public void Add(TaskEntity task, string actorId, string action, string? field = null,
        string? oldValue = null, string? newValue = null, string? reason = null)
    {
        db.TaskAuditLogs.Add(new TaskAuditLog
        {
            Task = task, PerformedById = actorId, Action = action, FieldName = field,
            OldValue = oldValue, NewValue = newValue, Reason = reason
        });
    }
}
