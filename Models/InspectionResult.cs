using System.ComponentModel.DataAnnotations;

namespace ConstructionProjectManagement.Models;

public enum InspectionResult
{
    [Display(Name = "Chờ nghiệm thu")] Pending,
    [Display(Name = "Đạt")] Passed,
    [Display(Name = "Không đạt")] Failed
}
