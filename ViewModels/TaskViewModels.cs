using System.ComponentModel.DataAnnotations;
using ConstructionProjectManagement.Models;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatus = ConstructionProjectManagement.Models.TaskStatus;

namespace ConstructionProjectManagement.ViewModels;

public sealed class TaskEditViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn dự án.")]
    public int ProjectId { get; set; }

    [Display(Name = "WBS")]
    public int? WorkItemId { get; set; }

    [Display(Name = "Công việc cha")]
    public int? ParentTaskId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên công việc.")]
    [MaxLength(200, ErrorMessage = "Tên công việc không được vượt quá 200 ký tự.")]
    [Display(Name = "Tên công việc")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Mô tả")]
    [MaxLength(2000, ErrorMessage = "Mô tả không được vượt quá 2000 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Ưu tiên")]
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    [DataType(DataType.Date)]
    [Display(Name = "Ngày bắt đầu dự kiến")]
    public DateTime PlannedStartDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    [Display(Name = "Ngày kết thúc dự kiến")]
    public DateTime PlannedEndDate { get; set; } = DateTime.Today.AddDays(1);

    [DataType(DataType.Date)]
    [Display(Name = "Ngày bắt đầu thực tế")]
    public DateTime? ActualStartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Ngày kết thúc thực tế")]
    public DateTime? ActualEndDate { get; set; }

    [Display(Name = "Trạng thái")]
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    [Range(0, 100, ErrorMessage = "Phần trăm phải từ 0 đến 100.")]
    [Display(Name = "Phần trăm hoàn thành")]
    public decimal PercentComplete { get; set; }

    [Display(Name = "Yêu cầu nghiệm thu")]
    public bool RequiresInspection { get; set; }

    [Display(Name = "Người phụ trách")]
    public List<string> AssigneeIds { get; set; } = [];
}

public sealed class TaskListViewModel
{
    public required IReadOnlyCollection<TaskEntity> Tasks { get; init; }
    public bool CanCreate { get; init; }
    public IReadOnlyCollection<Project> Projects { get; init; } = [];
    public int? ProjectId { get; init; }
    public TaskStatus? Status { get; init; }
    public TaskPriority? Priority { get; init; }
    public string? Keyword { get; init; }
}

public sealed class TaskDetailsViewModel
{
    public required TaskEntity Task { get; init; }
    public bool CanEdit { get; init; }
}
