using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public enum IncidentStatus
{
    [Display(Name = "Đang mở")] Open,
    [Display(Name = "Đang xử lý")] InProgress,
    [Display(Name = "Đã giải quyết")] Resolved,
    [Display(Name = "Đã đóng")] Closed
}
