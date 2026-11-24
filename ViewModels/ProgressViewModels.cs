using System.ComponentModel.DataAnnotations;
using ConstructionProjectManagement.Models;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatusModel = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.ViewModels;

public sealed class ProgressIndexViewModel
{
    public required IReadOnlyCollection<TaskEntity> Tasks { get; init; }
    public required IReadOnlySet<int> UpdatableTaskIds { get; init; }
    public required IReadOnlySet<int> CancelableTaskIds { get; init; }
    public required ProgressFilterViewModel Filter { get; init; }
    public required IReadOnlyCollection<Project> Projects { get; init; }
    public required IReadOnlyCollection<WorkItem> WorkItems { get; init; }
    public required IReadOnlySet<int> BlockerCommentTaskIds { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}

public sealed class ProgressFilterViewModel
{
    public int? ProjectId { get; set; }
    public int? WorkItemId { get; set; }
    public TaskStatusModel? Status { get; set; }
    public TaskPriority? Priority { get; set; }
    public bool OnlyOverdue { get; set; }
    public bool OnlyBlocked { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class ProgressUpdateViewModel
{
    public int TaskId { get; set; }

    public Guid? ConcurrencyStamp { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public bool Reopen { get; set; }

    [Range(0, 100, ErrorMessage = "Phần trăm phải từ 0 đến 100.")]
    [Display(Name = "Phần trăm hoàn thành")]
    public decimal PercentComplete { get; set; }

    [Display(Name = "Trạng thái")]
    public TaskStatusModel Status { get; set; }

    [Display(Name = "Nguyên nhân chậm tiến độ")]
    public string? DelayReason { get; set; }
}

public sealed class RestoreCancelledTaskViewModel
{
    public int TaskId { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
    public TaskStatusModel? Status { get; set; }
    [MaxLength(500)]
    public string? Reason { get; set; }
    [MaxLength(500)]
    public string? DelayReason { get; set; }
    public bool ReturnToDetails { get; set; }
}
