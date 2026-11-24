using ConstructionProjectManagement.Data;
using ConstructionProjectManagement.Models;
using ConstructionProjectManagement.Services;
using ConstructionProjectManagement.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = LicenseType.Community;

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IProjectDocumentService, ProjectDocumentService>();
builder.Services.AddScoped<IWbsService, WbsService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IConstructionLogService, ConstructionLogService>();
builder.Services.AddScoped<IQualityService, QualityService>();
builder.Services.AddScoped<ICompletionService, CompletionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DbInitializer.ApplyMigrationsAsync(dbContext, app.Configuration);
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await DbInitializer.SeedRolesAsync(roleManager);
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DbInitializer");
    await DbInitializer.SeedAdminAsync(
        userManager,
        app.Configuration,
        logger);
    if (Task03TestDataSeeder.IsEnabled(app.Configuration))
    {
        await Task03TestDataSeeder.SeedAsync(
            dbContext,
            userManager,
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
            logger);
    }
    var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
    await DbInitializer.SeedSampleDataAsync(dbContext, projectService, app.Configuration);

    // Dữ liệu kiểm thử TASK-02: chỉ Development và phải bật cờ cấu hình rõ ràng.
    if (app.Environment.IsDevelopment())
    {
        if (Task02TestDataSeeder.IsEnabled(app.Configuration, "CPMS_REMOVE_TASK02_TEST_DATA"))
        {
            await Task02TestDataSeeder.RemoveAsync(dbContext, logger);
        }
        else if (Task02TestDataSeeder.IsEnabled(app.Configuration, "CPMS_SEED_TASK02_TEST_DATA"))
        {
            await Task02TestDataSeeder.SeedAsync(
                dbContext,
                projectService,
                scope.ServiceProvider.GetRequiredService<IEmployeeService>(),
                logger);
        }
    }
}

app.Run();
