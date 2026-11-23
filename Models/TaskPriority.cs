using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public enum TaskPriority
{
    [Display(Name = "Thấp")] Low,
    [Display(Name = "Trung bình")] Medium,
    [Display(Name = "Cao")] High,
    [Display(Name = "Khẩn cấp")] Critical
}
