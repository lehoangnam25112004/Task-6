using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ConstructionProjectManagement.Models;

public static class EnumDisplayExtensions
{
    public static string DisplayName(this Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.GetName()
        ?? value.ToString();
}
