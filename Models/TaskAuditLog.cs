using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public class TaskAuditLog
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public Task Task { get; set; } = null!;
    [MaxLength(50)]
    public required string Action { get; set; }
    [MaxLength(100)]
    public string? FieldName { get; set; }
    [MaxLength(1000)]
    public string? OldValue { get; set; }
    [MaxLength(1000)]
    public string? NewValue { get; set; }
    [MaxLength(500)]
    public string? Reason { get; set; }
    public required string PerformedById { get; set; }
    public ApplicationUser PerformedBy { get; set; } = null!;
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
}
