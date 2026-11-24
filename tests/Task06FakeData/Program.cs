using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using TaskStatus = ConstructionProjectManagement.Models.TaskStatus;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) ||
    !string.Equals(Path.GetFileName(args[0]), "ConstructionProjectManagement.Task06FakeTest.db", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Truyền một đường dẫn tuyệt đối có tên ConstructionProjectManagement.Task06FakeTest.db.");
if (!string.Equals(Environment.GetEnvironmentVariable("CPMS_ALLOW_TASK06_MIGRATION"), "true", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Phải bật CPMS_ALLOW_TASK06_MIGRATION=true cho database giả lập mới.");
var password = Environment.GetEnvironmentVariable("TASK06_FAKE_PASSWORD");
if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || !password.Any(char.IsDigit))
    throw new InvalidOperationException("Đặt TASK06_FAKE_PASSWORD từ 8 ký tự, có ít nhất một chữ số.");

var path = Path.GetFullPath(args[0]);
var backupPath = path + ".before-migration.bak";
if (File.Exists(path) || File.Exists(backupPath))
    throw new IOException("Database hoặc bản sao lưu đã tồn tại; không ghi đè. Chọn thư mục mới.");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
await using (var source = new SqliteConnection(connectionString))
{
    await source.OpenAsync();
    await using var check = source.CreateCommand();
    check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'";
    if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 0)
        throw new InvalidOperationException("Database mới không rỗng; dừng trước migration.");
    await using var backupFile = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    backupFile.Close();
    await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = backupPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
    await backup.OpenAsync();
    source.BackupDatabase(backup);
    await using var backupCheck = backup.CreateCommand();
    backupCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'";
    if (Convert.ToInt64(await backupCheck.ExecuteScalarAsync()) != 0)
        throw new InvalidOperationException("Bản sao trước migration không rỗng; dừng.");
    backupCheck.CommandText = "PRAGMA integrity_check";
    if (!string.Equals(Convert.ToString(await backupCheck.ExecuteScalarAsync()), "ok", StringComparison.Ordinal))
        throw new InvalidOperationException("Bản sao trước migration không toàn vẹn; dừng.");
}
Console.WriteLine($"Database giả lập mới: {path}");
Console.WriteLine($"Đã kiểm tra database rỗng và sao lưu trước migration: {backupPath}");

await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlite(connectionString).Options);
var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
await DbInitializer.ApplyMigrationsAsync(db, configuration);
await using var transaction = await db.Database.BeginTransactionAsync();

var roles = RoleNames.All.ToDictionary(name => name, name => new IdentityRole
{
    Name = name, NormalizedName = name.ToUpperInvariant()
});
db.Roles.AddRange(roles.Values);
var accounts = new (string Key, string Role, string Email, string FullName, string Organization)[]
{
    ("admin", "Admin", "admin.task06@example.invalid", "TASK06 Admin", "Chủ đầu tư A"),
    ("director", "Project Director", "director.task06@example.invalid", "TASK06 Giám đốc dự án", "Chủ đầu tư A"),
    ("manager", "Project Manager", "manager.task06@example.invalid", "TASK06 Quản lý dự án", "Chủ đầu tư A"),
    ("site", "Site Manager", "site.task06@example.invalid", "TASK06 Chỉ huy công trường", "Chủ đầu tư A"),
    ("engineer", "Supervising Engineer", "engineer.task06@example.invalid", "TASK06 Kỹ sư giám sát", "Chủ đầu tư A"),
    ("worker", "Worker", "worker.task06@example.invalid", "TASK06 Công nhân được giao", "Chủ đầu tư A"),
    ("worker-empty", "Worker", "worker.empty.task06@example.invalid", "TASK06 Công nhân chưa được giao", "Chủ đầu tư A"),
    ("materials", "Materials Staff", "materials.task06@example.invalid", "TASK06 Nhân viên vật tư", "Chủ đầu tư A"),
    ("client", "Client Representative", "client.task06@example.invalid", "TASK06 Đại diện chủ đầu tư A", "Chủ đầu tư A"),
    ("client-mismatch", "Client Representative", "client.other.task06@example.invalid", "TASK06 Đại diện tổ chức khác", "Chủ đầu tư B")
};
var users = new Dictionary<string, ApplicationUser>();
foreach (var account in accounts)
{
    var user = new ApplicationUser
    {
        UserName = account.Email, NormalizedUserName = account.Email.ToUpperInvariant(),
        Email = account.Email, NormalizedEmail = account.Email.ToUpperInvariant(),
        FullName = account.FullName, OrganizationName = account.Organization,
        EmailConfirmed = true, IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
    };
    user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, password);
    users.Add(account.Key, user);
    db.Users.Add(user);
    db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roles[account.Role].Id });
}

var today = DateTime.Today;
Project NewProject(string code, string name, ProjectStatus status, bool archived = false, string client = "Chủ đầu tư A") => new()
{
    ProjectCode = code, Name = name, ClientName = client, Status = status, IsArchived = archived,
    StartDate = today.AddDays(-60), ExpectedEndDate = today.AddDays(90)
};
var active = NewProject("T06-A", "TASK06 - Công trường đang hoạt động", ProjectStatus.InProgress);
active.ProjectManagerId = users["manager"].Id;
var outside = NewProject("T06-OUT", "TASK06 - Ngoài phạm vi", ProjectStatus.InProgress, client: "Chủ đầu tư B");
var archived = NewProject("T06-ARCH", "TASK06 - Dự án lưu trữ", ProjectStatus.Completed, archived: true);
var completed = NewProject("T06-DONE", "TASK06 - Dự án hoàn thành", ProjectStatus.Completed);
var cancelled = NewProject("T06-CANCEL", "TASK06 - Dự án đã hủy", ProjectStatus.Cancelled);
var projects = new[] { active, outside, archived, completed, cancelled };
db.Projects.AddRange(projects);
foreach (var project in new[] { active, archived, completed, cancelled })
    foreach (var account in accounts.Where(account => account.Key != "admin"))
        db.ProjectMembers.Add(new ProjectMember { Project = project, UserId = users[account.Key].Id, IsActive = true });

var items = new Dictionary<Project, WorkItem>();
foreach (var project in projects)
{
    var phase = new ProjectPhase { Project = project, Name = "Thi công TASK06", SequenceOrder = 1 };
    var item = new WorkItem
    {
        Project = project, ProjectPhase = phase, WbsCode = $"{project.ProjectCode}-WBS",
        Name = "Hạng mục thi công giả lập", SequenceOrder = 1, Level = 1,
        Status = project.Status == ProjectStatus.Completed ? WbsItemStatus.Completed : WbsItemStatus.InProgress
    };
    db.WorkItems.Add(item);
    items.Add(project, item);
}
await db.SaveChangesAsync();

var taskList = new List<TaskEntity>();
TaskEntity NewTask(Project project, string title, TaskStatus status, decimal percent,
    bool inspection = false, bool assigned = true, bool overdue = false)
{
    var task = new TaskEntity
    {
        Project = project, WorkItem = items[project], Title = title, Status = status, PercentComplete = percent,
        PlannedStartDate = today.AddDays(-20), PlannedEndDate = overdue ? today.AddDays(-5) : today.AddDays(20),
        ActualStartDate = percent > 0 ? today.AddDays(-3) : null,
        ActualEndDate = status == TaskStatus.Completed ? today.AddDays(-1) : null,
        RequiresInspection = inspection, CreatedAt = today.AddDays(-20), UpdatedAt = today.AddDays(-1),
        CancelReason = status == TaskStatus.Cancelled ? "Hủy do thay đổi phạm vi thi công" : null,
        Priority = (TaskPriority)(taskList.Count % 4)
    };
    if (assigned)
    {
        task.Assignments.Add(new TaskAssignment { UserId = users["worker"].Id, AssignedAt = today.AddDays(-15) });
        task.Assignments.Add(new TaskAssignment { UserId = users["engineer"].Id, AssignedAt = today.AddDays(-15) });
    }
    db.Tasks.Add(task);
    taskList.Add(task);
    var actorId = users[project == outside ? "admin" : "manager"].Id;
    db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = actorId, Action = "Created", PerformedAt = today.AddDays(-15) });
    db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = actorId, Action = "WbsChanged",
        FieldName = nameof(TaskEntity.WorkItemId), NewValue = items[project].Id.ToString(), PerformedAt = today.AddDays(-15) });
    if (assigned)
        foreach (var assignment in task.Assignments)
            db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = actorId,
                Action = "AssigneeChanged", FieldName = "UserId", NewValue = assignment.UserId, PerformedAt = today.AddDays(-15) });

    var initial = assigned ? TaskStatus.Assigned : TaskStatus.Pending;
    db.TaskProgressHistories.Add(new TaskProgressHistory
    {
        Task = task, UpdatedById = actorId, PreviousStatus = initial, NewStatus = initial,
        UpdatedAt = today.AddDays(-15)
    });
    var previousStatus = initial;
    decimal previousPercent = 0;
    void Transition(TaskStatus next, decimal value, int days, string? note = null)
    {
        db.TaskProgressHistories.Add(new TaskProgressHistory
        {
            Task = task, UpdatedById = actorId, PreviousStatus = previousStatus, NewStatus = next,
            PreviousPercentComplete = previousPercent, NewPercentComplete = value, UpdatedAt = today.AddDays(days),
            DelayReason = overdue && value < 100 && next != TaskStatus.Cancelled ? "Mưa lớn kéo dài ảnh hưởng thi công" : null,
            Note = note
        });
        if (previousStatus != next)
            db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = actorId,
                Action = "StatusChanged", FieldName = nameof(TaskEntity.Status), OldValue = previousStatus.ToString(),
                NewValue = next.ToString(), Reason = note, PerformedAt = today.AddDays(days) });
        if (previousPercent != value)
            db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = actorId,
                Action = "ProgressUpdated", FieldName = nameof(TaskEntity.PercentComplete),
                OldValue = previousPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture), PerformedAt = today.AddDays(days) });
        previousStatus = next;
        previousPercent = value;
    }
    if (status is TaskStatus.InProgress or TaskStatus.PendingInspection or TaskStatus.NeedsRework or TaskStatus.Completed or TaskStatus.Cancelled)
        Transition(TaskStatus.InProgress, status is TaskStatus.InProgress or TaskStatus.Cancelled ? percent : 60, -3);
    if (status is TaskStatus.PendingInspection or TaskStatus.NeedsRework || status == TaskStatus.Completed && inspection)
        Transition(TaskStatus.PendingInspection, 100, -2);
    if (status == TaskStatus.NeedsRework) Transition(status, percent, -1);
    if (status == TaskStatus.Completed) Transition(status, 100, -1);
    if (status == TaskStatus.Cancelled)
    {
        Transition(status, percent, -1, task.CancelReason);
        db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = actorId,
            Action = "Cancelled", Reason = task.CancelReason, PerformedAt = today.AddDays(-1) });
    }
    if (inspection && status is TaskStatus.PendingInspection or TaskStatus.NeedsRework or TaskStatus.Completed)
    {
        var result = status == TaskStatus.PendingInspection ? InspectionResult.Pending :
            status == TaskStatus.NeedsRework ? InspectionResult.Failed : InspectionResult.Passed;
        var request = new InspectionRequest
        {
            Project = project, Task = task, RequestedById = users["manager"].Id, RequestedAt = today.AddDays(-2),
            Result = result, InspectedById = result == InspectionResult.Pending ? null : users["engineer"].Id,
            InspectedAt = result == InspectionResult.Pending ? null : today.AddDays(-1)
        };
        if (result != InspectionResult.Pending)
            request.History.Add(new InspectionHistory { Result = result, InspectedById = users["engineer"].Id,
                InspectedAt = today.AddDays(-1), Notes = "Kết quả nghiệm thu giả lập" });
        db.InspectionRequests.Add(request);
    }
    return task;
}

NewTask(active, "T06-01 Chờ xử lý", TaskStatus.Pending, 0, assigned: false);
NewTask(active, "T06-02 Đã phân công", TaskStatus.Assigned, 0);
var overdueTask = NewTask(active, "T06-03 Trễ hạn cần lý do chậm", TaskStatus.InProgress, 35, overdue: true);
NewTask(active, "T06-04 Chờ nghiệm thu", TaskStatus.PendingInspection, 100, inspection: true);
NewTask(active, "T06-05 Nghiệm thu không đạt cần làm lại", TaskStatus.NeedsRework, 65, inspection: true);
var doneTask = NewTask(active, "T06-06 Hoàn thành nghiệm thu đạt", TaskStatus.Completed, 100, inspection: true);
NewTask(active, "T06-07 Đã hủy có lý do", TaskStatus.Cancelled, 30);
var waiting = NewTask(active, "T06-08 Tiền nhiệm chưa hoàn tất", TaskStatus.Assigned, 0);
var blocked = NewTask(active, "T06-09 Bị chặn bởi tiền nhiệm", TaskStatus.Assigned, 0);
var ready = NewTask(active, "T06-10 Tiền nhiệm đã hoàn tất", TaskStatus.Assigned, 0);
await db.SaveChangesAsync();
foreach (var (task, predecessor) in new[] { (blocked, waiting), (ready, doneTask) })
{
    db.TaskDependencies.Add(new TaskDependency { Task = task, PredecessorTask = predecessor,
        CreatedById = users["manager"].Id, CreatedAt = today.AddDays(-10) });
    db.TaskAuditLogs.Add(new TaskAuditLog { Task = task, PerformedById = users["manager"].Id,
        Action = "DependencyAdded", FieldName = "PredecessorTaskId", NewValue = predecessor.Id.ToString(), PerformedAt = today.AddDays(-10) });
}
foreach (var index in Enumerable.Range(11, 24))
    NewTask(active, $"T06-{index:00} Công việc phân trang", TaskStatus.InProgress, 10 + index % 8 * 10);
NewTask(outside, "T06-OUT Công việc ngoài phạm vi", TaskStatus.Pending, 0, assigned: false);
foreach (var project in new[] { archived, completed })
    NewTask(project, $"{project.ProjectCode} Công việc chỉ đọc", TaskStatus.Completed, 100);
NewTask(cancelled, $"{cancelled.ProjectCode} Công việc chỉ đọc", TaskStatus.InProgress, 25);
db.ProjectHandovers.Add(new ProjectHandover
{
    Project = archived, Title = "Bàn giao giả lập trước lưu trữ", HandedOverById = users["manager"].Id,
    ReceivedByName = "Đại diện chủ đầu tư A", HandoverDate = today, IsConfirmed = true, ConfirmedAt = DateTime.UtcNow
});
db.TaskComments.AddRange(
    new TaskComment { Task = overdueTask, AuthorId = users["worker"].Id,
        Content = "Mưa lớn làm chậm tiến độ, cần điều chỉnh lịch thi công.", IsBlocker = true,
        ProgressSnapshot = 35, CreatedAt = today.AddDays(-1).AddHours(8) },
    new TaskComment { Task = overdueTask, AuthorId = users["site"].Id,
        Content = "Chỉ huy công trường đang phối hợp xử lý.", ProgressSnapshot = 35, CreatedAt = today.AddDays(-1).AddHours(9) });
await db.SaveChangesAsync();
await transaction.CommitAsync();

Console.WriteLine($"Đã tạo {projects.Length} dự án, {taskList.Count} công việc, {accounts.Length} tài khoản; " +
    $"{await db.TaskProgressHistories.CountAsync()} lịch sử, {await db.TaskAuditLogs.CountAsync()} audit, " +
    $"{await db.TaskComments.CountAsync()} bình luận, {await db.TaskDependencies.CountAsync()} phụ thuộc.");
foreach (var account in accounts) Console.WriteLine($"{account.Role,-22} {account.Email}");
Console.WriteLine("Mật khẩu: giá trị TASK06_FAKE_PASSWORD đã đặt trước khi chạy seed.");
