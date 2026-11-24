using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public class TaskDependency
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public Task Task { get; set; } = null!;

    public int PredecessorTaskId { get; set; }

    public Task PredecessorTask { get; set; } = null!;

    [MaxLength(20)]
    public string DependencyType { get; set; } = "FS";

    public string? CreatedById { get; set; }

    public ApplicationUser? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
