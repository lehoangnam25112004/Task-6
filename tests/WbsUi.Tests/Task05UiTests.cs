using ConstructionProjectManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TaskEntity = ConstructionProjectManagement.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace WbsUi.Tests;

public sealed class Task05UiTests(WbsUiFixture fixture) : IClassFixture<WbsUiFixture>
{
    [Fact]
    public async Task TaskForm_LoadsScopedOptionsAndReadOnlyControls()
    {
        var (active, outside, readOnly) = await SeedTasksAsync();
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Project Manager");
        var response = await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Create");
        Assert.Equal(200, response!.Status);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.Locator("#taskProjectSelect").SelectOptionAsync(fixture.ActiveId.ToString());
        await page.WaitForURLAsync(url => url.Contains($"projectId={fixture.ActiveId}"));
        Assert.Contains("TASK05-ACTIVE", await page.Locator("#ParentTaskId").InnerTextAsync());
        Assert.DoesNotContain("TASK05-OUTSIDE", await page.Locator("#ParentTaskId").InnerTextAsync());
        Assert.DoesNotContain("UI-COMPLETED", await page.Locator("#taskProjectSelect").InnerTextAsync());
        await page.Locator("#taskProjectSelect").SelectOptionAsync("");
        await page.WaitForURLAsync(url => !url.Contains("projectId="));
        Assert.DoesNotContain("TASK05-ACTIVE", await page.Locator("#ParentTaskId").InnerTextAsync());

        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{active}");
        Assert.Equal(1, await page.GetByRole(AriaRole.Link, new() { Name = "Cập nhật" }).CountAsync());
        await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{readOnly}");
        Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Cập nhật" }).CountAsync());
        var denied = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/Details/{outside}", new() { MaxRedirects = 0 });
        Assert.Equal(404, denied.Status);
        denied = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/Edit/{readOnly}", new() { MaxRedirects = 0 });
        Assert.Equal(302, denied.Status);
        Assert.Contains("/Account/AccessDenied", denied.Headers["location"], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Admin", true, true, true)]
    [InlineData("Project Director", true, true, true)]
    [InlineData("Project Manager", true, true, true)]
    [InlineData("Site Manager", false, true, true)]
    [InlineData("Supervising Engineer", false, true, true)]
    [InlineData("Materials Staff", false, false, false)]
    [InlineData("Worker", false, true, false)]
    [InlineData("Client Representative", false, false, true)]
    [InlineData("AdminPM", true, true, true)]
    [InlineData("AdminPD", true, true, true)]
    public async Task RoleMatrix_TaskProgressAndQuality(string role, bool manageTask, bool progress, bool quality)
    {
        var marker = $"MATRIX-{role.Replace(" ", "-")}-{Guid.NewGuid():N}";
        int activeId;
        int outsideId;
        int inspectionId;
        var readOnlyIds = new List<int>();
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails[role]);
            var active = new TaskEntity
            {
                ProjectId = fixture.ActiveId, Title = marker, Priority = TaskPriority.High, RequiresInspection = true,
                PlannedStartDate = DateTime.UtcNow.Date, PlannedEndDate = DateTime.UtcNow.Date.AddDays(10),
                Assignments = [new TaskAssignment { UserId = user.Id }]
            };
            db.Tasks.Add(active);
            var outsideTask = new TaskEntity
            {
                ProjectId = fixture.OutsideId, Title = $"{marker}-OUTSIDE",
                PlannedStartDate = DateTime.UtcNow.Date, PlannedEndDate = DateTime.UtcNow.Date.AddDays(10)
            };
            db.Tasks.Add(outsideTask);
            foreach (var project in fixture.ReadOnlyProjects)
                db.Tasks.Add(new TaskEntity
                {
                    ProjectId = project.ProjectId, Title = $"{marker}-{project.Name}",
                    PlannedStartDate = DateTime.UtcNow.Date, PlannedEndDate = DateTime.UtcNow.Date.AddDays(10)
                });
            await db.SaveChangesAsync();
            activeId = active.Id;
            outsideId = outsideTask.Id;
            var inspection = new InspectionRequest { ProjectId = fixture.ActiveId, TaskId = activeId, RequestedById = user.Id };
            db.InspectionRequests.Add(inspection);
            await db.SaveChangesAsync();
            inspectionId = inspection.Id;
            readOnlyIds = await db.Tasks.Where(task => task.Title.StartsWith(marker + "-UI-"))
                .Select(task => task.Id).ToListAsync();
        }

        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        try
        {
            Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Tasks"))!.Status);
            Assert.Contains(marker, await page.Locator("main").InnerTextAsync());
            Assert.Equal(manageTask ? 1 : 0, await page.GetByRole(AriaRole.Link, new() { Name = "Tạo công việc" }).CountAsync());
            await page.Locator("#ProjectId").SelectOptionAsync(fixture.ActiveId.ToString());
            await page.Locator("#Status").SelectOptionAsync("0");
            await page.Locator("#Priority").SelectOptionAsync("2");
            await page.Locator("#Keyword").FillAsync(marker);
            await page.GetByRole(AriaRole.Button, new() { Name = "Tìm kiếm" }).ClickAsync();
            Assert.Equal(1, await page.Locator("tbody tr").CountAsync());
            Assert.Contains(marker, await page.Locator("tbody").InnerTextAsync());
            Assert.Contains("Chờ xử lý", await page.Locator("tbody").InnerTextAsync());
            Assert.Contains("Cao", await page.Locator("tbody").InnerTextAsync());
            Assert.Equal("2", await page.Locator("#Priority").InputValueAsync());
            Assert.Equal("0", await page.Locator("#Status").InputValueAsync());

            await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{activeId}");
            Assert.Equal(manageTask ? 1 : 0, await page.GetByRole(AriaRole.Link, new() { Name = "Cập nhật" }).CountAsync());
            foreach (var id in readOnlyIds)
            {
                Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Tasks/Details/{id}"))!.Status);
                Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Cập nhật" }).CountAsync());
            }
            Assert.Equal(3, readOnlyIds.Count);
            var outside = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks?ProjectId={fixture.OutsideId}", new() { MaxRedirects = 0 });
            var outsideHtml = await outside.TextAsync();
            Assert.Equal(200, outside.Status);
            var global = role is "Admin" or "AdminPM" or "AdminPD";
            Assert.Equal(global, outsideHtml.Contains($"{marker}-OUTSIDE", StringComparison.Ordinal));
            var outsideDetails = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/Details/{outsideId}", new() { MaxRedirects = 0 });
            Assert.Equal(global ? 200 : 404, outsideDetails.Status);

            await page.GotoAsync($"{fixture.BaseUrl}/Progress?Keyword={Uri.EscapeDataString(marker)}");
            var activeRow = page.Locator("tbody tr").Filter(new() { HasText = marker })
                .Filter(new() { HasNotText = "-UI-" }).Filter(new() { HasNotText = $"{marker}-OUTSIDE" });
            Assert.Equal(progress ? 1 : 0, await activeRow.Locator("form").CountAsync());
            foreach (var project in fixture.ReadOnlyProjects)
                Assert.Equal(0, await page.Locator("tbody tr").Filter(new() { HasText = $"{marker}-{project.Name}" }).Locator("form").CountAsync());
            var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
            var readOnlyForm = context.APIRequest.CreateFormData();
            readOnlyForm.Append("__RequestVerificationToken", token!);
            readOnlyForm.Append("TaskId", readOnlyIds[0].ToString());
            readOnlyForm.Append("PercentComplete", "25");
            readOnlyForm.Append("Status", "InProgress");
            var blocked = await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/Update", new() { Form = readOnlyForm, MaxRedirects = 0 });
            Assert.Equal(role == "Worker" ? 404 : 302, blocked.Status);
            if (role != "Worker")
                Assert.Contains("/Account/AccessDenied", blocked.Headers["location"], StringComparison.OrdinalIgnoreCase);
            if (progress)
            {
                await activeRow.Locator("input[name=PercentComplete]").FillAsync("25");
                await activeRow.Locator("select[name=Status]").SelectOptionAsync("InProgress");
                await activeRow.GetByRole(AriaRole.Button, new() { Name = "Lưu tiến độ" }).ClickAsync();
                await using var verify = fixture.OpenDb();
                var changed = await verify.Tasks.SingleAsync(task => task.Id == activeId);
                Assert.Equal(25m, changed.PercentComplete);
                Assert.Equal(ConstructionProjectManagement.Models.TaskStatus.InProgress, changed.Status);
            }

            var qualityResponse = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Quality", new() { MaxRedirects = 0 });
            Assert.Equal(quality ? 200 : 302, qualityResponse.Status);
            if (quality)
            {
                var html = await qualityResponse.TextAsync();
                Assert.Contains(marker, html);
                await page.GotoAsync($"{fixture.BaseUrl}/Quality");
                var form = page.Locator($"form[action$='/RecordInspection']:has(input[name=id][value='{inspectionId}'])");
                Assert.Equal(role == "Client Representative" ? 0 : 1, await form.CountAsync());
                if (role == "Client Representative")
                {
                    Assert.DoesNotContain("TASK05-INCIDENT", html);
                    Assert.DoesNotContain("TASK05-RFI", html);
                    Assert.DoesNotContain("/Quality/RecordInspection", html);
                }
            }
        }
        catch
        {
            fixture.SaveLog($"task05-{role.Replace(" ", "-")}");
            await page.ScreenshotAsync(new() { Path = Path.Combine(fixture.Root, "tests", "WbsUi.Tests", "TestResults", $"task05-{role.Replace(" ", "-")}.png") });
            throw;
        }
    }

    [Theory]
    [InlineData("Worker")]
    [InlineData("Supervising Engineer")]
    public async Task Progress_UnassignedUserCannotPost(string role)
    {
        var (active, _, _) = await SeedTasksAsync();
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        await page.GotoAsync($"{fixture.BaseUrl}/Progress");
        var row = page.Locator("tbody tr").Filter(new() { Has = page.Locator($"input[name=TaskId][value='{active}']") });
        Assert.Equal(0, await row.CountAsync());
        var token = await page.Locator("form[action*='/Account/Logout'] input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        var form = context.APIRequest.CreateFormData();
        form.Append("__RequestVerificationToken", token!);
        form.Append("TaskId", active.ToString());
        form.Append("PercentComplete", "25");
        form.Append("Status", "InProgress");
        var response = await context.APIRequest.PostAsync($"{fixture.BaseUrl}/Progress/Update", new() { Form = form, MaxRedirects = 0 });
        Assert.Equal(role == "Worker" ? 404 : 302, response.Status);
        if (role != "Worker")
            Assert.Contains("/Account/AccessDenied", response.Headers["location"], StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.OpenDb();
        Assert.Equal(0m, (await db.Tasks.SingleAsync(task => task.Id == active)).PercentComplete);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Project Director", false)]
    [InlineData("Project Manager", false)]
    public async Task Completion_OpenTasksRespectScope(string role, bool global)
    {
        var marker = $"COMPLETION-{Guid.NewGuid():N}";
        await using (var db = fixture.OpenDb())
        {
            db.Tasks.AddRange(
                new TaskEntity { ProjectId = fixture.ActiveId, Title = $"{marker}-ACTIVE", PlannedEndDate = DateTime.UtcNow.AddDays(1) },
                new TaskEntity { ProjectId = fixture.OutsideId, Title = $"{marker}-OUTSIDE", PlannedEndDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        Assert.Equal(200, (await page.GotoAsync($"{fixture.BaseUrl}/Completion"))!.Status);
        var text = await page.Locator("main").InnerTextAsync();
        Assert.Contains($"{marker}-ACTIVE", text);
        Assert.Equal(global, text.Contains($"{marker}-OUTSIDE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Mismatch")]
    [InlineData("InactiveMember")]
    public async Task ScopeEdgeCases_AreEnforced(string role)
    {
        var (active, outside, _) = await SeedTasksAsync();
        int inactiveMemberTask;
        await using (var db = fixture.OpenDb())
        {
            var inactiveTask = new TaskEntity { ProjectId = fixture.InactiveMemberId, Title = "TASK05-INACTIVE-MEMBER" };
            db.Tasks.Add(inactiveTask);
            await db.SaveChangesAsync();
            inactiveMemberTask = inactiveTask.Id;
        }
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, role);
        var response = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks", new() { MaxRedirects = 0 });
        if (role == "Inactive")
        {
            Assert.Equal(302, response.Status);
            return;
        }
        Assert.Equal(200, response.Status);
        Assert.DoesNotContain("TASK05-OUTSIDE", await response.TextAsync());
        var task = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/Details/{outside}", new() { MaxRedirects = 0 });
        Assert.Equal(404, task.Status);
        if (role == "Mismatch")
        {
            var quality = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Quality", new() { MaxRedirects = 0 });
            Assert.DoesNotContain("TASK05-ACTIVE", await quality.TextAsync());
            Assert.Equal(404, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/Details/{active}", new() { MaxRedirects = 0 })).Status);
        }
        if (role == "InactiveMember")
            Assert.Equal(404, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Tasks/Details/{inactiveMemberTask}", new() { MaxRedirects = 0 })).Status);
    }

    [Fact]
    public async Task ProgressAndQuality_ClientIsReadOnlyAndSeesOnlyInspections()
    {
        var (active, _, _) = await SeedTasksAsync();
        await using (var db = fixture.OpenDb())
        {
            var user = await db.Users.SingleAsync(item => item.Email == fixture.Emails["Project Manager"]);
            db.InspectionRequests.Add(new InspectionRequest
            {
                ProjectId = fixture.ActiveId, TaskId = active, RequestedById = user.Id
            });
            db.Incidents.Add(new Incident
            {
                ProjectId = fixture.ActiveId, Title = "TASK05-INCIDENT", Description = "D",
                ReportedById = user.Id
            });
            db.RfiRequests.Add(new RfiRequest
            {
                ProjectId = fixture.ActiveId, Subject = "TASK05-RFI", Question = "Q",
                CreatedById = user.Id
            });
            await db.SaveChangesAsync();
        }

        await using var context = await fixture.Browser.NewContextAsync();
        var page = await fixture.LoginAsync(context, "Client Representative");
        var response = await page.GotoAsync($"{fixture.BaseUrl}/Progress");
        Assert.Equal(200, response!.Status);
        Assert.Equal(0, await page.Locator("main form[action$='/Progress/Update']").CountAsync());
        response = await page.GotoAsync($"{fixture.BaseUrl}/Quality");
        Assert.Equal(200, response!.Status);
        var main = await page.Locator("main").InnerTextAsync();
        Assert.Contains("TASK05-ACTIVE", main);
        Assert.DoesNotContain("TASK05-INCIDENT", main);
        Assert.DoesNotContain("TASK05-RFI", main);
        Assert.Equal(0, await page.Locator("main form[action$='/RecordInspection']").CountAsync());
        var denied = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Quality/CreateInspection", new() { MaxRedirects = 0 });
        Assert.Equal(302, denied.Status);
        Assert.Contains("/Account/AccessDenied", denied.Headers["location"], StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(int Active, int Outside, int ReadOnly)> SeedTasksAsync()
    {
        await using var db = fixture.OpenDb();
        TaskEntity NewTask(int projectId, string title) => new()
        {
            ProjectId = projectId,
            Title = title,
            RequiresInspection = true,
            PlannedStartDate = DateTime.UtcNow.Date,
            PlannedEndDate = DateTime.UtcNow.Date.AddDays(10)
        };
        var active = NewTask(fixture.ActiveId, "TASK05-ACTIVE");
        var outside = NewTask(fixture.OutsideId, "TASK05-OUTSIDE");
        var readOnly = NewTask(fixture.ReadOnlyProjects.Single(project => project.Name == "UI-COMPLETED").ProjectId, "TASK05-READONLY");
        db.Tasks.AddRange(active, outside, readOnly);
        await db.SaveChangesAsync();
        return (active.Id, outside.Id, readOnly.Id);
    }
}
