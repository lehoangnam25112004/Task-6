using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Task = System.Threading.Tasks.Task;

namespace WbsUi.Tests;

public sealed class WbsUiFixture : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cpms-wbs-ui-{Guid.NewGuid():N}.sqlite");
    private readonly string _password = $"Test1-{Guid.NewGuid():N}";
    private readonly List<string> _serverLog = [];
    private Process? _server;
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";
    public string Root { get; } = FindRoot();
    public int ActiveId { get; private set; }
    public int OutsideId { get; private set; }
    public int InactiveMemberId { get; private set; }
    public int OutsidePhaseId { get; private set; }
    public int OutsideItemId { get; private set; }
    public int PhaseId { get; private set; }
    public int ItemId { get; private set; }
    public int ChildId { get; private set; }
    public int ArchivedItemId { get; private set; }
    public List<(string Name, int ProjectId, int PhaseId, int ItemId, int ArchivedPhaseId, int ArchivedItemId)> ReadOnlyProjects { get; } = [];
    public Dictionary<string, string> Emails { get; } = new();

    public ApplicationDbContext OpenDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_dbPath};Pooling=False").Options);

    public async Task InitializeAsync()
    {
        try
        {
            await SeedAsync();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            BaseUrl = $"http://127.0.0.1:{port}";

            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in new[] { "run", "--project", Path.Combine(Root, "ConstructionProjectManagement.csproj"),
                         "--no-build", "--no-restore", "--no-launch-profile", "--urls", BaseUrl })
                start.ArgumentList.Add(arg);
            start.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={_dbPath};Pooling=False";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_URLS"] = BaseUrl;
            start.Environment["CPMS_SEED_SAMPLE_DATA"] = "false";
            start.Environment["CPMS_SEED_TASK02_TEST_DATA"] = "false";
            start.Environment["CPMS_REMOVE_TASK02_TEST_DATA"] = "false";
            start.Environment["CPMS_SEED_TASK03_TEST_DATA"] = "false";
            start.Environment.Remove("CPMS_ADMIN_EMAIL");
            start.Environment.Remove("CPMS_ADMIN_PASSWORD");
            _server = new Process { StartInfo = start };
            _server.OutputDataReceived += (_, e) => RecordLog(e.Data);
            _server.ErrorDataReceived += (_, e) => RecordLog(e.Data);
            if (!_server.Start()) throw new InvalidOperationException("Không thể khởi động ứng dụng kiểm thử.");
            _server.BeginOutputReadLine();
            _server.BeginErrorReadLine();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            for (var attempt = 0; attempt < 90; attempt++)
            {
                if (_server.HasExited) break;
                try
                {
                    using var response = await http.GetAsync($"{BaseUrl}/Identity/Account/Login");
                    if (response.IsSuccessStatusCode) { ready = true; break; }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(500);
            }
            if (!ready) throw new InvalidOperationException("Ứng dụng kiểm thử không sẵn sàng.");
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "msedge",
                Headless = true
            });
        }
        catch
        {
            SaveLog("startup");
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_server is not null)
        {
            if (!_server.HasExited) _server.Kill(entireProcessTree: true);
            await _server.WaitForExitAsync();
            _server.Dispose();
        }
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Delete(path);
                    break;
                }
                catch (IOException) when (attempt < 9)
                {
                    await Task.Delay(200);
                }
            }
        }
    }

    public async Task<IPage> LoginAsync(IBrowserContext context, string account)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/Identity/Account/Login");
        await page.Locator("#Input_Email").FillAsync(Emails[account]);
        await page.Locator("#Input_Password").FillAsync(_password);
        await page.Locator("form").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("#Input_Password")
        }).Locator("button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));
        return page;
    }

    public void SaveLog(string name)
    {
        var directory = Path.Combine(Root, "tests", "WbsUi.Tests", "TestResults");
        Directory.CreateDirectory(directory);
        lock (_serverLog) File.WriteAllLines(Path.Combine(directory, $"{name}-server.log"), _serverLog);
    }

    private void RecordLog(string? line)
    {
        if (line is null) return;
        lock (_serverLog) _serverLog.Add(line);
    }

    private async Task SeedAsync()
    {
        await using var db = OpenDb();
        await db.Database.MigrateAsync();
        var roles = RoleNames.All.ToDictionary(name => name, name => new IdentityRole
        {
            Name = name, NormalizedName = name.ToUpperInvariant()
        });
        db.Roles.AddRange(roles.Values);
        var users = new Dictionary<string, ApplicationUser>();
        foreach (var key in RoleNames.All.Concat(new[] { "Inactive", "InactiveMember", "Mismatch", "AdminPM", "AdminPD", "NoRole" }))
        {
            var email = $"wbs-ui-{key.Replace(" ", "-").ToLowerInvariant()}@example.invalid";
            var user = new ApplicationUser
            {
                UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(), IsActive = key != "Inactive",
                OrganizationName = key == "Mismatch" ? "Other Client" : "Client A"
            };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, _password);
            users.Add(key, user);
            Emails.Add(key, email);
            db.Users.Add(user);
            var userRoles = key switch
            {
                "Inactive" or "InactiveMember" => new[] { "Project Manager" },
                "Mismatch" => new[] { "Client Representative" },
                "AdminPM" => new[] { "Admin", "Project Manager" },
                "AdminPD" => new[] { "Admin", "Project Director" },
                "NoRole" => Array.Empty<string>(),
                _ => new[] { key }
            };
            foreach (var role in userRoles)
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roles[role].Id });
        }

        Project NewProject(string code, ProjectStatus status, bool archived = false) => new()
        {
            ProjectCode = code, Name = $"UI test {code}", ClientName = "Client A",
            StartDate = DateTime.UtcNow.Date, ExpectedEndDate = DateTime.UtcNow.Date.AddDays(30),
            Status = status, IsArchived = archived
        };
        var active = NewProject("UI-ACTIVE", ProjectStatus.InProgress);
        var outside = NewProject("UI-OUTSIDE", ProjectStatus.InProgress);
        var inactiveMember = NewProject("UI-INACTIVE-MEMBER", ProjectStatus.InProgress);
        var readOnly = new[]
        {
            NewProject("UI-ARCHIVED", ProjectStatus.InProgress, true),
            NewProject("UI-COMPLETED", ProjectStatus.Completed),
            NewProject("UI-CANCELLED", ProjectStatus.Cancelled)
        };
        db.Projects.AddRange(new[] { active, outside, inactiveMember }.Concat(readOnly));
        await db.SaveChangesAsync();
        ActiveId = active.Id;
        OutsideId = outside.Id;
        InactiveMemberId = inactiveMember.Id;
        foreach (var project in new[] { active }.Concat(readOnly))
        {
            foreach (var (key, user) in users)
            {
                if (key is "Admin" or "AdminPM" or "AdminPD" or "InactiveMember") continue;
                db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = user.Id, IsActive = true });
            }
        }
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = inactiveMember.Id, UserId = users["InactiveMember"].Id, IsActive = false
        });
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = active.Id, UserId = users["InactiveMember"].Id, IsActive = true
        });
        foreach (var project in new[] { active, outside }.Concat(readOnly))
        {
            var phase = new ProjectPhase { ProjectId = project.Id, Name = "UI phase", SequenceOrder = 1 };
            db.ProjectPhases.Add(phase);
            await db.SaveChangesAsync();
            var item = new WorkItem
            {
                ProjectId = project.Id, ProjectPhaseId = phase.Id, WbsCode = $"UI-{project.Id}-ROOT",
                Name = "UI root", SequenceOrder = 1, Level = 1, Status = WbsItemStatus.NotStarted
            };
            db.WorkItems.Add(item);
            await db.SaveChangesAsync();
            if (project.Id == active.Id)
            {
                PhaseId = phase.Id;
                ItemId = item.Id;
                var child = new WorkItem
                {
                    ProjectId = project.Id, ProjectPhaseId = phase.Id, ParentWorkItemId = item.Id,
                    WbsCode = "UI-CHILD", Name = "UI child", SequenceOrder = 1, Level = 2,
                    Status = WbsItemStatus.OnHold
                };
                var archivedItem = new WorkItem
                {
                    ProjectId = project.Id, ProjectPhaseId = phase.Id, WbsCode = "UI-ARCHIVED-ITEM",
                    Name = "UI archived item", SequenceOrder = 2, Level = 1, IsArchived = true,
                    Status = WbsItemStatus.Archived, StatusBeforeArchive = WbsItemStatus.InProgress,
                    ArchiveGroupId = Guid.NewGuid()
                };
                db.WorkItems.AddRange(child, archivedItem);
                await db.SaveChangesAsync();
                ChildId = child.Id;
                ArchivedItemId = archivedItem.Id;
            }
            else if (project.Id == outside.Id)
            {
                OutsidePhaseId = phase.Id;
                OutsideItemId = item.Id;
            }
            else
            {
                var archivedItem = new WorkItem
                {
                    ProjectId = project.Id, ProjectPhaseId = phase.Id, WbsCode = $"UI-{project.Id}-ARCHIVED",
                    Name = "UI readonly archived", SequenceOrder = 2, Level = 1, IsArchived = true,
                    Status = WbsItemStatus.Archived, StatusBeforeArchive = WbsItemStatus.OnHold,
                    ArchiveGroupId = Guid.NewGuid()
                };
                var archivedPhase = new ProjectPhase
                {
                    ProjectId = project.Id, Name = "UI readonly archived phase", SequenceOrder = 2,
                    IsArchived = true, Status = ProjectPhaseStatus.Archived,
                    StatusBeforeArchive = ProjectPhaseStatus.InProgress, ArchiveGroupId = Guid.NewGuid()
                };
                db.WorkItems.Add(archivedItem);
                db.ProjectPhases.Add(archivedPhase);
                await db.SaveChangesAsync();
                ReadOnlyProjects.Add((project.ProjectCode, project.Id, phase.Id, item.Id, archivedPhase.Id, archivedItem.Id));
            }
        }
        await db.SaveChangesAsync();
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ConstructionProjectManagement.csproj"))) return dir.FullName;
        throw new DirectoryNotFoundException("Không tìm thấy project ứng dụng.");
    }
}
