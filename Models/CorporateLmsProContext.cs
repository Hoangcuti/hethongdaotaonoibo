using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace KhoaHoc.Models;

public partial class CorporateLmsProContext : DbContext
{
    public CorporateLmsProContext()
    {
    }

    public CorporateLmsProContext(DbContextOptions<CorporateLmsProContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AttendanceLog> AttendanceLogs { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<BackupLog> BackupLogs { get; set; }

    public virtual DbSet<Badge> Badges { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Certificate> Certificates { get; set; }

    public virtual DbSet<Course> Courses { get; set; }

    public virtual DbSet<CourseCost> CourseCosts { get; set; }

    public virtual DbSet<CourseFeedback> CourseFeedbacks { get; set; }

    public virtual DbSet<CourseModule> CourseModules { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<DeptRequiredSkill> DeptRequiredSkills { get; set; }

    public virtual DbSet<DeptTrainingBudget> DeptTrainingBudgets { get; set; }

    public virtual DbSet<DocumentLibrary> DocumentLibraries { get; set; }

    public virtual DbSet<Enrollment> Enrollments { get; set; }

    public virtual DbSet<Exam> Exams { get; set; }

    public virtual DbSet<ExamQuestion> ExamQuestions { get; set; }

    public virtual DbSet<ExternalVendor> ExternalVendors { get; set; }

    public virtual DbSet<Faq> Faqs { get; set; }

    public virtual DbSet<InternalMessage> InternalMessages { get; set; }

    public virtual DbSet<ItMovementLog> ItMovementLogs { get; set; }

    public virtual DbSet<JobTitle> JobTitles { get; set; }

    public virtual DbSet<LearningPath> LearningPaths { get; set; }

    public virtual DbSet<Lesson> Lessons { get; set; }

    public virtual DbSet<LessonAttachment> LessonAttachments { get; set; }

    public virtual DbSet<LessonComment> LessonComments { get; set; }

    public virtual DbSet<NewsletterSubscription> NewsletterSubscriptions { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<OfflineTrainingEvent> OfflineTrainingEvents { get; set; }

    public virtual DbSet<PathCourse> PathCourses { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<QuestionBank> QuestionBanks { get; set; }

    public virtual DbSet<QuestionOption> QuestionOptions { get; set; }

    public virtual DbSet<QuizSessionState> QuizSessionStates { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Skill> Skills { get; set; }

    public virtual DbSet<Survey> Surveys { get; set; }

    public virtual DbSet<SurveyResult> SurveyResults { get; set; }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    public virtual DbSet<Trainer> Trainers { get; set; }

    public virtual DbSet<TrainingAssignment> TrainingAssignments { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserAnswer> UserAnswers { get; set; }

    public virtual DbSet<UserBadge> UserBadges { get; set; }

    public virtual DbSet<UserExam> UserExams { get; set; }

    public virtual DbSet<UserLessonLog> UserLessonLogs { get; set; }

    public virtual DbSet<UserPermission> UserPermissions { get; set; }

    public virtual DbSet<UserPathProgress> UserPathProgresses { get; set; }

    public virtual DbSet<UserPoint> UserPoints { get; set; }

    public virtual DbSet<UserSkill> UserSkills { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceLog>(entity =>
        {
            entity.HasKey(e => new { e.EventId, e.UserId }).HasName("PK__Attendan__A83C44BA693DC41F");

            entity.Property(e => e.Status).HasDefaultValue(false);

            entity.HasOne(d => d.Event).WithMany(p => p.AttendanceLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Attendanc__Event__2FCF1A8A");

            entity.HasOne(d => d.User).WithMany(p => p.AttendanceLogs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Attendanc__UserI__30C33EC3");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__AuditLog__5E5499A8DB1F4FBD");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs).HasConstraintName("FK__AuditLogs__UserI__31B762FC");
        });

        modelBuilder.Entity<BackupLog>(entity =>
        {
            entity.HasKey(e => e.BackupId).HasName("PK__BackupLo__EB9069E290401933");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Badge>(entity =>
        {
            entity.HasKey(e => e.BadgeId).HasName("PK__Badges__1918237C94FC8A24");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__Categori__19093A2B6D7FD2FF");

            entity.HasOne(d => d.OwnerDept).WithMany(p => p.Categories).HasConstraintName("FK__Categorie__Owner__32AB8735");
        });

        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.CertId).HasName("PK__Certific__E5BD38E57C7F233C");

            entity.HasOne(d => d.Course).WithMany(p => p.Certificates).HasConstraintName("FK__Certifica__Cours__339FAB6E");

            entity.HasOne(d => d.User).WithMany(p => p.Certificates).HasConstraintName("FK__Certifica__UserI__3493CFA7");
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(e => e.CourseId).HasName("PK__Courses__C92D718700FF0E13");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsMandatory).HasDefaultValue(false);
            entity.Property(e => e.Status).HasDefaultValue("Draft");

            entity.HasOne(d => d.Category).WithMany(p => p.Courses).HasConstraintName("FK__Courses__Categor__395884C4");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Courses).HasConstraintName("FK__Courses__Created__3A4CA8FD");

            entity.HasOne(d => d.TargetDepartment).WithMany(p => p.TargetedCourses).HasConstraintName("FK__Courses__TargetD__7E123456");

            entity.HasOne(d => d.OwnerDepartment).WithMany(p => p.OwnedCourses).HasConstraintName("FK__Courses__OwnerDe__7E123457");
        });

        modelBuilder.Entity<CourseCost>(entity =>
        {
            entity.HasKey(e => e.CourseId).HasName("PK__CourseCo__C92D7187EF37DAF4");

            entity.Property(e => e.CourseId).ValueGeneratedNever();

            entity.HasOne(d => d.Course).WithOne(p => p.CourseCost)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CourseCos__Cours__3587F3E0");
        });

        modelBuilder.Entity<CourseFeedback>(entity =>
        {
            entity.HasKey(e => e.FeedbackId).HasName("PK__CourseFe__6A4BEDF691BA344B");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Course).WithMany(p => p.CourseFeedbacks).HasConstraintName("FK__CourseFee__Cours__367C1819");

            entity.HasOne(d => d.User).WithMany(p => p.CourseFeedbacks).HasConstraintName("FK__CourseFee__UserI__37703C52");
        });

        modelBuilder.Entity<CourseModule>(entity =>
        {
            entity.HasKey(e => e.ModuleId).HasName("PK__CourseMo__2B74778764AB1F61");

            entity.HasOne(d => d.Course).WithMany(p => p.CourseModules).HasConstraintName("FK__CourseMod__Cours__3864608B");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("PK__Departme__B2079BCDB04D3603");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.SidebarStyle).HasDefaultValue("default");
            entity.Property(e => e.ThemeColor).HasDefaultValue("#2c3e50");
            entity.Property(e => e.DepartmentEmail).HasMaxLength(255);
        });

        modelBuilder.Entity<DeptRequiredSkill>(entity =>
        {
            entity.HasKey(e => new { e.DeptId, e.SkillId }).HasName("PK__DeptRequ__6CB2889025D098FF");

            entity.HasOne(d => d.Dept).WithMany(p => p.DeptRequiredSkills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__DeptRequi__DeptI__3B40CD36");

            entity.HasOne(d => d.Skill).WithMany(p => p.DeptRequiredSkills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__DeptRequi__Skill__3C34F16F");
        });

        modelBuilder.Entity<DeptTrainingBudget>(entity =>
        {
            entity.HasKey(e => e.BudgetId).HasName("PK__DeptTrai__E38E79C4530256E9");

            entity.Property(e => e.SpentAmount).HasDefaultValue(0m);

            entity.HasOne(d => d.Dept).WithMany(p => p.DeptTrainingBudgets).HasConstraintName("FK__DeptTrain__DeptI__3D2915A8");
        });

        modelBuilder.Entity<DocumentLibrary>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Document__3214EC27DDB07995");
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(e => e.EnrollmentId).HasName("PK__Enrollme__7F6877FB0CF52B43");

            entity.Property(e => e.EnrollDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ProgressPercent).HasDefaultValue(0);

            entity.HasOne(d => d.Course).WithMany(p => p.Enrollments).HasConstraintName("FK__Enrollmen__Cours__3E1D39E1");

            entity.HasOne(d => d.User).WithMany(p => p.Enrollments).HasConstraintName("FK__Enrollmen__UserI__3F115E1A");
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.HasKey(e => e.ExamId).HasName("PK__Exams__297521A70F2A8A3E");

            entity.HasOne(d => d.Course).WithMany(p => p.Exams).HasConstraintName("FK__Exams__CourseID__41EDCAC5");

            entity.HasOne(d => d.Module)
                .WithMany()
                .HasForeignKey(d => d.ModuleId)
                .HasConstraintName("FK_Exams_CourseModules");
        });

        modelBuilder.Entity<ExamQuestion>(entity =>
        {
            entity.HasKey(e => new { e.ExamId, e.QuestionId }).HasName("PK__ExamQues__F9A9275F088E0EED");

            entity.HasOne(d => d.Exam).WithMany(p => p.ExamQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ExamQuest__ExamI__40058253");

            entity.HasOne(d => d.Question).WithMany(p => p.ExamQuestions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ExamQuest__Quest__40F9A68C");
        });

        modelBuilder.Entity<ExternalVendor>(entity =>
        {
            entity.HasKey(e => e.VendorId).HasName("PK__External__FC8618D34ED4669B");
        });

        modelBuilder.Entity<Faq>(entity =>
        {
            entity.HasKey(e => e.Faqid).HasName("PK__FAQ__4B89D1E226BB5932");

            entity.HasOne(d => d.Category).WithMany(p => p.Faqs).HasConstraintName("FK__FAQ__CategoryID__42E1EEFE");
        });

        modelBuilder.Entity<InternalMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Internal__3214EC2754E5FFFB");

            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<ItMovementLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__IT_Movem__5E5499A879B3714D");

            entity.Property(e => e.MoveDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.ActionByNavigation).WithMany(p => p.ItMovementLogActionByNavigations).HasConstraintName("FK__IT_Moveme__Actio__43D61337");

            entity.HasOne(d => d.Employee).WithMany(p => p.ItMovementLogEmployees).HasConstraintName("FK__IT_Moveme__Emplo__44CA3770");
        });

        modelBuilder.Entity<JobTitle>(entity =>
        {
            entity.HasKey(e => e.JobTitleId).HasName("PK__JobTitle__35382FC95F2DC8FF");
        });

        modelBuilder.Entity<LearningPath>(entity =>
        {
            entity.HasKey(e => e.PathId).HasName("PK__Learning__CD67DC39EE3BA32F");

            entity.HasOne(d => d.CreatedByDept).WithMany(p => p.LearningPaths).HasConstraintName("FK__LearningP__Creat__45BE5BA9");
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(e => e.LessonId).HasName("PK__Lessons__B084ACB0D8D1EE96");

            entity.HasOne(d => d.Module).WithMany(p => p.Lessons).HasConstraintName("FK__Lessons__ModuleI__47A6A41B");
        });

        modelBuilder.Entity<LessonAttachment>(entity =>
        {
            entity.HasKey(e => e.AttachmentId).HasName("PK__LessonAt__442C64DE1C8111DB");

            entity.HasOne(d => d.Lesson).WithMany(p => p.LessonAttachments).HasConstraintName("FK__LessonAtt__Lesso__46B27FE2");
        });

        modelBuilder.Entity<LessonComment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LessonCo__3214EC274FC651B2");

            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<NewsletterSubscription>(entity =>
        {
            entity.HasKey(e => e.SubId).HasName("PK__Newslett__4D9BB86ABB3332CD");

            entity.Property(e => e.IsSubscribed).HasDefaultValue(true);

            entity.HasOne(d => d.User).WithMany(p => p.NewsletterSubscriptions).HasConstraintName("FK__Newslette__UserI__489AC854");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Notifica__3214EC2754B9E247");

            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<OfflineTrainingEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__OfflineT__7944C87079E775B9");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Course).WithMany(p => p.OfflineTrainingEvents).HasConstraintName("FK__OfflineTr__Cours__498EEC8D");
        });

        modelBuilder.Entity<PathCourse>(entity =>
        {
            entity.HasKey(e => new { e.PathId, e.CourseId }).HasName("PK__PathCour__A1F50B216B537C7C");

            entity.HasOne(d => d.Course).WithMany(p => p.PathCourses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PathCours__Cours__4A8310C6");

            entity.HasOne(d => d.Path).WithMany(p => p.PathCourses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PathCours__PathI__4B7734FF");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.PermissionId).HasName("PK__Permissi__EFA6FB0F3D8084DF");
        });

        modelBuilder.Entity<QuestionBank>(entity =>
        {
            entity.HasKey(e => e.QuestionId).HasName("PK__Question__0DC06F8CD26EC118");

            entity.HasOne(d => d.Category).WithMany(p => p.QuestionBanks).HasConstraintName("FK__QuestionB__Categ__4C6B5938");
        });

        modelBuilder.Entity<QuestionOption>(entity =>
        {
            entity.HasKey(e => e.OptionId).HasName("PK__Question__92C7A1DFE7D4F3B6");

            entity.HasOne(d => d.Question).WithMany(p => p.QuestionOptions).HasConstraintName("FK__QuestionO__Quest__4D5F7D71");
        });

        modelBuilder.Entity<QuizSessionState>(entity =>
        {
            entity.HasKey(e => e.UserExamId).HasName("PK_QuizSessionStates");

            entity.Property(e => e.AnsweredCount).HasDefaultValue(0);
            entity.Property(e => e.CurrentQuestionIndex).HasDefaultValue(0);
            entity.Property(e => e.LastActivityAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.LastSavedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.RemainingSeconds).HasDefaultValue(0);

            entity.HasOne(d => d.UserExam).WithOne()
                .HasForeignKey<QuizSessionState>(d => d.UserExamId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_QuizSessionStates_UserExams");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE3AE1C798E7");

            entity.HasMany(d => d.Permissions).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RolePermission",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK__RolePermi__Permi__4E53A1AA"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK__RolePermi__RoleI__4F47C5E3"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermissionId").HasName("PK__RolePerm__6400A18AAA698440");
                        j.ToTable("RolePermissions");
                        j.IndexerProperty<int>("RoleId").HasColumnName("RoleID");
                        j.IndexerProperty<int>("PermissionId").HasColumnName("PermissionID");
                    });
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => e.SkillId).HasName("PK__Skills__DFA091E7A404092E");
        });

        modelBuilder.Entity<Survey>(entity =>
        {
            entity.HasKey(e => e.SurveyId).HasName("PK__Surveys__A5481F9DBAB5230E");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Surveys).HasConstraintName("FK__Surveys__Created__5224328E");
        });

        modelBuilder.Entity<SurveyResult>(entity =>
        {
            entity.HasKey(e => e.ResultId).HasName("PK__SurveyRe__97690228000D2DD2");

            entity.Property(e => e.SubmittedAt).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Survey).WithMany(p => p.SurveyResults).HasConstraintName("FK__SurveyRes__Surve__503BEA1C");

            entity.HasOne(d => d.User).WithMany(p => p.SurveyResults).HasConstraintName("FK__SurveyRes__UserI__51300E55");
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.SettingKey).HasName("PK__SystemSe__01E719ACA5424E51");

            entity.Property(e => e.ModifiedAt).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Trainer>(entity =>
        {
            entity.HasKey(e => e.TrainerId).HasName("PK__Trainers__366A1B9C3ED35BB3");

            entity.HasOne(d => d.User).WithMany(p => p.Trainers).HasConstraintName("FK__Trainers__UserID__531856C7");
        });

        modelBuilder.Entity<TrainingAssignment>(entity =>
        {
            entity.HasKey(e => e.AssignmentId).HasName("PK__Training__32499E57084BE757");

            entity.Property(e => e.AssignedDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.AssignedByNavigation).WithMany(p => p.TrainingAssignmentAssignedByNavigations).HasConstraintName("FK__TrainingA__Assig__540C7B00");

            entity.HasOne(d => d.Course).WithMany(p => p.TrainingAssignments).HasConstraintName("FK__TrainingA__Cours__55009F39");

            entity.HasOne(d => d.User).WithMany(p => p.TrainingAssignmentUsers).HasConstraintName("FK__TrainingA__UserI__55F4C372");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CCACAAC67F0D");

            entity.Property(e => e.IsDeptAdmin).HasDefaultValue(false);
            entity.Property(e => e.IsItadmin).HasDefaultValue(false);
            entity.Property(e => e.Status).HasDefaultValue("Active");

            entity.HasOne(d => d.Department).WithMany(p => p.Users).HasConstraintName("FK__Users__Departmen__625A9A57");

            entity.HasOne(d => d.JobTitle).WithMany(p => p.Users).HasConstraintName("FK__Users__JobTitleI__634EBE90");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserRole",
                    r => r.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK__UserRoles__RoleI__607251E5"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK__UserRoles__UserI__6166761E"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId").HasName("PK__UserRole__AF27604F28626526");
                        j.ToTable("UserRoles");
                        j.IndexerProperty<int>("UserId").HasColumnName("UserID");
                        j.IndexerProperty<int>("RoleId").HasColumnName("RoleID");
                    });
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.PermissionId }).HasName("PK_UserPermissions");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Permission).WithMany(p => p.UserPermissions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserPermissions_Permission");

            entity.HasOne(d => d.User).WithMany(p => p.UserPermissions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserPermissions_User");
        });

        modelBuilder.Entity<UserAnswer>(entity =>
        {
            entity.HasKey(e => new { e.UserExamId, e.QuestionId }).HasName("PK_UserAnswers_App");

            entity.HasOne(d => d.UserExam).WithMany().HasConstraintName("FK__UserAnswe__UserE__56E8E7AB");
        });

        modelBuilder.Entity<UserBadge>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.BadgeId }).HasName("PK__UserBadg__C6194E9B744E2666");

            entity.Property(e => e.EarnedDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Badge).WithMany(p => p.UserBadges)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserBadge__Badge__57DD0BE4");

            entity.HasOne(d => d.User).WithMany(p => p.UserBadges)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserBadge__UserI__58D1301D");
        });

        modelBuilder.Entity<UserExam>(entity =>
        {
            entity.HasKey(e => e.UserExamId).HasName("PK__UserExam__37688871AAD98D7B");

            entity.Property(e => e.IsFinish).HasDefaultValue(false);

            entity.HasOne(d => d.Exam).WithMany(p => p.UserExams).HasConstraintName("FK__UserExams__ExamI__59C55456");

            entity.HasOne(d => d.User).WithMany(p => p.UserExams).HasConstraintName("FK__UserExams__UserI__5AB9788F");
        });

        modelBuilder.Entity<UserLessonLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__UserLess__5E5499A8EB5168DF");

            entity.HasOne(d => d.Lesson).WithMany(p => p.UserLessonLogs).HasConstraintName("FK__UserLesso__Lesso__5BAD9CC8");

            entity.HasOne(d => d.User).WithMany(p => p.UserLessonLogs).HasConstraintName("FK__UserLesso__UserI__5CA1C101");
        });

        modelBuilder.Entity<UserPathProgress>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.PathId }).HasName("PK__UserPath__9B5EB16F3D01620E");

            entity.HasOne(d => d.Path).WithMany(p => p.UserPathProgresses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserPathP__PathI__5D95E53A");

            entity.HasOne(d => d.User).WithMany(p => p.UserPathProgresses)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserPathP__UserI__5E8A0973");
        });

        modelBuilder.Entity<UserPoint>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__UserPoin__1788CCACD40B703F");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.LastUpdated).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.TotalPoints).HasDefaultValue(0);

            entity.HasOne(d => d.User).WithOne(p => p.UserPoint)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserPoint__UserI__5F7E2DAC");
        });

        modelBuilder.Entity<UserSkill>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.SkillId }).HasName("PK__UserSkil__7A72C5B203286E04");

            entity.HasOne(d => d.Skill).WithMany(p => p.UserSkills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserSkill__Skill__6442E2C9");

            entity.HasOne(d => d.User).WithMany(p => p.UserSkills)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__UserSkill__UserI__65370702");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
