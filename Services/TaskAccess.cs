using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;

namespace ConstructionProjectManagement.Services;

public sealed class TaskAccess
{
    private static readonly string[] ManageRoles = ["Admin", "Project Director", "Project Manager"];
    private static readonly string[] ProgressRoles = ["Admin", "Project Director", "Project Manager", "Site Manager"];
    private static readonly string[] QualityRoles = ["Admin", "Project Director", "Project Manager", "Site Manager", "Supervising Engineer"];
    private readonly IReadOnlyCollection<string> _roles;
    private readonly Dictionary<int, Project> _projects;

    private TaskAccess(string userId, IReadOnlyCollection<string> roles, IEnumerable<Project> projects)
    {
        UserId = userId;
        _roles = roles;
        _projects = projects.ToDictionary(project => project.Id);
    }

    public string UserId { get; }
    public IReadOnlyCollection<string> Roles => _roles;
    public bool CanManageTasks => _roles.Any(ManageRoles.Contains);
    public bool CanManageQuality => _roles.Any(QualityRoles.Contains);
    public bool CanViewQuality => CanManageQuality || _roles.Contains("Client Representative", StringComparer.Ordinal);
    public int[] ProjectIds => _projects.Keys.ToArray();
    public int[] WritableProjectIds => _projects.Values.Where(IsWritable).Select(project => project.Id).ToArray();

    public static async Task<TaskAccess?> LoadAsync(
        ApplicationDbContext db, string userId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        if (!roles.Any(role => RoleNames.All.Contains(role, StringComparer.Ordinal)))
            return null;

        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        if (user is null)
            return null;

        var admin = roles.Contains(RoleNames.Admin, StringComparer.Ordinal);
        var client = roles.Contains("Client Representative", StringComparer.Ordinal);
        var query = db.Projects.AsNoTracking().Include(project => project.Members).AsQueryable();
        if (client)
            query = query.Where(project => project.Members.Any(member => member.UserId == userId && member.IsActive));
        else if (!admin)
            query = query.Where(project => project.ProjectManagerId == userId ||
                project.Members.Any(member => member.UserId == userId && member.IsActive));

        var projects = await query.ToListAsync(cancellationToken);
        if (client)
            projects = projects.Where(project =>
                !string.IsNullOrWhiteSpace(user.OrganizationName) &&
                !string.IsNullOrWhiteSpace(project.ClientName) &&
                string.Equals(user.OrganizationName.Trim(), project.ClientName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new TaskAccess(userId, roles, projects);
    }

    public bool CanView(int projectId) => _projects.ContainsKey(projectId);

    public bool CanEditTask(int projectId) =>
        CanManageTasks && _projects.TryGetValue(projectId, out var project) && IsWritable(project);

    public bool CanWriteQuality(int projectId) =>
        CanManageQuality && _projects.TryGetValue(projectId, out var project) && IsWritable(project);

    public bool CanUpdateProgress(TaskEntity task) =>
        _projects.TryGetValue(task.ProjectId, out var project) && IsWritable(project) &&
        (_roles.Any(ProgressRoles.Contains) ||
         (_roles.Any(role => role is "Worker" or "Supervising Engineer") &&
          task.Assignments.Any(assignment => assignment.UserId == UserId)));

    public static bool IsWritable(Project project) =>
        !project.IsArchived && project.Status is not (ProjectStatus.Completed or ProjectStatus.Cancelled);
}
