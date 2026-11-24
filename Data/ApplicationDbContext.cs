using ConstructionProjectManagement.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskEntity = ConstructionProjectManagement.Models.Task;

namespace ConstructionProjectManagement.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    private static readonly DateTime CategorySeedCreatedAt =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectRole> ProjectRoles => Set<ProjectRole>();

    public DbSet<ProjectMemberRoleAssignment> ProjectMemberRoleAssignments => Set<ProjectMemberRoleAssignment>();

    public DbSet<ProjectManagerHistory> ProjectManagerHistories => Set<ProjectManagerHistory>();

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentAuditLog> DocumentAuditLogs => Set<DocumentAuditLog>();
    public DbSet<DocumentApprovalLog> DocumentApprovalLogs => Set<DocumentApprovalLog>();
    public DbSet<DocumentEditLock> DocumentEditLocks => Set<DocumentEditLock>();
    public DbSet<ContractParty> ContractParties => Set<ContractParty>();
    public DbSet<ContractProject> ContractProjects => Set<ContractProject>();
    public DbSet<ContractAmendment> ContractAmendments => Set<ContractAmendment>();
    public DbSet<ContractValueHistory> ContractValueHistories => Set<ContractValueHistory>();
    public DbSet<ContractAuditLog> ContractAuditLogs => Set<ContractAuditLog>();
    public DbSet<ContractEditLock> ContractEditLocks => Set<ContractEditLock>();

    public DbSet<ProjectPhase> ProjectPhases => Set<ProjectPhase>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();

    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    public DbSet<TaskProgressHistory> TaskProgressHistories => Set<TaskProgressHistory>();

    public DbSet<TaskAuditLog> TaskAuditLogs => Set<TaskAuditLog>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<ConstructionTeam> ConstructionTeams => Set<ConstructionTeam>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<MaterialRequest> MaterialRequests => Set<MaterialRequest>();

    public DbSet<MaterialRequestItem> MaterialRequestItems => Set<MaterialRequestItem>();

    public DbSet<MaterialTransaction> MaterialTransactions => Set<MaterialTransaction>();

    public DbSet<Equipment> Equipment => Set<Equipment>();

    public DbSet<EquipmentAssignment> EquipmentAssignments => Set<EquipmentAssignment>();

    public DbSet<ConstructionLog> ConstructionLogs => Set<ConstructionLog>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<RfiRequest> RfiRequests => Set<RfiRequest>();

    public DbSet<InspectionRequest> InspectionRequests => Set<InspectionRequest>();

    public DbSet<InspectionHistory> InspectionHistories => Set<InspectionHistory>();

    public DbSet<ProjectExpense> ProjectExpenses => Set<ProjectExpense>();

    public DbSet<ProjectHandover> ProjectHandovers => Set<ProjectHandover>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DocumentCategory>(entity =>
        {
            entity.HasIndex(item => item.Code).IsUnique();
            entity.HasData(
                new DocumentCategory { Id = 1, Code = "Legal", Name = "Pháp lý", Description = "Pháp lý & Hành chính", ApprovalLevels = 2, DisplayOrder = 1, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 2, Code = "Contract", Name = "Hợp đồng", Description = "Hợp đồng", ApprovalLevels = 2, DisplayOrder = 2, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 3, Code = "Amendment", Name = "Phụ lục hợp đồng", Description = "Hợp đồng", ApprovalLevels = 2, DisplayOrder = 3, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 4, Code = "OfficialLetter", Name = "Công văn", Description = "Hành chính", ApprovalLevels = 1, DisplayOrder = 4, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 5, Code = "Minutes", Name = "Biên bản họp", Description = "Hành chính", ApprovalLevels = 0, DisplayOrder = 5, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 6, Code = "Design", Name = "Bản vẽ & Thiết kế", Description = "Kỹ thuật", ApprovalLevels = 2, DisplayOrder = 6, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 7, Code = "Construction", Name = "Biện pháp & Thi công", Description = "Kỹ thuật & Hiện trường", ApprovalLevels = 2, DisplayOrder = 7, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 8, Code = "Materials", Name = "Vật tư & Thiết bị", Description = "Vật tư", ApprovalLevels = 1, DisplayOrder = 8, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 9, Code = "Quality", Name = "Quản lý chất lượng (QA/QC)", Description = "Chất lượng", ApprovalLevels = 2, DisplayOrder = 9, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 10, Code = "Acceptance", Name = "Nghiệm thu", Description = "Chất lượng & Nghiệm thu", ApprovalLevels = 2, DisplayOrder = 10, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 11, Code = "Safety", Name = "An toàn lao động (HSE)", Description = "An toàn", ApprovalLevels = 1, DisplayOrder = 11, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 12, Code = "Payment", Name = "Hồ sơ thanh toán", Description = "Tài chính", ApprovalLevels = 2, DisplayOrder = 12, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 13, Code = "Handover", Name = "Bàn giao & Hoàn công", Description = "Bàn giao", ApprovalLevels = 2, DisplayOrder = 13, CreatedAt = CategorySeedCreatedAt },
                new DocumentCategory { Id = 14, Code = "Other", Name = "Tài liệu khác / Hướng dẫn chung", Description = "Chung", ApprovalLevels = 0, DisplayOrder = 14, CreatedAt = CategorySeedCreatedAt });
        });

        builder.Entity<DocumentCategory>().Property(item => item.IsActive).HasDefaultValue(true);
        builder.Entity<DocumentCategory>().Property(item => item.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Entity<ProjectDocument>(entity =>
        {
            entity.HasIndex(item => item.DocumentCode).IsUnique();
            entity.HasOne(item => item.CategoryEntity).WithMany().HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Project).WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Amendment).WithMany().HasForeignKey(item => item.AmendmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DocumentVersion>(entity =>
        {
            entity.HasIndex(item => new { item.DocumentId, item.VersionNumber }).IsUnique();
            entity.HasOne(item => item.Document).WithMany(document => document.Versions).HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.UploadedBy).WithMany().HasForeignKey(item => item.UploadedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DocumentAuditLog>(entity =>
        {
            entity.HasOne(item => item.Document).WithMany(document => document.AuditLogs).HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.PerformedBy).WithMany().HasForeignKey(item => item.PerformedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DocumentApprovalLog>(entity =>
        {
            entity.HasOne(item => item.Document).WithMany(document => document.ApprovalLogs).HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.DocumentVersion).WithMany().HasForeignKey(item => item.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Actor).WithMany().HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DocumentEditLock>(entity =>
        {
            entity.HasKey(item => item.DocumentId);
            entity.HasOne(item => item.Document).WithOne(document => document.EditLock).HasForeignKey<DocumentEditLock>(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.LockedBy).WithMany().HasForeignKey(item => item.LockedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.LockedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ContractParty>().HasIndex(item => item.Code).IsUnique();
        builder.Entity<Contract>(entity =>
        {
            entity.HasIndex(item => item.ContractNumber).IsUnique();
            entity.Property(item => item.OriginalValue).HasPrecision(18, 2);
            entity.Property(item => item.CurrentValue).HasPrecision(18, 2);
            entity.Property(item => item.ContractValue).HasPrecision(18, 2);
            entity.HasOne(item => item.ClientParty).WithMany().HasForeignKey(item => item.ClientPartyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ContractorParty).WithMany().HasForeignKey(item => item.ContractorPartyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.CreatedBy).WithMany().HasForeignKey(item => item.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ContractProject>(entity =>
        {
            entity.HasIndex(item => new { item.ContractId, item.ProjectId }).IsUnique();
            entity.Property(item => item.AllocatedValue).HasPrecision(18, 2);
            entity.HasOne(item => item.Contract).WithMany(contract => contract.ContractProjects).HasForeignKey(item => item.ContractId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Project).WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ContractAmendment>(entity =>
        {
            entity.HasIndex(item => item.AmendmentNumber).IsUnique();
            entity.Property(item => item.ValueAdjustment).HasPrecision(18, 2);
            entity.HasOne(item => item.Contract).WithMany(contract => contract.Amendments).HasForeignKey(item => item.ContractId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.CreatedBy).WithMany().HasForeignKey(item => item.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ContractValueHistory>(entity =>
        {
            entity.Property(item => item.PreviousValue).HasPrecision(18, 2);
            entity.Property(item => item.AdjustmentAmount).HasPrecision(18, 2);
            entity.Property(item => item.NewValue).HasPrecision(18, 2);
            entity.HasOne(item => item.Contract).WithMany(contract => contract.ValueHistories).HasForeignKey(item => item.ContractId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Amendment).WithMany().HasForeignKey(item => item.AmendmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.RecordedBy).WithMany().HasForeignKey(item => item.RecordedById).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ContractAuditLog>(entity =>
        {
            entity.HasOne(item => item.Contract).WithMany(contract => contract.AuditLogs).HasForeignKey(item => item.ContractId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Amendment).WithMany().HasForeignKey(item => item.AmendmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.PerformedBy).WithMany().HasForeignKey(item => item.PerformedById).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ContractEditLock>(entity =>
        {
            entity.HasKey(item => item.ContractId);
            entity.HasOne(item => item.Contract).WithOne().HasForeignKey<ContractEditLock>(item => item.ContractId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.LockedBy).WithMany().HasForeignKey(item => item.LockedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Project>()
            .HasIndex(project => project.ProjectCode)
            .IsUnique();

        builder.Entity<Project>()
            .Property(project => project.Budget)
            .HasPrecision(18, 2);

        builder.Entity<Project>()
            .HasOne(project => project.ProjectManager)
            .WithMany()
            .HasForeignKey(project => project.ProjectManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProjectMember>()
            .HasIndex(member => new { member.ProjectId, member.UserId })
            .IsUnique();

        builder.Entity<ProjectMember>()
            .HasIndex(member => new { member.ProjectId, member.EmployeeId })
            .IsUnique()
            .HasFilter("\"EmployeeId\" IS NOT NULL");

        builder.Entity<ProjectMember>()
            .HasOne(member => member.Project)
            .WithMany(project => project.Members)
            .HasForeignKey(member => member.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProjectMember>()
            .HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProjectMember>()
            .HasOne(member => member.Employee)
            .WithMany()
            .HasForeignKey(member => member.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProjectRole>(entity =>
        {
            entity.HasKey(role => role.Code);
            entity.Property(role => role.Code).HasMaxLength(40);
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ProjectRoles_MaximumConcurrentProjects",
                "\"MaximumConcurrentProjects\" IS NULL OR \"MaximumConcurrentProjects\" > 0"));
            entity.HasData(
                new ProjectRole
                {
                    Code = ProjectRoleCode.ProjectManager,
                    Name = "Project Manager",
                    MaximumConcurrentProjects = null
                },
                new ProjectRole
                {
                    Code = ProjectRoleCode.DeputyProjectManager,
                    Name = "Deputy Project Manager",
                    MaximumConcurrentProjects = null
                },
                new ProjectRole
                {
                    Code = ProjectRoleCode.SiteManager,
                    Name = "Site Manager",
                    MaximumConcurrentProjects = 1
                },
                new ProjectRole
                {
                    Code = ProjectRoleCode.SupervisingEngineer,
                    Name = "Supervising Engineer",
                    MaximumConcurrentProjects = 1
                });
        });

        builder.Entity<ProjectMemberRoleAssignment>(entity =>
        {
            entity.HasIndex(assignment => new
                {
                    assignment.ProjectMemberId,
                    assignment.ProjectRoleCode
                })
                .IsUnique()
                .HasFilter("\"EndedAt\" IS NULL");

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ProjectMemberRoleAssignments_DateRange",
                "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\""));

            entity.HasOne(assignment => assignment.ProjectMember)
                .WithMany(member => member.RoleAssignments)
                .HasForeignKey(assignment => assignment.ProjectMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(assignment => assignment.ProjectRole)
                .WithMany(role => role.Assignments)
                .HasForeignKey(assignment => assignment.ProjectRoleCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProjectManagerHistory>(entity =>
        {
            entity.HasIndex(history => history.ProjectId)
                .IsUnique()
                .HasFilter("\"EndedAt\" IS NULL");

            entity.Property(history => history.ChangeReason).HasMaxLength(500);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ProjectManagerHistories_DateRange",
                "\"StartedAt\" IS NULL OR \"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\""));

            entity.HasOne(history => history.Project)
                .WithMany()
                .HasForeignKey(history => history.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(history => history.PreviousProjectManager)
                .WithMany()
                .HasForeignKey(history => history.PreviousProjectManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(history => history.NewProjectManager)
                .WithMany()
                .HasForeignKey(history => history.NewProjectManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProjectPhase>(entity =>
        {
            entity.HasOne(phase => phase.Project)
                .WithMany(project => project.Phases)
                .HasForeignKey(phase => phase.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(phase => new { phase.ProjectId, phase.SequenceOrder });
            entity.HasIndex(phase => new { phase.ProjectId, phase.IsArchived });
            entity.Property(phase => phase.Status).HasConversion<int>();
            entity.Property(phase => phase.StatusBeforeArchive).HasConversion<int?>();
        });

        builder.Entity<WorkItem>(entity =>
        {
            entity.HasOne(item => item.Project)
                .WithMany()
                .HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ProjectPhase)
                .WithMany(phase => phase.WorkItems)
                .HasForeignKey(item => item.ProjectPhaseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ParentWorkItem)
                .WithMany(item => item.Children)
                .HasForeignKey(item => item.ParentWorkItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(item => item.WbsCode).UseCollation("NOCASE");
            entity.Property(item => item.Status).HasConversion<int>();
            entity.Property(item => item.StatusBeforeArchive).HasConversion<int?>();
            entity.HasIndex(item => new { item.ProjectId, item.WbsCode }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.IsArchived });
            entity.ToTable(table => table.HasCheckConstraint("CK_WorkItems_Level", "\"Level\" >= 1"));
        });

        builder.Entity<TaskEntity>()
            .Property(task => task.PercentComplete)
            .HasPrecision(5, 2);

        builder.Entity<TaskEntity>().Property(task => task.ConcurrencyStamp).IsConcurrencyToken();
        builder.Entity<TaskEntity>().ToTable(table =>
        {
            table.HasCheckConstraint("CK_Tasks_CompletionPercentage", "CAST(\"PercentComplete\" AS NUMERIC) >= 0 AND CAST(\"PercentComplete\" AS NUMERIC) <= 100");
            table.HasCheckConstraint("CK_Tasks_PlannedDates", "\"PlannedEndDate\" >= \"PlannedStartDate\"");
        });

        builder.Entity<TaskEntity>()
            .HasOne(task => task.Project)
            .WithMany()
            .HasForeignKey(task => task.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TaskEntity>()
            .HasOne(task => task.WorkItem)
            .WithMany()
            .HasForeignKey(task => task.WorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<TaskEntity>()
            .HasOne(task => task.ParentTask)
            .WithMany(task => task.Children)
            .HasForeignKey(task => task.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TaskAssignment>()
            .HasIndex(assignment => new { assignment.TaskId, assignment.UserId })
            .IsUnique();

        builder.Entity<TaskAssignment>()
            .HasOne(assignment => assignment.Task)
            .WithMany(task => task.Assignments)
            .HasForeignKey(assignment => assignment.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TaskAssignment>()
            .HasOne(assignment => assignment.User)
            .WithMany()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TaskDependency>()
            .HasOne(dependency => dependency.Task)
            .WithMany(task => task.Dependencies)
            .HasForeignKey(dependency => dependency.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TaskDependency>()
            .HasOne(dependency => dependency.PredecessorTask)
            .WithMany()
            .HasForeignKey(dependency => dependency.PredecessorTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TaskDependency>().HasIndex(dependency => new { dependency.TaskId, dependency.PredecessorTaskId }).IsUnique();
        builder.Entity<TaskDependency>().Property(dependency => dependency.DependencyType).HasDefaultValue("FS");
        builder.Entity<TaskDependency>().ToTable(table => table.HasCheckConstraint("CK_TaskDependencies_NotSelf", "\"TaskId\" <> \"PredecessorTaskId\""));
        builder.Entity<TaskDependency>().HasOne(dependency => dependency.CreatedBy).WithMany()
            .HasForeignKey(dependency => dependency.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TaskAuditLog>().HasOne(log => log.Task).WithMany()
            .HasForeignKey(log => log.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<TaskAuditLog>().HasOne(log => log.PerformedBy).WithMany()
            .HasForeignKey(log => log.PerformedById).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TaskComment>().HasOne(comment => comment.Task).WithMany()
            .HasForeignKey(comment => comment.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<TaskComment>().HasOne(comment => comment.Author).WithMany()
            .HasForeignKey(comment => comment.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TaskComment>().Property(comment => comment.ProgressSnapshot).HasPrecision(5, 2);

        builder.Entity<TaskProgressHistory>()
            .Property(history => history.PreviousPercentComplete)
            .HasPrecision(5, 2);

        builder.Entity<TaskProgressHistory>()
            .Property(history => history.NewPercentComplete)
            .HasPrecision(5, 2);

        builder.Entity<TaskProgressHistory>()
            .HasOne(history => history.Task)
            .WithMany()
            .HasForeignKey(history => history.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TaskProgressHistory>()
            .HasOne(history => history.UpdatedBy)
            .WithMany()
            .HasForeignKey(history => history.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Employee>()
            .HasIndex(employee => employee.EmployeeCode)
            .IsUnique();

        builder.Entity<Employee>()
            .HasIndex(employee => employee.UserId)
            .IsUnique();

        builder.Entity<Employee>()
            .HasOne(employee => employee.User)
            .WithOne()
            .HasForeignKey<Employee>(employee => employee.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Employee>()
            .HasOne(employee => employee.Department)
            .WithMany(department => department.Employees)
            .HasForeignKey(employee => employee.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<TeamMember>()
            .HasIndex(member => new { member.TeamId, member.EmployeeId })
            .IsUnique();

        builder.Entity<TeamMember>()
            .HasOne(member => member.Team)
            .WithMany(team => team.Members)
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TeamMember>()
            .HasOne(member => member.Employee)
            .WithMany()
            .HasForeignKey(member => member.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Material>()
            .HasIndex(material => material.MaterialCode)
            .IsUnique();

        builder.Entity<Material>()
            .Property(material => material.CurrentStock)
            .HasPrecision(18, 3);

        builder.Entity<MaterialRequest>()
            .HasOne(request => request.Project)
            .WithMany()
            .HasForeignKey(request => request.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialRequest>()
            .HasOne(request => request.Task)
            .WithMany()
            .HasForeignKey(request => request.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<MaterialRequest>()
            .HasOne(request => request.RequestedBy)
            .WithMany()
            .HasForeignKey(request => request.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialRequest>()
            .HasOne(request => request.ApprovedBy)
            .WithMany()
            .HasForeignKey(request => request.ApprovedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialRequestItem>()
            .HasIndex(item => new { item.MaterialRequestId, item.MaterialId })
            .IsUnique();

        builder.Entity<MaterialRequestItem>()
            .Property(item => item.QuantityRequested)
            .HasPrecision(18, 3);

        builder.Entity<MaterialRequestItem>()
            .Property(item => item.QuantityIssued)
            .HasPrecision(18, 3);

        builder.Entity<MaterialRequestItem>()
            .HasOne(item => item.MaterialRequest)
            .WithMany(request => request.Items)
            .HasForeignKey(item => item.MaterialRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MaterialRequestItem>()
            .HasOne(item => item.Material)
            .WithMany(material => material.RequestItems)
            .HasForeignKey(item => item.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialTransaction>()
            .Property(transaction => transaction.Quantity)
            .HasPrecision(18, 3);

        builder.Entity<MaterialTransaction>()
            .HasOne(transaction => transaction.Material)
            .WithMany(material => material.Transactions)
            .HasForeignKey(transaction => transaction.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Equipment>()
            .HasIndex(equipment => equipment.EquipmentCode)
            .IsUnique();

        builder.Entity<EquipmentAssignment>()
            .HasOne(assignment => assignment.Equipment)
            .WithMany(equipment => equipment.Assignments)
            .HasForeignKey(assignment => assignment.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ConstructionLog>()
            .HasIndex(log => new { log.ProjectId, log.LogDate });

        builder.Entity<ConstructionLog>()
            .HasOne(log => log.Project)
            .WithMany()
            .HasForeignKey(log => log.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ConstructionLog>()
            .HasOne(log => log.CreatedBy)
            .WithMany()
            .HasForeignKey(log => log.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Incident>()
            .HasOne(item => item.Project).WithMany()
            .HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Incident>()
            .HasOne(item => item.Task).WithMany()
            .HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Incident>()
            .HasOne(item => item.AssignedTo).WithMany()
            .HasForeignKey(item => item.AssignedToId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Incident>()
            .HasOne(item => item.ReportedBy).WithMany()
            .HasForeignKey(item => item.ReportedById).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RfiRequest>()
            .HasOne(item => item.Project).WithMany()
            .HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RfiRequest>()
            .HasOne(item => item.Task).WithMany()
            .HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<RfiRequest>()
            .HasOne(item => item.CreatedBy).WithMany()
            .HasForeignKey(item => item.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RfiRequest>()
            .HasOne(item => item.RespondedBy).WithMany()
            .HasForeignKey(item => item.RespondedById).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InspectionRequest>()
            .HasOne(item => item.Project).WithMany()
            .HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InspectionRequest>()
            .HasOne(item => item.Task).WithMany()
            .HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InspectionRequest>()
            .HasOne(item => item.RequestedBy).WithMany()
            .HasForeignKey(item => item.RequestedById).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InspectionRequest>()
            .HasOne(item => item.InspectedBy).WithMany()
            .HasForeignKey(item => item.InspectedById).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InspectionHistory>()
            .HasOne(item => item.InspectionRequest).WithMany(request => request.History)
            .HasForeignKey(item => item.InspectionRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<InspectionHistory>()
            .HasOne(item => item.InspectedBy).WithMany()
            .HasForeignKey(item => item.InspectedById).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EquipmentAssignment>()
            .HasOne(assignment => assignment.Project)
            .WithMany()
            .HasForeignKey(assignment => assignment.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProjectExpense>()
            .Property(expense => expense.Amount).HasPrecision(18, 2);
        builder.Entity<ProjectExpense>()
            .HasOne(expense => expense.Project).WithMany()
            .HasForeignKey(expense => expense.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectExpense>()
            .HasOne(expense => expense.RecordedBy).WithMany()
            .HasForeignKey(expense => expense.RecordedById).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectHandover>()
            .HasOne(handover => handover.Project).WithMany()
            .HasForeignKey(handover => handover.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectHandover>()
            .HasOne(handover => handover.HandedOverBy).WithMany()
            .HasForeignKey(handover => handover.HandedOverById).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EquipmentAssignment>()
            .HasOne(assignment => assignment.Task)
            .WithMany()
            .HasForeignKey(assignment => assignment.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<EquipmentAssignment>()
            .HasOne(assignment => assignment.AssignedTo)
            .WithMany()
            .HasForeignKey(assignment => assignment.AssignedToId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EquipmentAssignment>()
            .HasOne(assignment => assignment.HandedOverBy)
            .WithMany()
            .HasForeignKey(assignment => assignment.HandedOverById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EquipmentAssignment>()
            .HasOne(assignment => assignment.ReturnedBy)
            .WithMany()
            .HasForeignKey(assignment => assignment.ReturnedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialTransaction>()
            .HasOne(transaction => transaction.MaterialRequest)
            .WithMany(request => request.Transactions)
            .HasForeignKey(transaction => transaction.MaterialRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<MaterialTransaction>()
            .HasOne(transaction => transaction.Project)
            .WithMany()
            .HasForeignKey(transaction => transaction.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialTransaction>()
            .HasOne(transaction => transaction.Task)
            .WithMany()
            .HasForeignKey(transaction => transaction.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<MaterialTransaction>()
            .HasOne(transaction => transaction.PerformedBy)
            .WithMany()
            .HasForeignKey(transaction => transaction.PerformedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Contract>()
            .Property(contract => contract.ContractValue)
            .HasPrecision(18, 2);

        builder.Entity<Contract>()
            .HasIndex(contract => contract.ContractNumber)
            .IsUnique();

        builder.Entity<Contract>()
            .HasOne(contract => contract.Project)
            .WithMany()
            .HasForeignKey(contract => contract.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProjectDocument>()
            .HasOne(document => document.Project)
            .WithMany()
            .HasForeignKey(document => document.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProjectDocument>()
            .HasOne(document => document.Contract)
            .WithMany(contract => contract.Documents)
            .HasForeignKey(document => document.ContractId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        builder.Entity<ProjectDocument>()
            .HasOne(document => document.UploadedBy)
            .WithMany()
            .HasForeignKey(document => document.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureDocumentHistoryIsImmutable();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureDocumentHistoryIsImmutable();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureDocumentHistoryIsImmutable()
    {
        var mutableAuditLogs = ChangeTracker.Entries<DocumentAuditLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        var mutableApprovalLogs = ChangeTracker.Entries<DocumentApprovalLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (mutableAuditLogs || mutableApprovalLogs)
        {
            throw new InvalidOperationException("Không được sửa hoặc xóa lịch sử tài liệu.");
        }
    }
}
