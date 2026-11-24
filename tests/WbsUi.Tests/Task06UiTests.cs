using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace WbsUi.Tests;

public sealed class Task06UiTests(WbsUiFixture fixture) : IClassFixture<WbsUiFixture>
{
    [Fact]
    public async Task ResolveBlocker_UpdatesBadgesOnAllPagesAndPreservesHistory()
    {
        var marker = $"RESOLVE-BADGES-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var author = await db.Users.SingleAsync(user => user.Email == fixture.Emails["Worker"]);
            var task = new TaskEntity { ProjectId = fixture.ActiveId, Title = marker,
                Status = ConstructionProjectManagement.Models.TaskStatus.InProgress, PercentComplete = 35,
                PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10) };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
            db.TaskComments.AddRange(
                new TaskComment { TaskId = taskId, AuthorId = author.Id, Content = "Vướng mắc thứ nhất", IsBlocker = true, UpdatedAt = DateTime.UtcNow.AddHours(-1) },
                new TaskComment { TaskId = taskId, AuthorId = author.Id, Content = "Vướng mắc thứ hai", IsBlocker = true });
            await db.SaveChangesAsync();
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        Assert.Equal(1, await page.Locator("h1 .badge").CountAsync());
        var first = page.Locator("div.mb-3").Filter(new() { HasText = "Vướng mắc thứ nhất" });
        await first.GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).ClickAsync();
        Assert.Contains("Đã giải quyết vướng mắc", await page.Locator(".alert-success").InnerTextAsync());
        Assert.Contains("Đã chỉnh sửa", await first.InnerTextAsync());
        Assert.Equal(0, await first.Locator(".badge").CountAsync());
        Assert.Equal(1, await page.Locator("h1 .badge").CountAsync());
        foreach (var route in new[] { "Progress", "Tasks" })
        {
            await page.GotoAsync($"{fixture.BaseUrl}/{route}?Keyword={marker}");
            var row = page.Locator("tbody tr").Filter(new() { HasText = marker });
            Assert.Equal(1, await row.Locator(".badge").Filter(new() { HasText = "Vướng mắc" }).CountAsync());
        }
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        await page.Locator("div.mb-3").Filter(new() { HasText = "Vướng mắc thứ hai" })
            .GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).ClickAsync();
        Assert.Equal(0, await page.Locator("h1 .badge").CountAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).CountAsync());
        Assert.Equal(1, await page.Locator("#commentContent").CountAsync());
        foreach (var route in new[] { "Progress", "Tasks" })
        {
            await page.GotoAsync($"{fixture.BaseUrl}/{route}?Keyword={marker}");
            var row = page.Locator("tbody tr").Filter(new() { HasText = marker });
            Assert.Equal(0, await row.Locator(".badge").Filter(new() { HasText = "Vướng mắc" }).CountAsync());
        }
        await using var verify = fixture.OpenDb();
        var saved = await verify.Tasks.SingleAsync(task => task.Id == taskId);
        Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.InProgress, saved.Status);
        Assert.Equal(35m, saved.PercentComplete);
        Assert.Equal(2, await verify.TaskComments.CountAsync(comment => comment.TaskId == taskId && !comment.IsBlocker));
        Assert.Equal(2, await verify.TaskAuditLogs.CountAsync(log => log.TaskId == taskId && log.Action == "BlockerResolved"));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", false)]
    [InlineData("Supervising Engineer", false)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", false)]
    public async Task ResolveBlocker_OtherAuthorsRoleMatrix(string role, bool allowed)
    {
        int taskId, commentId, readOnlyTaskId, readOnlyCommentId;
        await using (var db = fixture.OpenDb())
        {
            var actor = await db.Users.SingleAsync(user => user.Email == fixture.Emails[role]);
            var author = await db.Users.SingleAsync(user => user.Email == fixture.Emails["Project Manager"]);
            var tasks = new[] { fixture.ActiveId, fixture.ReadOnlyProjects[0].ProjectId }.Select(projectId => new TaskEntity
            {
                ProjectId = projectId, Title = $"RESOLVE-ROLE-{role}-{Guid.NewGuid():N}",
                PlannedEndDate = DateTime.Today.AddDays(10), Assignments = [new TaskAssignment { UserId = actor.Id }]
            }).ToArray();
            db.Tasks.AddRange(tasks);
            await db.SaveChangesAsync();
            var comments = tasks.Select(task => new TaskComment
                { TaskId = task.Id, AuthorId = author.Id, Content = "Vướng mắc để thử quyền", IsBlocker = true }).ToArray();
            db.TaskComments.AddRange(comments);
            await db.SaveChangesAsync();
            taskId = tasks[0].Id; commentId = comments[0].Id;
            readOnlyTaskId = tasks[1].Id; readOnlyCommentId = comments[1].Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        Assert.Equal(allowed ? 1 : 0, await page.GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).CountAsync());
        var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        async Task AssertForbidden(int targetTaskId, int targetCommentId)
        {
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("taskId", targetTaskId.ToString());
            form.Append("commentId", targetCommentId.ToString());
            var response = await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/ResolveBlocker", new() { Form = form, MaxRedirects = 0 });
            Assert.Equal(302, response.Status);
            Assert.Contains("AccessDenied", response.Headers["location"], StringComparison.OrdinalIgnoreCase);
        }
        if (!allowed) await AssertForbidden(taskId, commentId);
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{readOnlyTaskId}");
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).CountAsync());
        await AssertForbidden(readOnlyTaskId, readOnlyCommentId);
        await using var verify = fixture.OpenDb();
        Assert.True((await verify.TaskComments.SingleAsync(comment => comment.Id == readOnlyCommentId)).IsBlocker);
    }

    [Fact]
    public async Task ResolveBlocker_WorkerCanResolveOwnButNotAnotherAuthorsComment()
    {
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var worker = await db.Users.SingleAsync(user => user.Email == fixture.Emails["Worker"]);
            var manager = await db.Users.SingleAsync(user => user.Email == fixture.Emails["Project Manager"]);
            var task = new TaskEntity { ProjectId = fixture.ActiveId, Title = $"RESOLVE-OWNER-{Guid.NewGuid():N}",
                PlannedEndDate = DateTime.Today.AddDays(10), Assignments = [new TaskAssignment { UserId = worker.Id }] };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
            db.TaskComments.AddRange(
                new TaskComment { TaskId = taskId, AuthorId = worker.Id, Content = "Của tôi", IsBlocker = true, CreatedAt = DateTime.UtcNow.AddDays(-1) },
                new TaskComment { TaskId = taskId, AuthorId = manager.Id, Content = "Của người khác", IsBlocker = true });
            await db.SaveChangesAsync();
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Worker");
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        Assert.Equal(1, await page.GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Đã giải quyết" }).ClickAsync();
        Assert.Equal(1, await page.Locator("h1 .badge").CountAsync());
        await using var verify = fixture.OpenDb();
        Assert.False((await verify.TaskComments.SingleAsync(comment => comment.TaskId == taskId && comment.Content == "Của tôi")).IsBlocker);
        Assert.True((await verify.TaskComments.SingleAsync(comment => comment.TaskId == taskId && comment.Content == "Của người khác")).IsBlocker);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", false)]
    [InlineData("Supervising Engineer", false)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", false)]
    public async Task RestoreCancelled_RoleMatrixOnProgressAndDetails(string role, bool allowed)
    {
        var marker = $"RESTORE-{role.Replace(" ", "-")}-{Guid.NewGuid():N}";
        int taskId;
        Guid stamp;
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails[role]);
            var task = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker, Status = ConstructionProjectManagement.Models.TaskStatus.Cancelled,
                PercentComplete = 20, CancelReason = "Hủy để kiểm thử khôi phục",
                PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10),
                Assignments = [new TaskAssignment { UserId = user.Id }]
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
            stamp = task.ConcurrencyStamp;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var row = page.Locator("tbody tr").Filter(new() { HasText = marker });
        Assert.Equal(allowed ? 1 : 0, await row.GetByRole(AriaRole.Button, new() { Name = "Khôi phục", Exact = true }).CountAsync());
        if (allowed)
            Assert.Equal(1, await row.Locator("input[readonly]").CountAsync());
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        Assert.Equal(allowed ? 1 : 0, await page.GetByRole(AriaRole.Button, new() { Name = "Khôi phục công việc" }).CountAsync());
        if (!allowed)
        {
            var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("TaskId", taskId.ToString());
            form.Append("ConcurrencyStamp", stamp.ToString());
            form.Append("Status", "InProgress");
            form.Append("Reason", "Mở lại");
            var response = await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/RestoreCancelled", new() { Form = form, MaxRedirects = 0 });
            Assert.Equal(302, response.Status);
            Assert.Contains("AccessDenied", response.Headers["location"], StringComparison.OrdinalIgnoreCase);
            await using var verify = fixture.OpenDb();
            Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.Cancelled,
                (await verify.Tasks.SingleAsync(item => item.Id == taskId)).Status);
        }
    }

    [Fact]
    public async Task RestoreCancelled_PreservesProgressAndShowsBlockingWarning()
    {
        var marker = $"RESTORE-BLOCK-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var manager = await db.Users.SingleAsync(item => item.Email == fixture.Emails["Project Manager"]);
            var predecessor = new TaskEntity { ProjectId = fixture.ActiveId, Title = $"{marker}-PREDECESSOR", PlannedEndDate = DateTime.Today.AddDays(10) };
            var task = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker, Status = ConstructionProjectManagement.Models.TaskStatus.Cancelled,
                PercentComplete = 35, ActualStartDate = DateTime.Today.AddDays(-1), CancelReason = "Hủy công việc trước đó",
                PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10)
            };
            db.Tasks.AddRange(predecessor, task);
            await db.SaveChangesAsync();
            db.TaskDependencies.Add(new TaskDependency { TaskId = task.Id, PredecessorTaskId = predecessor.Id });
            db.TaskComments.Add(new TaskComment { TaskId = task.Id, AuthorId = manager.Id,
                Content = "Vướng mắc còn lại", IsBlocker = true, ProgressSnapshot = 35 });
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        var form = page.Locator("form[action$='/RestoreCancelled']");
        await form.Locator("input[name=Reason]").FillAsync("Khôi phục");
        await form.GetByRole(AriaRole.Button, new() { Name = "Khôi phục công việc" }).ClickAsync();
        Assert.Contains("Đã khôi phục", await page.Locator(".alert-success").InnerTextAsync());
        Assert.Contains($"{marker}-PREDECESSOR", await page.Locator(".alert-warning[role=alert]").InnerTextAsync());
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var row = page.Locator("tbody tr").Filter(new() { HasText = marker }).Filter(new() { HasNotText = "-PREDECESSOR" });
        Assert.Contains("Bị chặn", await row.InnerTextAsync());
        Assert.Contains("Vướng mắc", await row.InnerTextAsync());
        Assert.Equal(0, await row.GetByRole(AriaRole.Button, new() { Name = "Lưu tiến độ" }).CountAsync());
        await using var verify = fixture.OpenDb();
        var saved = await verify.Tasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.InProgress, saved.Status);
        Assert.Equal(35m, saved.PercentComplete);
        Assert.Equal("Hủy công việc trước đó", saved.CancelReason);
        Assert.True(await verify.TaskAuditLogs.AnyAsync(item => item.TaskId == taskId && item.Action == "StatusChanged" && item.Reason == "Khôi phục"));
    }

    [Fact]
    public async Task RestoreCancelled_FromProgressIgnoresForgedPercent()
    {
        var marker = $"RESTORE-PROGRESS-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var task = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker, Status = ConstructionProjectManagement.Models.TaskStatus.Cancelled,
                PercentComplete = 0, CancelReason = "Hủy để kiểm thử khôi phục",
                PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10)
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var form = page.Locator("tbody tr").Filter(new() { HasText = marker }).Locator("form[action$='/RestoreCancelled']");
        Assert.Equal("Pending", await form.Locator("select[name=Status]").InputValueAsync());
        await form.Locator("input[name=Reason]").FillAsync("Mở lại");
        await form.EvaluateAsync("(form) => { const input = document.createElement('input'); input.type = 'hidden'; input.name = 'PercentComplete'; input.value = '99'; form.appendChild(input); }");
        await form.GetByRole(AriaRole.Button, new() { Name = "Khôi phục", Exact = true }).ClickAsync();
        Assert.Contains("Đã khôi phục", await page.Locator(".alert-success").InnerTextAsync());
        await using var verify = fixture.OpenDb();
        var saved = await verify.Tasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.Pending, saved.Status);
        Assert.Equal(0m, saved.PercentComplete);
    }

    [Fact]
    public async Task Schedule_SiteManagerCanOnlyChangePlanInWritableScope()
    {
        var marker = $"SCHEDULE-{Guid.NewGuid():N}";
        int taskId, outsideId;
        var readonlyIds = new List<int>();
        await using (var db = fixture.OpenDb())
        {
            TaskEntity NewTask(int projectId) => new()
            {
                ProjectId = projectId, Title = marker, Status = ConstructionProjectManagement.Models.TaskStatus.InProgress,
                PercentComplete = 20, PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10)
            };
            var task = NewTask(fixture.ActiveId);
            var outside = NewTask(fixture.OutsideId);
            var readOnly = fixture.ReadOnlyProjects.Select(item => NewTask(item.ProjectId)).ToList();
            db.Tasks.AddRange(new[] { task, outside }.Concat(readOnly));
            await db.SaveChangesAsync();
            taskId = task.Id;
            outsideId = outside.Id;
            readonlyIds.AddRange(readOnly.Select(item => item.Id));
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Site Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{taskId}");
        Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Cập nhật", Exact = true }).CountAsync());
        await page.GetByRole(AriaRole.Link, new() { Name = "Điều chỉnh kế hoạch" }).ClickAsync();
        var token = await page.Locator("input[name=__RequestVerificationToken]").Last.GetAttributeAsync("value");
        var stamp = await page.Locator("#ConcurrencyStamp").InputValueAsync();
        var stale = await context.NewPageAsync();
        await stale.GotoAsync($"{fixture.BaseUrl}/Tasks/AdjustSchedule/{taskId}");
        await page.Locator("#PlannedStartDate").FillAsync(DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"));
        await page.Locator("#PlannedEndDate").FillAsync(DateTime.Today.AddDays(15).ToString("yyyy-MM-dd"));
        await page.Locator("#Reason").FillAsync("Điều chỉnh kế hoạch thi công");
        await page.GetByRole(AriaRole.Button, new() { Name = "Lưu kế hoạch" }).ClickAsync();
        Assert.Contains("Đã điều chỉnh kế hoạch", await page.Locator("main").InnerTextAsync());
        await stale.Locator("#PlannedEndDate").FillAsync(DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"));
        await stale.Locator("#Reason").FillAsync("Điều chỉnh kế hoạch thi công");
        await stale.GetByRole(AriaRole.Button, new() { Name = "Lưu kế hoạch" }).ClickAsync();
        Assert.Contains("tải lại dữ liệu", await stale.Locator("main").InnerTextAsync());
        Assert.Equal(404, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/AdjustSchedule/{outsideId}")).Status);
        foreach (var id in readonlyIds)
        {
            await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{id}");
            Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Điều chỉnh kế hoạch" }).CountAsync());
            Assert.Equal(302, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/AdjustSchedule/{id}",
                new() { MaxRedirects = 0 })).Status);
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("Id", id.ToString());
            form.Append("ConcurrencyStamp", stamp);
            form.Append("PlannedStartDate", DateTime.Today.ToString("yyyy-MM-dd"));
            form.Append("PlannedEndDate", DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"));
            form.Append("Reason", "Điều chỉnh kế hoạch thi công");
            Assert.Equal(302, (await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Tasks/AdjustSchedule/{id}",
                new() { Form = form, MaxRedirects = 0 })).Status);
        }
        await using var verify = fixture.OpenDb();
        var saved = await verify.Tasks.SingleAsync(item => item.Id == taskId);
        Assert.Equal(DateTime.Today.AddDays(15), saved.PlannedEndDate);
        Assert.Equal(20m, saved.PercentComplete);
        Assert.Equal(2, await verify.TaskAuditLogs.CountAsync(item => item.TaskId == taskId && item.Action == "ScheduleUpdated"));
        Assert.All(await verify.Tasks.Where(item => readonlyIds.Contains(item.Id)).ToListAsync(),
            item => Assert.Equal(DateTime.Today.AddDays(10), item.PlannedEndDate));
    }

    [Fact]
    public async Task Details_ShowsOnlyUnfinishedPredecessorWarningWithoutLeakingToUnassignedWorker()
    {
        var marker = $"DETAIL-BLOCK-{Guid.NewGuid():N}";
        int blockedId, openId;
        await using (var db = fixture.OpenDb())
        {
            var predecessor = new TaskEntity { ProjectId = fixture.ActiveId, Title = $"{marker}-pending", PlannedEndDate = DateTime.Today.AddDays(10) };
            var finished = new TaskEntity { ProjectId = fixture.ActiveId, Title = $"{marker}-finished", PlannedEndDate = DateTime.Today.AddDays(10),
                Status = ConstructionProjectManagement.Models.TaskStatus.Completed, PercentComplete = 100 };
            var blocked = new TaskEntity { ProjectId = fixture.ActiveId, Title = marker, PlannedEndDate = DateTime.Today.AddDays(10) };
            var open = new TaskEntity { ProjectId = fixture.ActiveId, Title = $"{marker}-open", PlannedEndDate = DateTime.Today.AddDays(10) };
            db.Tasks.AddRange(predecessor, finished, blocked, open);
            await db.SaveChangesAsync();
            db.TaskDependencies.AddRange(new TaskDependency { TaskId = blocked.Id, PredecessorTaskId = predecessor.Id },
                new TaskDependency { TaskId = blocked.Id, PredecessorTaskId = finished.Id },
                new TaskDependency { TaskId = open.Id, PredecessorTaskId = finished.Id });
            await db.SaveChangesAsync();
            blockedId = blocked.Id;
            openId = open.Id;
        }
        await using var managerContext = await fixture.Browser.NewContextAsync();
        var manager = await fixture.LoginAsync(managerContext, "Project Manager");
        foreach (var route in new[] { "Tasks", "Progress" })
        {
            await manager.GotoAsync($"{fixture.BaseUrl}/{route}/Details/{blockedId}");
            var warning = manager.Locator(".alert-warning[role=alert]");
            Assert.Contains($"{marker}-pending", await warning.InnerTextAsync());
            Assert.DoesNotContain($"{marker}-finished", await warning.InnerTextAsync());
            await manager.GotoAsync($"{fixture.BaseUrl}/{route}/Details/{openId}");
            Assert.Equal(0, await manager.Locator(".alert-warning[role=alert]").CountAsync());
        }
        await using var workerContext = await fixture.Browser.NewContextAsync();
        var worker = await fixture.LoginAsync(workerContext, "Worker");
        await worker.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{blockedId}");
        Assert.Equal(0, await worker.Locator(".alert-warning[role=alert]").CountAsync());
        Assert.DoesNotContain($"{marker}-pending", await worker.Locator("main").InnerTextAsync());
        Assert.Equal(404, (await workerContext.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/Details/{blockedId}")).Status);
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails["Worker"]);
            db.TaskAssignments.Add(new TaskAssignment { TaskId = blockedId, UserId = user.Id });
            await db.SaveChangesAsync();
        }
        await worker.GotoAsync($"{fixture.BaseUrl}/Progress/Details/{blockedId}");
        Assert.Contains($"{marker}-pending", await worker.Locator(".alert-warning[role=alert]").InnerTextAsync());
    }

    [Theory]
    [InlineData("Admin", true, true, true, true, true)]
    [InlineData("Project Director", true, true, true, true, true)]
    [InlineData("Project Manager", true, true, true, true, true)]
    [InlineData("Site Manager", true, true, false, true, true)]
    [InlineData("Supervising Engineer", true, false, false, true, true)]
    [InlineData("Worker", true, false, false, true, true)]
    [InlineData("Materials Staff", false, false, false, true, false)]
    [InlineData("Client Representative", false, false, false, false, false)]
    public async Task Tracking_EightRoleUiAndServerMatrix(
        string role, bool progress, bool dependencies, bool cancel, bool comment, bool audit)
    {
        var marker = $"T06-{role.Replace(" ", "-")}-{Guid.NewGuid():N}";
        int assignedId, unassignedId, blockedId, predecessorId, completedId, outsideId;
        var readOnlyIds = new List<int>();
        var readOnlyPredecessorIds = new List<int>();
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails[role]);
            TaskEntity NewTask(string suffix, int projectId, bool assigned = false) => new()
            {
                ProjectId = projectId, Title = $"{marker}-{suffix}",
                PlannedStartDate = DateTime.Today.AddDays(-10), PlannedEndDate = DateTime.Today.AddDays(10),
                Assignments = assigned ? [new TaskAssignment { UserId = user.Id }] : []
            };
            var assigned = NewTask("ASSIGNED", fixture.ActiveId, true);
            assigned.Status = ConstructionProjectManagement.Models.TaskStatus.InProgress;
            assigned.PercentComplete = 20;
            assigned.RequiresInspection = true;
            var unassigned = NewTask("UNASSIGNED", fixture.ActiveId);
            var predecessor = NewTask("PREDECESSOR", fixture.ActiveId);
            var blocked = NewTask("BLOCKED", fixture.ActiveId, true);
            var completed = NewTask("COMPLETED", fixture.ActiveId, true);
            completed.Status = ConstructionProjectManagement.Models.TaskStatus.Completed;
            completed.PercentComplete = 100;
            var outside = NewTask("OUTSIDE", fixture.OutsideId);
            var readOnly = fixture.ReadOnlyProjects.Select(project => NewTask(project.Name, project.ProjectId, true)).ToList();
            var readOnlyPredecessors = fixture.ReadOnlyProjects.Select(project => NewTask($"{project.Name}-PREDECESSOR", project.ProjectId)).ToList();
            db.Tasks.AddRange(new[] { assigned, unassigned, predecessor, blocked, completed, outside }.Concat(readOnly).Concat(readOnlyPredecessors));
            await db.SaveChangesAsync();
            assignedId = assigned.Id; unassignedId = unassigned.Id; predecessorId = predecessor.Id;
            blockedId = blocked.Id; completedId = completed.Id; outsideId = outside.Id;
            readOnlyIds.AddRange(readOnly.Select(task => task.Id));
            readOnlyPredecessorIds.AddRange(readOnlyPredecessors.Select(task => task.Id));
            db.TaskDependencies.Add(new TaskDependency { TaskId = blockedId, PredecessorTaskId = predecessorId });
            db.TaskProgressHistories.Add(new TaskProgressHistory
            {
                TaskId = assignedId, UpdatedById = user.Id, PreviousStatus = ConstructionProjectManagement.Models.TaskStatus.Pending,
                NewStatus = ConstructionProjectManagement.Models.TaskStatus.InProgress, NewPercentComplete = 20, Note = "T06-HISTORY"
            });
            db.TaskAuditLogs.Add(new TaskAuditLog { TaskId = assignedId, PerformedById = user.Id, Action = "T06-AUDIT" });
            db.TaskComments.Add(new TaskComment
            {
                TaskId = assignedId, AuthorId = user.Id, Content = "T06-COMMENT", IsBlocker = true, ProgressSnapshot = 20
            });
            await db.SaveChangesAsync();
        }

        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}"))!.Status);
        var rows = await page.Locator("tbody").InnerTextAsync();
        Assert.Contains($"{marker}-ASSIGNED", rows);
        Assert.Equal(role != "Worker", rows.Contains($"{marker}-UNASSIGNED", StringComparison.Ordinal));
        Assert.Equal(role == "Admin", rows.Contains($"{marker}-OUTSIDE", StringComparison.Ordinal));
        var unassignedChart = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/ChartData?Keyword={marker}-UNASSIGNED");
        Assert.Equal(200, unassignedChart.Status);
        Assert.Equal(role == "Worker", (await unassignedChart.TextAsync()).Trim() == "[]");
        var outsideChart = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/ChartData?Keyword={marker}-OUTSIDE");
        Assert.Equal(role != "Admin", (await outsideChart.TextAsync()).Trim() == "[]");
        var assignedRow = page.Locator("tbody tr").Filter(new() { HasText = $"{marker}-ASSIGNED" });
        Assert.Equal(progress ? 1 : 0, await assignedRow.Locator("input[type=number][name=PercentComplete]").CountAsync());
        var blockedRow = page.Locator("tbody tr").Filter(new() { HasText = $"{marker}-BLOCKED" });
        Assert.Contains("Bị chặn", await blockedRow.InnerTextAsync());
        Assert.Equal(0, await blockedRow.Locator("input[type=number][name=PercentComplete]").CountAsync());
        var completedRow = page.Locator("tbody tr").Filter(new() { HasText = $"{marker}-COMPLETED" });
        Assert.Equal(cancel ? 1 : 0, await completedRow.GetByRole(AriaRole.Button, new() { Name = "Mở lại" }).CountAsync());
        var unassignedRow = page.Locator("tbody tr").Filter(new() { HasText = $"{marker}-UNASSIGNED" });
        if (role != "Worker")
            Assert.Equal(role is "Admin" or "Project Director" or "Project Manager" or "Site Manager" ? 1 : 0,
                await unassignedRow.Locator("input[type=number][name=PercentComplete]").CountAsync());
        foreach (var project in fixture.ReadOnlyProjects)
        {
            var row = page.Locator("tbody tr").Filter(new() { HasText = $"{marker}-{project.Name}" })
                .Filter(new() { HasNotText = "-PREDECESSOR" });
            Assert.Equal(1, await row.CountAsync());
            Assert.Equal(0, await row.Locator("form").CountAsync());
        }

        Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Progress/Details/{assignedId}"))!.Status);
        var text = await page.Locator("main").InnerTextAsync();
        Assert.Contains("T06-HISTORY", text);
        Assert.Contains("T06-COMMENT", text);
        Assert.Equal(audit, text.Contains("T06-AUDIT", StringComparison.Ordinal));
        Assert.Equal(comment ? 1 : 0, await page.Locator("#commentContent").CountAsync());
        Assert.Equal(dependencies ? 1 : 0, await page.Locator("#predecessorId").CountAsync());
        var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        Assert.NotNull(token);
        if (comment)
        {
            await page.Locator("#commentContent").FillAsync($"{marker}-POSTED");
            await page.GetByRole(AriaRole.Button, new() { Name = "Gửi bình luận" }).ClickAsync();
            Assert.Contains($"{marker}-POSTED", await page.Locator("main").InnerTextAsync());
        }
        else
        {
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("taskId", assignedId.ToString());
            form.Append("content", $"{marker}-FORBIDDEN");
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/AddComment", new() { Form = form });
        }
        if (dependencies)
        {
            await page.Locator("#predecessorId").SelectOptionAsync(predecessorId.ToString());
            await page.GetByRole(AriaRole.Button, new() { Name = "Thêm phụ thuộc" }).ClickAsync();
            Assert.Contains($"{marker}-PREDECESSOR", await page.Locator("main").InnerTextAsync());
        }
        else
        {
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("taskId", assignedId.ToString());
            form.Append("predecessorId", predecessorId.ToString());
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/AddDependency", new() { Form = form });
        }
        for (var index = 0; index < readOnlyIds.Count; index++)
        {
            var readOnlyId = readOnlyIds[index];
            Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Progress/Details/{readOnlyId}"))!.Status);
            Assert.Equal(0, await page.Locator("#commentContent").CountAsync());
            Assert.Equal(0, await page.Locator("#predecessorId").CountAsync());
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("taskId", readOnlyId.ToString());
            form.Append("content", $"{marker}-READONLY");
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/AddComment", new() { Form = form });
            var dependencyForm = context.APIRequest.CreateFormData();
            dependencyForm.Append("__RequestVerificationToken", token!);
            dependencyForm.Append("taskId", readOnlyId.ToString());
            dependencyForm.Append("predecessorId", readOnlyPredecessorIds[index].ToString());
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/AddDependency", new() { Form = dependencyForm });
            Guid stamp;
            await using (var readDb = fixture.OpenDb())
                stamp = (await readDb.Tasks.AsNoTracking().SingleAsync(item => item.Id == readOnlyId)).ConcurrencyStamp;
            var update = context.APIRequest.CreateFormData();
            update.Append("__RequestVerificationToken", token!);
            update.Append("TaskId", readOnlyId.ToString());
            update.Append("ConcurrencyStamp", stamp.ToString());
            update.Append("PercentComplete", "20");
            update.Append("Status", "InProgress");
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/Update", new() { Form = update });
        }
        Assert.Equal(role == "Worker" ? 404 : 200,
            (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/Details/{unassignedId}")).Status);
        Assert.Equal(role == "Admin" ? 200 : 404,
            (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/Details/{outsideId}")).Status);

        Guid cancelStamp, reopenStamp;
        await using (var readDb = fixture.OpenDb())
        {
            cancelStamp = (await readDb.Tasks.AsNoTracking().SingleAsync(item => item.Id == assignedId)).ConcurrencyStamp;
            reopenStamp = (await readDb.Tasks.AsNoTracking().SingleAsync(item => item.Id == completedId)).ConcurrencyStamp;
        }
        var cancelForm = context.APIRequest.CreateFormData();
        cancelForm.Append("__RequestVerificationToken", token!);
        cancelForm.Append("TaskId", assignedId.ToString());
        cancelForm.Append("ConcurrencyStamp", cancelStamp.ToString());
        cancelForm.Append("PercentComplete", "20");
        cancelForm.Append("Status", "Cancelled");
        cancelForm.Append("Reason", "Lý do hủy công việc kiểm thử");
        await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/Update", new() { Form = cancelForm });
        var reopenForm = context.APIRequest.CreateFormData();
        reopenForm.Append("__RequestVerificationToken", token!);
        reopenForm.Append("TaskId", completedId.ToString());
        reopenForm.Append("ConcurrencyStamp", reopenStamp.ToString());
        reopenForm.Append("PercentComplete", "80");
        reopenForm.Append("Status", "InProgress");
        reopenForm.Append("Reopen", "true");
        reopenForm.Append("Reason", "Lý do mở lại công việc kiểm thử");
        await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/Update", new() { Form = reopenForm });

        await using var verify = fixture.OpenDb();
        Assert.Equal(comment ? 1 : 0, await verify.TaskComments.CountAsync(item => item.TaskId == assignedId && item.Content == $"{marker}-POSTED"));
        Assert.False(await verify.TaskComments.AnyAsync(item => item.Content == $"{marker}-FORBIDDEN" || item.Content == $"{marker}-READONLY"));
        Assert.Equal(dependencies, await verify.TaskDependencies.AnyAsync(item => item.TaskId == assignedId && item.PredecessorTaskId == predecessorId));
        Assert.False(await verify.TaskDependencies.AnyAsync(item => readOnlyIds.Contains(item.TaskId)));
        Assert.All(await verify.Tasks.Where(item => readOnlyIds.Contains(item.Id)).ToListAsync(), item => Assert.Equal(0m, item.PercentComplete));
        Assert.Equal(cancel ? ConstructionProjectManagement.Models.TaskStatus.Cancelled : ConstructionProjectManagement.Models.TaskStatus.InProgress,
            (await verify.Tasks.SingleAsync(item => item.Id == assignedId)).Status);
        Assert.Equal(cancel ? ConstructionProjectManagement.Models.TaskStatus.InProgress : ConstructionProjectManagement.Models.TaskStatus.Completed,
            (await verify.Tasks.SingleAsync(item => item.Id == completedId)).Status);
    }

    [Theory]
    [InlineData("Admin", true, true)]
    [InlineData("Project Director", true, true)]
    [InlineData("Project Manager", true, true)]
    [InlineData("Site Manager", true, true)]
    [InlineData("Supervising Engineer", true, true)]
    [InlineData("Worker", true, false)]
    [InlineData("Materials Staff", false, false)]
    [InlineData("Client Representative", false, false)]
    public async Task Tracking_EightRoles_InspectionAt100(string role, bool progress, bool quality)
    {
        var marker = $"T06-INSPECTION-{role.Replace(" ", "-")}-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails[role]);
            var task = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker, RequiresInspection = true,
                PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10),
                Status = ConstructionProjectManagement.Models.TaskStatus.InProgress, PercentComplete = 20,
                Assignments = [new TaskAssignment { UserId = user.Id }]
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var row = page.Locator("tbody tr").Filter(new() { HasText = marker });
        Assert.Equal(progress ? 1 : 0, await row.Locator("input[type=number][name=PercentComplete]").CountAsync());
        if (progress)
        {
            await row.Locator("input[name=PercentComplete]").FillAsync("100");
            await row.Locator("select[name=Status]").SelectOptionAsync("PendingInspection");
            await row.GetByRole(AriaRole.Button, new() { Name = "Lưu tiến độ" }).ClickAsync();
            Assert.Equal(quality ? 1 : 0, await page.Locator(".alert-success").CountAsync());
            Assert.Equal(quality ? 0 : 1, await page.Locator(".alert-danger").CountAsync());
        }
        else
        {
            var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
            Guid stamp;
            await using (var readDb = fixture.OpenDb())
                stamp = (await readDb.Tasks.AsNoTracking().SingleAsync(item => item.Id == taskId)).ConcurrencyStamp;
            var request = context.APIRequest.CreateFormData();
            request.Append("__RequestVerificationToken", token!);
            request.Append("TaskId", taskId.ToString());
            request.Append("ConcurrencyStamp", stamp.ToString());
            request.Append("PercentComplete", "100");
            request.Append("Status", "PendingInspection");
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/Update", new() { Form = request });
        }
        int? inspectionId;
        await using (var db = fixture.OpenDb())
        {
            var task = await db.Tasks.AsNoTracking().SingleAsync(item => item.Id == taskId);
            Assert.Equal(quality ? 100m : 20m, task.PercentComplete);
            Assert.Equal(quality ? ConstructionProjectManagement.Models.TaskStatus.PendingInspection :
                ConstructionProjectManagement.Models.TaskStatus.InProgress, task.Status);
            inspectionId = await db.InspectionRequests.Where(item => item.TaskId == taskId).Select(item => (int?)item.Id).SingleOrDefaultAsync();
        }
        Assert.Equal(quality, inspectionId.HasValue);
        if (quality)
        {
            await page.GotoAsync($"{fixture.BaseUrl}/Quality");
            var form = page.Locator($"form[action$='/RecordInspection']:has(input[name=id][value='{inspectionId}'])");
            Assert.Equal(1, await form.CountAsync());
            await form.Locator("select[name=Result]").SelectOptionAsync("Passed");
            await form.GetByRole(AriaRole.Button, new() { Name = "Lưu" }).ClickAsync();
            await using var verify = fixture.OpenDb();
            var task = await verify.Tasks.SingleAsync(item => item.Id == taskId);
            Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.Completed, task.Status);
            Assert.Equal(InspectionResult.Passed, (await verify.InspectionRequests.SingleAsync(item => item.Id == inspectionId)).Result);
        }
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", true)]
    [InlineData("Project Manager", true)]
    [InlineData("Site Manager", true)]
    [InlineData("Supervising Engineer", true)]
    [InlineData("Worker", false)]
    [InlineData("Materials Staff", false)]
    [InlineData("Client Representative", true)]
    public async Task Tracking_ReadOnlyInspectionCannotBeRecorded(string role, bool canViewQuality)
    {
        var marker = $"T06-READONLY-Q-{role.Replace(" ", "-")}-{Guid.NewGuid():N}";
        var inspectionIds = new List<int>();
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails[role]);
            foreach (var project in fixture.ReadOnlyProjects)
            {
                var task = new TaskEntity
                {
                    ProjectId = project.ProjectId, Title = $"{marker}-{project.Name}", RequiresInspection = true,
                    PlannedStartDate = DateTime.Today, PlannedEndDate = DateTime.Today.AddDays(10),
                    Status = ConstructionProjectManagement.Models.TaskStatus.PendingInspection, PercentComplete = 100
                };
                db.Tasks.Add(task);
                await db.SaveChangesAsync();
                var inspection = new InspectionRequest { ProjectId = project.ProjectId, TaskId = task.Id, RequestedById = user.Id };
                db.InspectionRequests.Add(inspection);
                await db.SaveChangesAsync();
                inspectionIds.Add(inspection.Id);
            }
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        var qualityPage = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Quality", new() { MaxRedirects = 0 });
        Assert.Equal(canViewQuality ? 200 : 302, qualityPage.Status);
        if (canViewQuality)
        {
            await page.GotoAsync($"{fixture.BaseUrl}/Quality");
            foreach (var id in inspectionIds)
                Assert.Equal(0, await page.Locator($"form[action$='/RecordInspection']:has(input[name=id][value='{id}'])").CountAsync());
        }
        var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        foreach (var id in inspectionIds)
        {
            var form = context.APIRequest.CreateFormData();
            form.Append("__RequestVerificationToken", token!);
            form.Append("id", id.ToString());
            form.Append("Result", "Passed");
            await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Quality/RecordInspection", new() { Form = form });
        }
        await using var verify = fixture.OpenDb();
        Assert.All(await verify.InspectionRequests.Where(item => inspectionIds.Contains(item.Id)).ToListAsync(),
            item => Assert.Equal(InspectionResult.Pending, item.Result));
        Assert.All(await verify.Tasks.Where(item => item.Title.StartsWith(marker)).ToListAsync(),
            item => Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.PendingInspection, item.Status));
    }

    [Fact]
    public async Task Client_MismatchedOrganizationCannotSeeTaskOrChart()
    {
        var marker = $"CLIENT-MISMATCH-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var task = new TaskEntity { ProjectId = fixture.ActiveId, Title = marker, PlannedEndDate = DateTime.Today.AddDays(1) };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Mismatch");
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        Assert.DoesNotContain(marker, await page.Locator("tbody").InnerTextAsync());
        Assert.Equal(404, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/Details/{taskId}")).Status);
        var chart = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Progress/ChartData?Keyword={marker}");
        Assert.Equal(200, chart.Status);
        Assert.Equal("[]", (await chart.TextAsync()).Trim());
    }

    [Fact]
    public async Task Progress_FiltersAndPaginates()
    {
        var marker = $"TRACK-{Guid.NewGuid():N}";
        await using (var db = fixture.OpenDb())
        {
            for (var index = 0; index < 25; index++)
                db.Tasks.Add(new TaskEntity
                {
                    ProjectId = fixture.ActiveId, Title = $"{marker}-{index}",
                    PlannedStartDate = DateTime.Today.AddDays(-10), PlannedEndDate = DateTime.Today.AddDays(-1)
                });
            await db.SaveChangesAsync();
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Progress"))!.Status);
        await page.Locator("#ProjectId").SelectOptionAsync(fixture.ActiveId.ToString());
        await page.Locator("#Keyword").FillAsync(marker);
        await page.Locator("input[name=OnlyOverdue]").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Lọc" }).ClickAsync();
        Assert.Equal(20, await page.Locator("tbody tr").CountAsync());
        Assert.Contains("25 công việc", await page.Locator("nav[aria-label='Phân trang tiến độ']").InnerTextAsync());
        await page.GetByRole(AriaRole.Link, new() { Name = "2", Exact = true }).ClickAsync();
        Assert.Equal(5, await page.Locator("tbody tr").CountAsync());
    }

    [Fact]
    public async Task Progress_OverdueUpdateRequiresDelayReason()
    {
        var marker = $"T06-LATE-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails["Project Manager"]);
            var task = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker,
                PlannedStartDate = DateTime.Today.AddDays(-10), PlannedEndDate = DateTime.Today.AddDays(-1),
                Status = ConstructionProjectManagement.Models.TaskStatus.InProgress, PercentComplete = 20,
                Assignments = [new TaskAssignment { UserId = user.Id }]
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var row = page.Locator("tbody tr").Filter(new() { HasText = marker });
        Assert.Contains("Chậm", await row.InnerTextAsync());
        await row.Locator("input[name=PercentComplete]").FillAsync("30");
        await row.GetByRole(AriaRole.Button, new() { Name = "Lưu tiến độ" }).ClickAsync();
        Assert.Contains("lý do chậm", await page.Locator(".alert-danger").InnerTextAsync());
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        row = page.Locator("tbody tr").Filter(new() { HasText = marker });
        await row.Locator("input[name=PercentComplete]").FillAsync("30");
        await row.Locator("input[name=DelayReason]").FillAsync("Chậm do điều kiện thời tiết");
        await row.GetByRole(AriaRole.Button, new() { Name = "Lưu tiến độ" }).ClickAsync();
        Assert.Equal(1, await page.Locator(".alert-success").CountAsync());
        await using var verify = fixture.OpenDb();
        Assert.Equal(30m, (await verify.Tasks.SingleAsync(item => item.Id == taskId)).PercentComplete);
        Assert.Equal("Chậm do điều kiện thời tiết",
            (await verify.TaskProgressHistories.SingleAsync(item => item.TaskId == taskId)).DelayReason);
    }

    [Fact]
    public async Task Progress_ShowsDependencyAndPostsBlockerComment()
    {
        var marker = $"BLOCK-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var predecessor = new TaskEntity { ProjectId = fixture.ActiveId, Title = $"{marker}-predecessor", PlannedEndDate = DateTime.Today.AddDays(10) };
            var task = new TaskEntity { ProjectId = fixture.ActiveId, Title = marker, PlannedEndDate = DateTime.Today.AddDays(10) };
            db.Tasks.AddRange(predecessor, task);
            await db.SaveChangesAsync();
            db.TaskDependencies.Add(new TaskDependency { TaskId = task.Id, PredecessorTaskId = predecessor.Id });
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var row = page.Locator("tbody tr").Filter(new() { HasText = marker }).Filter(new() { HasNotText = "-predecessor" });
        Assert.Contains("Bị chặn", await row.InnerTextAsync());
        Assert.Equal(0, await row.Locator("input[type=number][name=PercentComplete]").CountAsync());
        Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Progress/Details/{taskId}"))!.Status);
        Assert.Contains($"{marker}-predecessor", await page.Locator("main").InnerTextAsync());
        await page.Locator("#commentContent").FillAsync("Vướng mắc cần xử lý");
        await page.Locator("input[name=isBlocker]").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Gửi bình luận" }).ClickAsync();
        Assert.Contains("Vướng mắc cần xử lý", await page.Locator("main").InnerTextAsync());
        await using var verify = fixture.OpenDb();
        Assert.True(await verify.TaskComments.AnyAsync(item => item.TaskId == taskId && item.IsBlocker));
    }

    [Fact]
    public async Task Progress_ChartApiFailureDisplaysMessage()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.RouteAsync("**/Progress/ChartData**", route => route.FulfillAsync(new() { Status = 500 }));
        await page.GotoAsync($"{fixture.BaseUrl}/Progress");
        await page.Locator("#chartMessage").WaitForAsync();
        await Assertions.Expect(page.Locator("#chartMessage")).ToContainTextAsync("Không thể tải biểu đồ");
    }

    [Fact]
    public async Task Progress_ReportsConflictFromStaleForm()
    {
        var marker = $"CONFLICT-{Guid.NewGuid():N}";
        int taskId;
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails["Project Manager"]);
            var task = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker, PlannedEndDate = DateTime.Today.AddDays(10),
                Assignments = [new TaskAssignment { UserId = user.Id }]
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={marker}");
        var row = page.Locator("tbody tr").Filter(new() { HasText = marker });
        await row.Locator("input[name=PercentComplete]").FillAsync("25");
        await row.Locator("select[name=Status]").SelectOptionAsync("InProgress");
        await using (var db = fixture.OpenDb())
        {
            var task = await db.Tasks.SingleAsync(item => item.Id == taskId);
            task.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        await row.GetByRole(AriaRole.Button, new() { Name = "Lưu tiến độ" }).ClickAsync();
        Assert.Contains("tải lại", await page.Locator(".alert-danger").InnerTextAsync());
        await using var verify = fixture.OpenDb();
        Assert.Equal(0m, (await verify.Tasks.SingleAsync(item => item.Id == taskId)).PercentComplete);
    }
}
