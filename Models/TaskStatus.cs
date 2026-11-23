using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public enum TaskStatus
{
    [Display(Name = "Chờ xử lý")] Pending,
    [Display(Name = "Đã phân công")] Assigned,
    [Display(Name = "Đang thực hiện")] InProgress,
    [Display(Name = "Chờ nghiệm thu")] PendingInspection,
    [Display(Name = "Hoàn thành")] Completed,
    [Display(Name = "Cần làm lại")] NeedsRework,
    [Display(Name = "Đã hủy")] Cancelled
}
