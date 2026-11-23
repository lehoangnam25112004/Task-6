using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public enum RfiStatus
{
    [Display(Name = "Đang mở")] Open,
    [Display(Name = "Đã phản hồi")] Answered,
    [Display(Name = "Đã đóng")] Closed
}
