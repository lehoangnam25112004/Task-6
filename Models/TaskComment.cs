using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public class TaskComment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public Task Task { get; set; } = null!;
    public required string AuthorId { get; set; }
    public ApplicationUser Author { get; set; } = null!;
    [MaxLength(2000)]
    public required string Content { get; set; }
    public bool IsBlocker { get; set; }
    public decimal ProgressSnapshot { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
