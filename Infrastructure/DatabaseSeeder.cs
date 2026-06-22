using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using KhoaHoc.Models;

namespace KhoaHoc.Infrastructure;

public static class DatabaseSeeder
{
    private class SeedQuestion
    {
        public string Text { get; set; } = "";
        public string Difficulty { get; set; } = "Easy";
        public List<string> Options { get; set; } = new();
        public int CorrectOptionIndex { get; set; } = 0;
    }

    public static async Task SeedAsync(CorporateLmsProContext context, bool forceReset = false)
    {
        if (forceReset)
        {
            await ClearDatabaseAsync(context);
        }

        // Nếu dữ liệu đã có phòng ban hoặc người dùng, bỏ qua không gieo tự động
        if (await context.Departments.AnyAsync() || await context.Users.AnyAsync())
        {
            // One-time patch: update lanhhgv0001 FullName to "Phòng Đào tạo"
            var targetUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "lanhhgv0001");
            if (targetUser != null && targetUser.FullName != "Phòng Đào tạo")
            {
                targetUser.FullName = "Phòng Đào tạo";
                await context.SaveChangesAsync();
            }
            return;
        }

        // 1. Tạo các JobTitle (Chức vụ)
        var jobTitles = new List<JobTitle>
        {
            new() { TitleName = "Giám đốc Điều hành", GradeLevel = 10 },
            new() { TitleName = "Phó Giám đốc", GradeLevel = 9 },
            new() { TitleName = "Trưởng phòng Nhân sự & Đào tạo", GradeLevel = 8 },
            new() { TitleName = "Trưởng phòng Kỹ thuật & Sản xuất", GradeLevel = 8 },
            new() { TitleName = "Trưởng phòng Công nghệ thông tin", GradeLevel = 8 },
            new() { TitleName = "Trưởng phòng Kinh doanh", GradeLevel = 8 },
            new() { TitleName = "Kế toán Trưởng", GradeLevel = 8 },
            new() { TitleName = "Giảng viên Nội bộ Cao cấp", GradeLevel = 6 },
            new() { TitleName = "Chuyên viên Đào tạo", GradeLevel = 4 },
            new() { TitleName = "Chuyên viên Kinh doanh", GradeLevel = 4 },
            new() { TitleName = "Chuyên viên Kế toán", GradeLevel = 4 },
            new() { TitleName = "Kỹ sư Hệ thống", GradeLevel = 4 },
            new() { TitleName = "IT Support", GradeLevel = 3 },
            new() { TitleName = "Tổ trưởng Sản xuất", GradeLevel = 4 },
            new() { TitleName = "Công nhân Vận hành", GradeLevel = 2 }
        };
        context.JobTitles.AddRange(jobTitles);
        await context.SaveChangesAsync();

        // 2. Tạo các Department (Phòng ban)
        var depts = new List<Department>
        {
            new() { DepartmentName = "Ban Giám đốc", Description = "Ban điều hành cấp cao của doanh nghiệp", ThemeColor = "#1e3a8a", SidebarStyle = "default" },
            new() { DepartmentName = "Phòng Nhân sự & Đào tạo", Description = "Tuyển dụng, đào tạo và phát triển nhân lực", ThemeColor = "#0d9488", SidebarStyle = "default" },
            new() { DepartmentName = "Phòng Kỹ thuật & Sản xuất", Description = "Vận hành thiết bị và dây chuyền sản xuất tại nhà máy", ThemeColor = "#ea580c", SidebarStyle = "default" },
            new() { DepartmentName = "Phòng Kinh doanh & Marketing", Description = "Kinh doanh, tìm kiếm khách hàng và quảng bá sản phẩm", ThemeColor = "#2563eb", SidebarStyle = "default" },
            new() { DepartmentName = "Phòng Tài chính - Kế toán", Description = "Quản lý dòng tiền, kế toán doanh nghiệp", ThemeColor = "#4f46e5", SidebarStyle = "default" },
            new() { DepartmentName = "Phòng Công nghệ thông tin", Description = "Vận hành hệ thống IT và bảo mật thông tin", ThemeColor = "#0f172a", SidebarStyle = "default" },
            new() { DepartmentName = "Trung tâm Đào tạo Nội bộ", Description = "Bộ phận giảng dạy nội bộ và phát triển kỹ năng doanh nghiệp", ThemeColor = "#7c3aed", SidebarStyle = "default" }
        };
        context.Departments.AddRange(depts);
        await context.SaveChangesAsync();

        // 3. Tạo các Roles
        var roles = new List<Role>
        {
            new() { RoleName = "IT" },
            new() { RoleName = "Manager" },
            new() { RoleName = "Student" },
            new() { RoleName = "Trainer" },
            new() { RoleName = "Lecturer" }
        };
        context.Roles.AddRange(roles);
        await context.SaveChangesAsync();

        // 4. Tạo Users (Mật khẩu mặc định 123456)
        byte[] defaultPasswordHash = SHA256.HashData(Encoding.UTF8.GetBytes("123456"));

        var rawUsers = new List<(string FullName, string EmployeeCode, string Title, string DeptName, bool IsDeptAdmin, string[] RoleNames, bool IsItAdmin, string? CustomUsername)>
        {
            // Ban Giám đốc
            ("Lê Minh Hùng", "GD0001", "Giám đốc Điều hành", "Ban Giám đốc", true, new[] { "Student", "Manager" }, false, null),
            ("Trần Đức Long", "GD0002", "Phó Giám đốc", "Ban Giám đốc", false, new[] { "Student" }, false, null),

            // Phòng Nhân sự & Đào tạo
            ("Trần Thị Tuyết", "HR0001", "Trưởng phòng Nhân sự & Đào tạo", "Phòng Nhân sự & Đào tạo", true, new[] { "Manager" }, false, null),
            ("Phan Tuyết Mai", "HR0002", "Chuyên viên Đào tạo", "Phòng Nhân sự & Đào tạo", false, new[] { "Student" }, false, null),
            ("Nguyễn Hoàng Nam", "HR0003", "Chuyên viên Kinh doanh", "Phòng Nhân sự & Đào tạo", false, new[] { "Student" }, false, null), // Tạm dùng Title hợp lệ
            ("Phạm Thị Dung", "HR0004", "Chuyên viên Đào tạo", "Phòng Nhân sự & Đào tạo", false, new[] { "Student" }, false, null),

            // Phòng Kỹ thuật & Sản xuất
            ("Phạm Văn Máy", "KT0001", "Trưởng phòng Kỹ thuật & Sản xuất", "Phòng Kỹ thuật & Sản xuất", true, new[] { "Manager", "Trainer" }, false, null),
            ("Nguyễn Văn Cường", "CN0001", "Công nhân Vận hành", "Phòng Kỹ thuật & Sản xuất", false, new[] { "Student" }, false, null),
            ("Lê Văn Dũng", "CN0002", "Tổ trưởng Sản xuất", "Phòng Kỹ thuật & Sản xuất", false, new[] { "Student" }, false, null),
            ("Hoàng Văn Thái", "CN0003", "Công nhân Vận hành", "Phòng Kỹ thuật & Sản xuất", false, new[] { "Student" }, false, null),
            ("Trần Thị Hoa", "CN0004", "Công nhân Vận hành", "Phòng Kỹ thuật & Sản xuất", false, new[] { "Student" }, false, null),
            ("Bùi Văn Hùng", "CN0005", "Công nhân Vận hành", "Phòng Kỹ thuật & Sản xuất", false, new[] { "Student" }, false, null),
            ("Đinh Văn Tài", "CN0006", "Công nhân Vận hành", "Phòng Kỹ thuật & Sản xuất", false, new[] { "Student" }, false, null),

            // Phòng Kinh doanh & Marketing
            ("Đỗ Thùy Linh", "KD0001", "Trưởng phòng Kinh doanh", "Phòng Kinh doanh & Marketing", true, new[] { "Manager" }, false, null),
            ("Nguyễn Minh Anh", "KD0002", "Chuyên viên Kinh doanh", "Phòng Kinh doanh & Marketing", false, new[] { "Student" }, false, null),
            ("Phạm Quốc Bảo", "KD0003", "Chuyên viên Kinh doanh", "Phòng Kinh doanh & Marketing", false, new[] { "Student" }, false, null),
            ("Vũ Hoàng Yến", "KD0004", "Chuyên viên Kinh doanh", "Phòng Kinh doanh & Marketing", false, new[] { "Student" }, false, null),

            // Phòng Tài chính - Kế toán
            ("Vũ Thị Hương", "TC0001", "Kế toán Trưởng", "Phòng Tài chính - Kế toán", true, new[] { "Manager" }, false, null),
            ("Bùi Minh Tuấn", "TC0002", "Chuyên viên Kế toán", "Phòng Tài chính - Kế toán", false, new[] { "Student" }, false, null),
            ("Lê Thị Thảo", "TC0003", "Chuyên viên Kế toán", "Phòng Tài chính - Kế toán", false, new[] { "Student" }, false, null),

            // Phòng Công nghệ thông tin
            ("Nguyễn Văn Trị", "IT0001", "Kỹ sư Hệ thống", "Phòng Công nghệ thông tin", true, new[] { "IT", "Manager" }, true, "admin"),
            ("Lê Huy Hoàng", "IT0002", "IT Support", "Phòng Công nghệ thông tin", false, new[] { "Student" }, false, null),
            ("Trần Văn Khánh", "IT0003", "IT Support", "Phòng Công nghệ thông tin", false, new[] { "Student" }, false, null),

            // Trung tâm Đào tạo Nội bộ
            ("Hoàng Hương Lan", "GV0001", "Giảng viên Nội bộ Cao cấp", "Trung tâm Đào tạo Nội bộ", true, new[] { "Manager", "Trainer" }, false, null),
            ("Trần Hải Nam", "GV0002", "Giảng viên Nội bộ Cao cấp", "Trung tâm Đào tạo Nội bộ", false, new[] { "Manager", "Trainer" }, false, null)
        };

        var userList = new List<User>();
        foreach (var ru in rawUsers)
        {
            string username, email;
            if (ru.CustomUsername != null)
            {
                username = ru.CustomUsername;
                email = ru.CustomUsername + "@basau.net";
            }
            else
            {
                GenerateEmailAndUsername(ru.FullName, ru.EmployeeCode, out username, out email);
            }

            var dept = depts.First(d => d.DepartmentName == ru.DeptName);
            var title = jobTitles.First(j => j.TitleName == ru.Title);

            var user = new User
            {
                Username = username,
                FullName = ru.FullName,
                Email = email,
                EmployeeCode = ru.EmployeeCode,
                PasswordHash = defaultPasswordHash,
                IsItadmin = ru.IsItAdmin,
                IsDeptAdmin = ru.IsDeptAdmin,
                Status = "Active",
                DepartmentId = dept.DepartmentId,
                JobTitleId = title.JobTitleId
            };
            userList.Add(user);
        }
        context.Users.AddRange(userList);
        await context.SaveChangesAsync();

        // 5. Gán Roles cho các Users
        var roleIt = roles.First(r => r.RoleName == "IT");
        var roleManager = roles.First(r => r.RoleName == "Manager");
        var roleStudent = roles.First(r => r.RoleName == "Student");
        var roleTrainer = roles.First(r => r.RoleName == "Trainer");

        foreach (var ru in rawUsers)
        {
            string username;
            if (ru.CustomUsername != null)
                username = ru.CustomUsername;
            else
                GenerateEmailAndUsername(ru.FullName, ru.EmployeeCode, out username, out _);

            var user = userList.First(u => u.Username == username);
            foreach (var rName in ru.RoleNames)
            {
                var role = roles.First(r => r.RoleName == rName);
                user.Roles.Add(role);
            }
        }
        await context.SaveChangesAsync();

        // 6. Thiết lập mối quan hệ Trưởng phòng vào bảng Departments
        depts.First(d => d.DepartmentName == "Ban Giám đốc").ManagerId = userList.First(u => u.EmployeeCode == "GD0001").UserId;
        depts.First(d => d.DepartmentName == "Phòng Nhân sự & Đào tạo").ManagerId = userList.First(u => u.EmployeeCode == "HR0001").UserId;
        depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").ManagerId = userList.First(u => u.EmployeeCode == "KT0001").UserId;
        depts.First(d => d.DepartmentName == "Trung tâm Đào tạo Nội bộ").ManagerId = userList.First(u => u.EmployeeCode == "GV0001").UserId;
        depts.First(d => d.DepartmentName == "Phòng Công nghệ thông tin").ManagerId = userList.First(u => u.Username == "admin").UserId;
        depts.First(d => d.DepartmentName == "Phòng Kinh doanh & Marketing").ManagerId = userList.First(u => u.EmployeeCode == "KD0001").UserId;
        depts.First(d => d.DepartmentName == "Phòng Tài chính - Kế toán").ManagerId = userList.First(u => u.EmployeeCode == "TC0001").UserId;
        await context.SaveChangesAsync();

        // 7. Tạo giảng viên chuyên trách vào bảng Trainers
        var trainerRecords = new List<Trainer>
        {
            new() { UserId = userList.First(u => u.EmployeeCode == "KT0001").UserId, Expertise = "An toàn lao động, Quy trình vận hành & Bảo trì 5S", Rating = 4.8m },
            new() { UserId = userList.First(u => u.EmployeeCode == "GV0001").UserId, Expertise = "Kỹ năng mềm, Thuyết trình, Giao tiếp công sở", Rating = 4.9m },
            new() { UserId = userList.First(u => u.EmployeeCode == "GV0002").UserId, Expertise = "An ninh mạng, Quản trị hệ thống IT, Lập trình", Rating = 4.7m },
            new() { UserId = userList.First(u => u.EmployeeCode == "HR0001").UserId, Expertise = "Tổng quan hội nhập doanh nghiệp, Luật lao động", Rating = 4.6m }
        };
        context.Trainers.AddRange(trainerRecords);
        await context.SaveChangesAsync();

        // 8. Tạo Ngân sách đào tạo cho các phòng ban (Năm hiện tại)
        int currentYear = DateTime.Now.Year;
        var budgets = depts.Select(d => new DeptTrainingBudget
        {
            DeptId = d.DepartmentId,
            Year = currentYear,
            TotalBudget = d.DepartmentName == "Phòng Kỹ thuật & Sản xuất" ? 150000000m : 80000000m,
            SpentAmount = 0m
        }).ToList();
        context.DeptTrainingBudgets.AddRange(budgets);
        await context.SaveChangesAsync();

        // 9. Tạo các Category (Danh mục đào tạo)
        var categories = new List<Category>
        {
            new() { CategoryName = "Kỹ năng Lãnh đạo & Quản lý", OwnerDeptId = depts.First(d => d.DepartmentName == "Phòng Nhân sự & Đào tạo").DepartmentId },
            new() { CategoryName = "An toàn Lao động & Vận hành", OwnerDeptId = depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").DepartmentId },
            new() { CategoryName = "Kỹ năng Mềm & Phát triển Bản thân", OwnerDeptId = depts.First(d => d.DepartmentName == "Trung tâm Đào tạo Nội bộ").DepartmentId },
            new() { CategoryName = "Nghiệp vụ Chuyên môn", OwnerDeptId = depts.First(d => d.DepartmentName == "Phòng Nhân sự & Đào tạo").DepartmentId },
            new() { CategoryName = "Công nghệ & Bảo mật", OwnerDeptId = depts.First(d => d.DepartmentName == "Phòng Công nghệ thông tin").DepartmentId },
            new() { CategoryName = "Tài chính & Quy trình", OwnerDeptId = depts.First(d => d.DepartmentName == "Phòng Tài chính - Kế toán").DepartmentId }
        };
        context.Categories.AddRange(categories);
        await context.SaveChangesAsync();

        // 10. Tạo các Skills (Kỹ năng)
        var skills = new List<Skill>
        {
            new() { SkillName = "Tư duy Chiến lược", Description = "Khả năng phân tích và hoạch định chiến lược kinh doanh" },
            new() { SkillName = "An toàn Lao động (HSE)", Description = "Hiểu và thực hành đúng nội quy an toàn lao động" },
            new() { SkillName = "Bảo trì 5S", Description = "Thực hiện sàng lọc, sắp xếp, sạch sẽ tại nơi làm việc" },
            new() { SkillName = "Kỹ năng Thuyết trình", Description = "Tự tin nói trước đám đông và trình bày ý tưởng" },
            new() { SkillName = "An toàn thông tin", Description = "Bảo vệ thông tin và phòng chống Phishing" },
            new() { SkillName = "Quản lý Tài chính", Description = "Lập báo cáo và thanh quyết toán chi phí nội bộ" }
        };
        context.Skills.AddRange(skills);
        await context.SaveChangesAsync();

        // Yêu cầu kỹ năng theo phòng ban
        var reqSkills = new List<DeptRequiredSkill>
        {
            new() { DeptId = depts.First(d => d.DepartmentName == "Ban Giám đốc").DepartmentId, SkillId = skills.First(s => s.SkillName == "Tư duy Chiến lược").SkillId, MinLevelRequired = 3 },
            new() { DeptId = depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").DepartmentId, SkillId = skills.First(s => s.SkillName == "An toàn Lao động (HSE)").SkillId, MinLevelRequired = 2 },
            new() { DeptId = depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").DepartmentId, SkillId = skills.First(s => s.SkillName == "Bảo trì 5S").SkillId, MinLevelRequired = 2 },
            new() { DeptId = depts.First(d => d.DepartmentName == "Phòng Tài chính - Kế toán").DepartmentId, SkillId = skills.First(s => s.SkillName == "Quản lý Tài chính").SkillId, MinLevelRequired = 2 }
        };
        context.DeptRequiredSkills.AddRange(reqSkills);
        await context.SaveChangesAsync();

        // 11. Tạo 5 Khóa học mẫu thực tế tương ứng các phòng ban
        var courses = new List<Course>
        {
            // Khóa 1: Cho Công nhân & Toàn công ty (Level 1)
            new() {
                CourseCode = "HSE101",
                Title = "An toàn Lao động & Phòng cháy Chữa cháy (HSE)",
                Description = "Khóa học bắt buộc dành cho công nhân sản xuất và toàn bộ nhân viên nhằm nắm vững quy tắc an toàn lao động, sơ cứu y tế cơ bản và quy trình xử lý khi có cháy nổ tại nhà máy.",
                Thumbnail = "https://images.unsplash.com/photo-1590486803833-1c5dc8ddd4c8?auto=format&fit=crop&w=400&q=80",
                IsMandatory = true,
                Level = 1,
                Status = "Published",
                CategoryId = categories.First(c => c.CategoryName == "An toàn Lao động & Vận hành").CategoryId,
                OwnerDepartmentId = depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").DepartmentId,
                IsForAllDepartments = true,
                CreatedBy = userList.First(u => u.EmployeeCode == "KT0001").UserId,
                CreatedAt = DateTime.Now
            },
            // Khóa 2: Cho Công nhân sản xuất (Level 1)
            new() {
                CourseCode = "PRO102",
                Title = "Quy trình Vận hành Máy & Tiêu chuẩn 5S",
                Description = "Hướng dẫn công nhân cách vận hành máy móc an toàn, các bước bảo trì hàng ngày và áp dụng phương pháp 5S (Sàng lọc, Sắp xếp, Sạch sẽ, Săn sóc, Sẵn sàng) tại vị trí sản xuất.",
                Thumbnail = "https://images.unsplash.com/photo-1581091226825-a6a2a5aee158?auto=format&fit=crop&w=400&q=80",
                IsMandatory = true,
                Level = 1,
                Status = "Published",
                CategoryId = categories.First(c => c.CategoryName == "An toàn Lao động & Vận hành").CategoryId,
                OwnerDepartmentId = depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").DepartmentId,
                TargetDepartmentId = depts.First(d => d.DepartmentName == "Phòng Kỹ thuật & Sản xuất").DepartmentId,
                IsForAllDepartments = false,
                CreatedBy = userList.First(u => u.EmployeeCode == "KT0001").UserId,
                CreatedAt = DateTime.Now
            },
            // Khóa 3: Cho IT & Toàn công ty (Level 1)
            new() {
                CourseCode = "ITSEC101",
                Title = "An ninh thông tin & Phòng ngừa Phishing",
                Description = "Nâng cao nhận thức bảo mật, quy tắc đặt mật khẩu mạnh, nhận biết các email giả mạo và phương án báo cáo sự cố an ninh thông tin cho bộ phận IT.",
                Thumbnail = "https://images.unsplash.com/photo-1550751827-4bd374c3f58b?auto=format&fit=crop&w=400&q=80",
                IsMandatory = true,
                Level = 1,
                Status = "Published",
                CategoryId = categories.First(c => c.CategoryName == "Công nghệ & Bảo mật").CategoryId,
                OwnerDepartmentId = depts.First(d => d.DepartmentName == "Phòng Công nghệ thông tin").DepartmentId,
                IsForAllDepartments = true,
                CreatedBy = userList.First(u => u.Username == "admin").UserId,
                CreatedAt = DateTime.Now
            },
            // Khóa 4: Cho Kinh doanh & Marketing (Level 2)
            new() {
                CourseCode = "SALES201",
                Title = "Kỹ năng Bán hàng & Chăm sóc Khách hàng",
                Description = "Quy trình tìm kiếm, tiếp cận khách hàng tiềm năng, kỹ năng telesales và đàm phán xử lý từ chối chuyên nghiệp để chốt hợp đồng hiệu quả.",
                Thumbnail = "https://images.unsplash.com/photo-1460925895917-afdab827c52f?auto=format&fit=crop&w=400&q=80",
                IsMandatory = false,
                Level = 2,
                Status = "Published",
                CategoryId = categories.First(c => c.CategoryName == "Kỹ năng Lãnh đạo & Quản lý").CategoryId,
                OwnerDepartmentId = depts.First(d => d.DepartmentName == "Phòng Kinh doanh & Marketing").DepartmentId,
                TargetDepartmentId = depts.First(d => d.DepartmentName == "Phòng Kinh doanh & Marketing").DepartmentId,
                IsForAllDepartments = false,
                CreatedBy = userList.First(u => u.EmployeeCode == "KD0001").UserId,
                CreatedAt = DateTime.Now
            },
            // Khóa 5: Cho Kế toán & Toàn công ty (Level 1)
            new() {
                CourseCode = "FIN201",
                Title = "Quy trình Thanh toán & Tạm ứng Nội bộ",
                Description = "Hướng dẫn chi tiết quy chế lập chứng từ hóa đơn, quy trình trình duyệt tạm ứng và quyết toán chi phí công tác dành cho toàn thể nhân viên.",
                Thumbnail = "https://images.unsplash.com/photo-1454165804606-c3d57bc86b40?auto=format&fit=crop&w=400&q=80",
                IsMandatory = false,
                Level = 1,
                Status = "Published",
                CategoryId = categories.First(c => c.CategoryName == "Tài chính & Quy trình").CategoryId,
                OwnerDepartmentId = depts.First(d => d.DepartmentName == "Phòng Tài chính - Kế toán").DepartmentId,
                IsForAllDepartments = true,
                CreatedBy = userList.First(u => u.EmployeeCode == "TC0001").UserId,
                CreatedAt = DateTime.Now
            }
        };
        context.Courses.AddRange(courses);
        await context.SaveChangesAsync();

        // 12. Tạo các Modules cho từng khóa học
        
        // --- Modules HSE101 ---
        var modulesHse = new List<CourseModule>
        {
            new() { CourseId = courses[0].CourseId, Title = "Chương 1: Tổng quan về An toàn & Bảo hộ lao động", SortOrder = 1, Level = 1 },
            new() { CourseId = courses[0].CourseId, Title = "Chương 2: Phòng cháy chữa cháy & Cứu nạn cứu hộ", SortOrder = 2, Level = 1 },
            new() { CourseId = courses[0].CourseId, Title = "Chương 3: An toàn Điện & An toàn Hóa chất", SortOrder = 3, Level = 1 }
        };
        // --- Modules PRO102 ---
        var modulesPro = new List<CourseModule>
        {
            new() { CourseId = courses[1].CourseId, Title = "Chương 1: Tiêu chuẩn 5S Nhật Bản trong sản xuất", SortOrder = 1, Level = 1 },
            new() { CourseId = courses[1].CourseId, Title = "Chương 2: Quy trình Vận hành Máy và Bảo trì cấp 1", SortOrder = 2, Level = 1 }
        };
        // --- Modules ITSEC101 ---
        var modulesItSec = new List<CourseModule>
        {
            new() { CourseId = courses[2].CourseId, Title = "Chương 1: Bảo mật tài khoản & Đặt mật khẩu an toàn", SortOrder = 1, Level = 1 },
            new() { CourseId = courses[2].CourseId, Title = "Chương 2: Nhận diện Email giả mạo (Phishing)", SortOrder = 2, Level = 1 }
        };
        // --- Modules SALES201 ---
        var modulesSales = new List<CourseModule>
        {
            new() { CourseId = courses[3].CourseId, Title = "Chương 1: Quy trình Tìm kiếm & Tiếp cận Khách hàng", SortOrder = 1, Level = 2 },
            new() { CourseId = courses[3].CourseId, Title = "Chương 2: Kỹ năng Xử lý Từ chối & Chốt sales", SortOrder = 2, Level = 2 }
        };
        // --- Modules FIN201 ---
        var modulesFin = new List<CourseModule>
        {
            new() { CourseId = courses[4].CourseId, Title = "Chương 1: Quy định Hóa đơn & Chứng từ hợp lệ", SortOrder = 1, Level = 1 },
            new() { CourseId = courses[4].CourseId, Title = "Chương 2: Quy trình Tạm ứng & Thanh quyết toán Chi phí", SortOrder = 2, Level = 1 }
        };

        var allModules = new List<CourseModule>();
        allModules.AddRange(modulesHse);
        allModules.AddRange(modulesPro);
        allModules.AddRange(modulesItSec);
        allModules.AddRange(modulesSales);
        allModules.AddRange(modulesFin);

        context.CourseModules.AddRange(allModules);
        await context.SaveChangesAsync();

        // 13. Tạo các Lessons (Bài học)
        var lessons = new List<Lesson>
        {
            // HSE Lessons
            new() { 
                ModuleId = modulesHse[0].ModuleId, 
                Title = "Bài 1: Nội quy an toàn chung tại nhà máy", 
                ContentType = "Text", 
                ContentBody = "<h1>Nội quy an toàn lao động nhà máy</h1><p>1. Tất cả nhân viên và khách tham quan phải mặc trang phục bảo hộ phù hợp khi bước vào khu vực sản xuất.</p><p>2. Không vận hành bất kỳ thiết bị nào khi chưa được đào tạo hoặc hướng dẫn cụ thể từ tổ trưởng.</p><p>3. Tuyệt đối tuân thủ biển cảnh báo và rào chắn an toàn.</p>", 
                VideoUrl = "https://www.w3schools.com/html/mov_bbb.mp4",
                SortOrder = 1, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesHse[0].ModuleId, 
                Title = "Bài 2: Sử dụng trang thiết bị bảo hộ cá nhân (PPE)", 
                ContentType = "Text", 
                ContentBody = "<h1>Hướng dẫn sử dụng PPE đúng cách</h1><p>PPE bao gồm: Mũ bảo hộ, Kính an toàn, Nút tai chống ồn, Găng tay bảo hộ và Giày mũi sắt chống đinh.</p><p>Nhân viên có trách nhiệm kiểm tra thiết bị bảo hộ của mình trước mỗi ca làm việc để phát hiện hư hỏng kịp thời.</p>", 
                VideoUrl = "",
                SortOrder = 2, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesHse[1].ModuleId, 
                Title = "Bài 1: Quy trình ứng phó khi có hỏa hoạn xảy ra", 
                ContentType = "Text", 
                ContentBody = "<h1>Hệ thống phòng cháy chữa cháy</h1><p>Trong trường hợp có còi báo cháy: Ngắt nguồn điện máy móc gần nhất, di chuyển nhanh ra lối thoát hiểm gần nhất theo chỉ dẫn đèn Exit.</p>", 
                VideoUrl = "https://www.w3schools.com/html/movie.mp4",
                SortOrder = 1, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesHse[1].ModuleId, 
                Title = "Bài 2: Kỹ thuật sử dụng bình chữa cháy CO2 và bình bột", 
                ContentType = "Text", 
                ContentBody = "<h1>Sử dụng bình cứu hỏa xách tay</h1><p>Sử dụng quy tắc PASS khi dùng bình cứu hỏa: Pull pin (Rút chốt) - Aim low (Hướng vòi vào gốc lửa) - Squeeze lever (Bóp cò) - Sweep (Quét qua lại).</p>", 
                VideoUrl = "",
                SortOrder = 2, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesHse[2].ModuleId, 
                Title = "Bài 1: Quy tắc an toàn điện trong vận hành máy móc", 
                ContentType = "Text", 
                ContentBody = "<h1>Nguyên tắc an toàn điện</h1><p>Không dùng tay ướt cắm phích điện hoặc chạm vào tủ điện chính. Bảo đảm thiết bị đã được nối đất và có aptomat chống giật.</p>", 
                VideoUrl = "https://www.w3schools.com/html/mov_bbb.mp4",
                SortOrder = 1, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesHse[2].ModuleId, 
                Title = "Bài 2: Xử lý sự cố rò rỉ hóa chất và sơ cứu ban đầu", 
                ContentType = "Text", 
                ContentBody = "<h1>Quy trình xử lý hóa chất tràn đổ</h1><p>Khi hóa chất tràn đổ, khoanh vùng rò rỉ, dùng cát thấm chất lỏng và đeo khẩu trang chống hơi độc để dọn dẹp. Rửa sạch da/mắt dưới vòi nước sạch 15 phút nếu dính hóa chất.</p>", 
                VideoUrl = "",
                SortOrder = 2, 
                Level = 1 
            },

            // PRO 5S Lessons
            new() { 
                ModuleId = modulesPro[0].ModuleId, 
                Title = "Bài 1: 5S là gì? Thực hành Seiri, Seiton, Seiso, Seiketsu, Shitsuke", 
                ContentType = "Text", 
                ContentBody = "<h1>Khái niệm 5S Nhật Bản</h1><p>1. Seiri (Sàng lọc): Loại bỏ vật dụng không cần thiết.<br>2. Seiton (Sắp xếp): Đặt vật dụng đúng nơi quy định.<br>3. Seiso (Sạch sẽ): Lau chùi vệ sinh máy móc hàng ngày.<br>4. Seiketsu (Săn sóc): Duy trì và chuẩn hóa 3S trên.<br>5. Shitsuke (Sẵn sàng): Tạo thói quen tự giác tuân thủ.</p>", 
                VideoUrl = "https://www.w3schools.com/html/movie.mp4",
                SortOrder = 1, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesPro[1].ModuleId, 
                Title = "Bài 1: Quy trình khởi động, kiểm tra và tắt máy an toàn", 
                ContentType = "Text", 
                ContentBody = "<h1>Vận hành máy đúng kỹ thuật</h1><p>Bước 1: Kiểm tra ngoại quan máy và dây dẫn điện.<br>Bước 2: Bật công tắc nguồn chính.<br>Bước 3: Nhấn nút Start. Lưu ý luôn giữ tay khô ráo khi thao tác điện.<br>Khi hoàn thành ca hoặc có sự cố, nhấn nút dừng khẩn cấp (Emergency Stop) để dừng máy ngay lập tức.</p>", 
                VideoUrl = "",
                SortOrder = 1, 
                Level = 1 
            },

            // ITSEC Lessons
            new() { 
                ModuleId = modulesItSec[0].ModuleId, 
                Title = "Bài 1: Nguyên tắc đặt mật khẩu mạnh và bảo mật đa lớp (MFA)", 
                ContentType = "Text", 
                ContentBody = "<h1>Quy tắc đặt mật khẩu mạnh</h1><p>Mật khẩu tối thiểu 8 ký tự, kết hợp chữ hoa, chữ thường, số và ký tự đặc biệt. Kích hoạt tính năng MFA (xác thực đa nhân tố) trên tài khoản làm việc của công ty.</p>", 
                VideoUrl = "https://www.w3schools.com/html/mov_bbb.mp4",
                SortOrder = 1, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesItSec[1].ModuleId, 
                Title = "Bài 1: Cách kiểm tra liên kết lạ và xử lý khi nghi ngờ bị tấn công Phishing", 
                ContentType = "Text", 
                ContentBody = "<h1>Phòng chống Phishing email</h1><p>Phishing là hình thức kẻ giả danh đối tác, sếp dụ người dùng nhấp vào link độc hại. Quy tắc vàng: Không nhấp link lạ trong email khẩn cấp, di chuột lên link để xem URL thực tế.</p>", 
                VideoUrl = "",
                SortOrder = 1, 
                Level = 1 
            },

            // SALES Lessons
            new() { 
                ModuleId = modulesSales[0].ModuleId, 
                Title = "Bài 1: Xác định chân dung khách hàng và kỹ thuật chào hàng qua điện thoại (Telesales)", 
                ContentType = "Text", 
                ContentBody = "<h1>Tìm kiếm Khách hàng</h1><p>Phân tích nhu cầu của doanh nghiệp để vẽ chân dung khách hàng. Chuẩn bị kịch bản cuộc gọi điện thoại chào hàng, hướng tới mục tiêu thiết lập một buổi hẹn gặp trực tiếp.</p>", 
                VideoUrl = "https://www.w3schools.com/html/movie.mp4",
                SortOrder = 1, 
                Level = 2 
            },
            new() { 
                ModuleId = modulesSales[1].ModuleId, 
                Title = "Bài 1: Các tình huống từ chối phổ biến và phương pháp đàm phán thuyết phục", 
                ContentType = "Text", 
                ContentBody = "<h1>Xử lý từ chối trong bán hàng</h1><p>Khi khách hàng từ chối về giá, hãy nhấn mạnh giá trị sản phẩm mang lại so với đối thủ và đưa ra bài toán lợi ích kinh tế lâu dài thay vì giảm giá vội vàng.</p>", 
                VideoUrl = "",
                SortOrder = 1, 
                Level = 2 
            },

            // FIN Lessons
            new() { 
                ModuleId = modulesFin[0].ModuleId, 
                Title = "Bài 1: Cách phân biệt hóa đơn GTGT hợp pháp và chứng từ thanh toán hợp lệ", 
                ContentType = "Text", 
                ContentBody = "<h1>Hóa đơn chứng từ kế toán</h1><p>Hóa đơn mua hàng từ 20 triệu trở lên bắt buộc phải chuyển khoản qua tài khoản ngân hàng của công ty. Hóa đơn GTGT đầu vào phải ghi chính xác tên doanh nghiệp và mã số thuế.</p>", 
                VideoUrl = "https://www.w3schools.com/html/mov_bbb.mp4",
                SortOrder = 1, 
                Level = 1 
            },
            new() { 
                ModuleId = modulesFin[1].ModuleId, 
                Title = "Bài 1: Hướng dẫn lập đề nghị tạm ứng, duyệt chi và hoàn ứng đúng thời hạn", 
                ContentType = "Text", 
                ContentBody = "<h1>Tạm ứng và hoàn ứng</h1><p>Nhân viên lập phiếu đề xuất tạm ứng chi phí công tác được duyệt bởi trưởng phòng. Hoàn ứng trong vòng tối đa 5 ngày làm việc sau khi kết thúc công tác kèm đầy đủ hóa đơn chứng từ.</p>", 
                VideoUrl = "",
                SortOrder = 1, 
                Level = 1 
            }
        };

        context.Lessons.AddRange(lessons);
        await context.SaveChangesAsync();

        // Thêm tài liệu đính kèm (Attachments) cho một số bài học
        var attachments = new List<LessonAttachment>
        {
            new() { LessonId = lessons[0].LessonId, FileName = "Nội quy An toàn nhà xưởng 2026.pdf", FilePath = "/attachments/Safety_Rules_2026.pdf" },
            new() { LessonId = lessons[1].LessonId, FileName = "Danh mục thiết bị PPE tiêu chuẩn.pdf", FilePath = "/attachments/PPE_List.pdf" },
            new() { LessonId = lessons[3].LessonId, FileName = "Cẩm nang PCCC công nghiệp.pdf", FilePath = "/attachments/PCCC_Guide.pdf" },
            new() { LessonId = lessons[4].LessonId, FileName = "Tiêu chuẩn an toàn điện cao áp.pdf", FilePath = "/attachments/Electrical_Safety.pdf" },
            new() { LessonId = lessons[12].LessonId, FileName = "Mẫu hóa đơn GTGT hợp lệ.pdf", FilePath = "/attachments/Valid_Invoice_Template.pdf" }
        };
        context.LessonAttachments.AddRange(attachments);
        await context.SaveChangesAsync();

        // 14. Tạo Đề thi kiểm tra (Exams) gắn với từng module (chương)

        // --- Exams HSE101 ---
        var examHse1 = new Exam { CourseId = courses[0].CourseId, ModuleId = modulesHse[0].ModuleId, ExamTitle = "HSE101 - Trắc nghiệm Chương 1: Quy tắc an toàn & PPE", DurationMinutes = 15, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };
        var examHse2 = new Exam { CourseId = courses[0].CourseId, ModuleId = modulesHse[1].ModuleId, ExamTitle = "HSE101 - Trắc nghiệm Chương 2: PCCC & Cứu nạn cứu hộ", DurationMinutes = 15, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };
        var examHse3 = new Exam { CourseId = courses[0].CourseId, ModuleId = modulesHse[2].ModuleId, ExamTitle = "HSE101 - Trắc nghiệm Chương 3: An toàn Điện & Hóa chất", DurationMinutes = 20, PassScore = 100m, Level = 1, MaxAttempts = 5, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };

        // --- Exams PRO102 ---
        var examPro1 = new Exam { CourseId = courses[1].CourseId, ModuleId = modulesPro[0].ModuleId, ExamTitle = "PRO102 - Trắc nghiệm Chương 1: Đánh giá tiêu chuẩn 5S", DurationMinutes = 10, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };
        var examPro2 = new Exam { CourseId = courses[1].CourseId, ModuleId = modulesPro[1].ModuleId, ExamTitle = "PRO102 - Trắc nghiệm Chương 2: Vận hành máy & Sự cố", DurationMinutes = 10, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };

        // --- Exams ITSEC101 ---
        var examItSec1 = new Exam { CourseId = courses[2].CourseId, ModuleId = modulesItSec[0].ModuleId, ExamTitle = "ITSEC101 - Trắc nghiệm Chương 1: Bảo mật tài khoản", DurationMinutes = 10, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };
        var examItSec2 = new Exam { CourseId = courses[2].CourseId, ModuleId = modulesItSec[1].ModuleId, ExamTitle = "ITSEC101 - Trắc nghiệm Chương 2: Phòng chống Phishing", DurationMinutes = 15, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };

        // --- Exams SALES201 ---
        var examSales1 = new Exam { CourseId = courses[3].CourseId, ModuleId = modulesSales[0].ModuleId, ExamTitle = "SALES201 - Trắc nghiệm Chương 1: Tìm kiếm & Tiếp cận", DurationMinutes = 10, PassScore = 80m, Level = 2, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };
        var examSales2 = new Exam { CourseId = courses[3].CourseId, ModuleId = modulesSales[1].ModuleId, ExamTitle = "SALES201 - Trắc nghiệm Chương 2: Xử lý từ chối", DurationMinutes = 15, PassScore = 80m, Level = 2, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };

        // --- Exams FIN201 ---
        var examFin1 = new Exam { CourseId = courses[4].CourseId, ModuleId = modulesFin[0].ModuleId, ExamTitle = "FIN201 - Trắc nghiệm Chương 1: Hóa đơn & Chứng từ", DurationMinutes = 10, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };
        var examFin2 = new Exam { CourseId = courses[4].CourseId, ModuleId = modulesFin[1].ModuleId, ExamTitle = "FIN201 - Trắc nghiệm Chương 2: Tạm ứng & Quyết toán", DurationMinutes = 10, PassScore = 100m, Level = 1, MaxAttempts = 3, StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(15) };

        var allExams = new List<Exam> { examHse1, examHse2, examHse3, examPro1, examPro2, examItSec1, examItSec2, examSales1, examSales2, examFin1, examFin2 };
        context.Exams.AddRange(allExams);
        await context.SaveChangesAsync();

        // 15. Gieo câu hỏi trắc nghiệm thông qua helper

        int catSafety = categories.First(c => c.CategoryName == "An toàn Lao động & Vận hành").CategoryId;
        int catSoft = categories.First(c => c.CategoryName == "Kỹ năng Mềm & Phát triển Bản thân").CategoryId;
        int catTech = categories.First(c => c.CategoryName == "Công nghệ & Bảo mật").CategoryId;
        int catLeader = categories.First(c => c.CategoryName == "Kỹ năng Lãnh đạo & Quản lý").CategoryId;
        int catFinance = categories.First(c => c.CategoryName == "Tài chính & Quy trình").CategoryId;

        // --- HSE101 Chương 1: 10 câu ---
        var hseCh1Questions = new List<SeedQuestion>
        {
            new() { Text = "Mục tiêu hàng đầu của công tác An toàn & Vệ sinh lao động tại nhà máy là gì?", Options = new() { "Tăng tối đa năng suất sản xuất bất chấp rủi ro", "Bảo vệ tính mạng, sức khỏe của người lao động và tài sản công ty", "Tiết kiệm chi phí mua sắm trang thiết bị bảo hộ", "Giảm thời gian nghỉ giải lao của công nhân" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi vào khu vực sản xuất có tiếng ồn máy móc vượt quy chuẩn cho phép, thiết bị PPE nào sau đây là bắt buộc?", Options = new() { "Kính bảo hộ chống bụi", "Khẩu trang hoạt tính", "Nút tai hoặc chụp tai chống ồn", "Mặt nạ phòng độc" }, CorrectOptionIndex = 2 },
            new() { Text = "Ai có trách nhiệm chính trong việc tự kiểm tra thiết bị bảo hộ cá nhân (PPE) trước ca làm việc?", Options = new() { "Trưởng phòng nhân sự", "Chính bản thân người lao động được cấp phát thiết bị", "Giám đốc điều hành công ty", "Nhân viên bảo vệ nhà máy" }, CorrectOptionIndex = 1 },
            new() { Text = "Mũ bảo hộ lao động đạt tiêu chuẩn có tác dụng cốt lõi nào sau đây?", Options = new() { "Bảo vệ vùng đầu tránh khỏi lực va đập, vật rơi từ trên cao và cách điện nhẹ", "Giúp đầu mát mẻ hơn khi làm việc ngoài trời nắng", "Thay thế cho việc chải tóc trước khi vào xưởng", "Làm đẹp và thể hiện cấp bậc chức vụ của nhân viên" }, CorrectOptionIndex = 0 },
            new() { Text = "Khi phát hiện kính bảo hộ hoặc găng tay của mình bị nứt, rách, hỏng, người lao động nên xử lý thế nào?", Options = new() { "Vẫn tiếp tục dùng tạm để không ảnh hưởng đến tiến độ", "Tự dùng băng keo dán lại để tiết kiệm cho công ty", "Báo cáo ngay với Tổ trưởng hoặc Giám sát an toàn để được cấp mới lập tức", "Đợi đến cuối tháng mới xin đổi thiết bị mới" }, CorrectOptionIndex = 2 },
            new() { Text = "Biển báo an toàn lao động hình tam giác màu vàng, viền đen và hình vẽ màu đen biểu thị thông tin gì?", Options = new() { "Biển chỉ dẫn lối thoát hiểm khẩn cấp", "Biển báo hành động bắt buộc phải thực hiện", "Biển báo cấm thực hiện hành vi", "Biển cảnh báo nguy hiểm hoặc khu vực có rủi ro cao" }, CorrectOptionIndex = 3 },
            new() { Text = "Biển báo hình tròn màu xanh lam có hình vẽ màu trắng biểu thị điều gì?", Options = new() { "Cấm đi vào khu vực này", "Hành động bắt buộc người lao động phải thực hiện (ví dụ: Phải đeo kính bảo hộ)", "Chỉ dẫn khu vực hút thuốc lá", "Thông báo chất độc hại chết người" }, CorrectOptionIndex = 1 },
            new() { Text = "Hành vi nào sau đây bị nghiêm cấm tuyệt đối trước và trong khi thực hiện ca làm việc?", Options = new() { "Uống nước lọc hoặc nước trà", "Ăn nhẹ trong khu vực nghỉ giải lao quy định", "Sử dụng rượu bia, chất kích thích hoặc tự ý rời bỏ vị trí vận hành máy nguy hiểm", "Trao đổi công việc chuyên môn với đồng nghiệp" }, CorrectOptionIndex = 2 },
            new() { Text = "Khi cần nâng một thùng hàng nặng từ mặt đất lên kệ, tư thế bê vác nào là đúng luật an toàn?", Options = new() { "Cúi gập lưng, giữ thẳng chân và dùng lực cơ lưng kéo hàng lên", "Giữ thẳng lưng, gập đầu gối hạ thấp trọng tâm, ôm sát vật nặng rồi dùng lực chân đứng lên", "Nghiêng người sang một bên để nâng bằng một tay", "Bê hàng nhanh nhất có thể mà không cần chú ý tư thế" }, CorrectOptionIndex = 1 },
            new() { Text = "Tại sao việc sắp xếp gọn gàng vật tư và giữ lối đi nhà xưởng luôn thông thoáng lại vô cùng quan trọng?", Options = new() { "Để nhà máy trông đẹp mắt hơn khi đón đoàn kiểm tra", "Để công nhân không có chỗ ngồi nghỉ ngơi", "Để giảm thiểu nguy cơ vấp ngã và đảm bảo di chuyển thoát nạn nhanh chóng khi xảy ra sự cố", "Để tăng diện tích trống để chứa thêm phế liệu" }, CorrectOptionIndex = 2 }
        };
        await SeedExamQuestions(context, examHse1.ExamId, catSafety, hseCh1Questions, 10m);

        // --- HSE101 Chương 2: 12 câu ---
        var hseCh2Questions = new List<SeedQuestion>
        {
            new() { Text = "Khi đột ngột phát hiện đám cháy xảy ra trong nhà xưởng, hành động đầu tiên và khẩn cấp nhất bạn cần làm là gì?", Options = new() { "Thu dọn tư trang, tài liệu cá nhân rồi mới ra ngoài", "Hô hoán to, nhấn nút báo cháy khẩn cấp để báo động cho toàn bộ khu vực", "Tự mình tìm nước dập lửa mà không báo cho ai biết", "Lập tức gọi điện thoại cho gia đình để thông báo" }, CorrectOptionIndex = 1 },
            new() { Text = "Số điện thoại khẩn cấp quốc gia để báo cháy và yêu cầu cứu nạn cứu hộ tại Việt Nam là số nào?", Options = new() { "113", "114", "115", "111" }, CorrectOptionIndex = 1 },
            new() { Text = "Quy tắc PASS dùng để hướng dẫn sử dụng bình chữa cháy xách tay bao gồm thứ tự các bước nào?", Options = new() { "Press (Ấn còi) - Aim (Nhắm) - Squeeze (Bóp cò) - Start (Khởi động)", "Pull (Rút chốt) - Aim (Hướng vòi vào gốc lửa) - Squeeze (Bóp cò) - Sweep (Quét vòi qua lại)", "Push (Đẩy bình) - Act (Hành động) - Sweep (Quét) - Stop (Dừng lại)", "Pull (Rút chốt) - Aim (Hướng vòi vào ngọn lửa) - Shake (Lắc bình) - Sweep (Quét)" }, CorrectOptionIndex = 1 },
            new() { Text = "Bình chữa cháy bằng khí CO2 (carbon dioxide) KHÔNG nên sử dụng cho đám cháy nào sau đây?", Options = new() { "Đám cháy thiết bị điện, bảng điện tử", "Đám cháy trong phòng kín chứa máy chủ (Server)", "Đám cháy ngoài trời có gió lớn hoặc đám cháy kim loại kiềm, than gỗ sinh nhiệt cao", "Đám cháy chất lỏng như xăng, dầu trong nhà" }, CorrectOptionIndex = 2 },
            new() { Text = "Khi phải di chuyển thoát nạn qua khu vực hành lang có nhiều khói đen mù mịt, tư thế nào giúp hạn chế hít khói độc tốt nhất?", Options = new() { "Đi thẳng người, hít thở thật sâu để chạy nhanh", "Dùng khăn ướt che mũi miệng, cúi khom lưng thật thấp hoặc bò sát mặt đất để di chuyển", "Chạy thật nhanh và liên tục la hét để mọi người nghe thấy", "Trùm chăn khô qua đầu và đi bình thường" }, CorrectOptionIndex = 1 },
            new() { Text = "Để dập tắt một đám cháy nhỏ mới phát sinh do xăng hoặc dầu hỏa rò rỉ, chất chữa cháy nào tuyệt đối KHÔNG được dùng?", Options = new() { "Cát khô", "Bình bột chữa cháy hệ ABC", "Chăn dạ nhúng ướt", "Nước lã trực tiếp từ vòi sinh hoạt" }, CorrectOptionIndex = 3 },
            new() { Text = "Khi chuông báo cháy của tòa nhà/nhà máy vang lên dồn dập, phương tiện di chuyển nào bị cấm sử dụng?", Options = new() { "Cầu thang thoát hiểm bộ ngoài trời", "Thang máy của tòa nhà", "Cầu thang bộ bên trong tòa nhà", "Cửa thoát hiểm tầng trệt" }, CorrectOptionIndex = 1 },
            new() { Text = "Khoảng cách đứng an toàn tối thiểu khi bắt đầu phun bình chữa cháy khí CO2 vào đám cháy là bao nhiêu?", Options = new() { "Khoảng 0.5 mét để tiếp cận ngọn lửa tốt nhất", "Từ 1.5 đến 2.0 mét để tránh nhiệt độ cao và tạt ngược lửa", "Trên 5 mét", "Đứng càng xa càng tốt không giới hạn" }, CorrectOptionIndex = 1 },
            new() { Text = "Để dập tắt đám cháy triệt để, loa phun hoặc vòi phun của bình chữa cháy cần hướng vào vị trí nào?", Options = new() { "Phần trên cùng của ngọn lửa đang bốc cao", "Xung quanh đám cháy để bao vây", "Gốc của ngọn lửa (nơi phát sinh nguồn cháy)", "Phun trực tiếp vào khói bốc lên" }, CorrectOptionIndex = 2 },
            new() { Text = "Lối thoát nạn tiêu chuẩn tại các phân xưởng sản xuất phải đáp ứng yêu cầu tối thiểu nào?", Options = new() { "Luôn mở tự do, không bị khóa xích, không bị che chắn bởi vật tư và có biển EXIT chiếu sáng dự phòng", "Có thể khóa lại để tránh mất cắp tài sản công ty khi làm việc", "Chỉ cần rộng khoảng 0.5m là đủ", "Chỉ mở cửa vào ban ngày, ban đêm khóa lại" }, CorrectOptionIndex = 0 },
            new() { Text = "Bình bột chữa cháy xách tay ký hiệu chữ ABC thể hiện khả năng dập tắt đám cháy nào?", Options = new() { "A: Thiết bị điện; B: Kim loại; C: Chất hóa học", "A: Đám cháy chất rắn; B: Đám cháy chất lỏng; C: Đám cháy chất khí", "A: Xăng dầu; B: Gỗ giấy; C: Khí Gas", "A: Axit; B: Bazơ; C: Muối" }, CorrectOptionIndex = 1 },
            new() { Text = "Sau khi đã di chuyển ra ngoài tòa nhà bị hỏa hoạn an toàn, hành động tiếp theo của bạn là gì?", Options = new() { "Lập tức quay lại xưởng lấy xe máy hoặc đồ đạc để quên", "Tập trung tại Điểm tập kết an toàn đã quy định để điểm danh quân số và báo cáo chỉ huy", "Tự ý đi về nhà vì công việc đã bị gián đoạn", "Đứng tụ tập ngay trước lối ra vào để xem lực lượng cứu hỏa làm việc" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examHse2.ExamId, catSafety, hseCh2Questions, 8.33m);

        // --- HSE101 Chương 3: 15 câu ---
        var hseCh3Questions = new List<SeedQuestion>
        {
            new() { Text = "Cường độ dòng điện chạy qua cơ thể người từ mức nào trở lên bắt đầu có thể gây co giật cơ, khó thở và nguy hiểm?", Options = new() { "Từ 1 mA đến 5 mA", "Từ 10 mA (miliampe) trở lên đối với dòng điện xoay chiều 50Hz", "Chỉ từ 1 A (ampe) trở lên", "Dưới 0.5 mA" }, CorrectOptionIndex = 1 },
            new() { Text = "Trước khi tiến hành sửa chữa bảo trì hệ thống điện của máy móc, nguyên tắc an toàn bắt buộc hàng đầu là gì?", Options = new() { "Đeo găng tay vải thông thường", "Ngắt nguồn điện chính, khóa nguồn bằng ổ khóa cá nhân và treo biển báo cảnh báo nguy hiểm (LOTO)", "Dùng bút thử điện đo khi máy vẫn đang chạy", "Tắt công tắc nguồn phụ của máy là đủ" }, CorrectOptionIndex = 1 },
            new() { Text = "Thiết bị bảo vệ điện nào tự động cắt nguồn điện khi phát hiện dòng điện rò rỉ ra vỏ máy hoặc có người chạm vào phần dẫn điện?", Options = new() { "Cầu chì dây chì", "Công tắc xoay", "Thiết bị chống rò dòng (ELCB / RCBO)", "Biến áp cách ly tự ngẫu" }, CorrectOptionIndex = 2 },
            new() { Text = "Dụng cụ cầm tay dùng cho sửa chữa thiết bị điện (kìm, tuốc nơ vít...) phải đảm bảo tiêu chuẩn kỹ thuật gì?", Options = new() { "Tay cầm kim loại bóng loáng dễ lau chùi", "Tay cầm được bọc lớp nhựa hoặc cao su cách điện đạt tiêu chuẩn điện áp sử dụng và không bị rách, nứt", "Chỉ cần quấn vài vòng băng dính đen thông thường", "Là dụng cụ có trọng lượng nhẹ nhất" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi phát hiện một đồng nghiệp đang bị điện giật dính chặt vào nguồn điện sinh hoạt, bước sơ cứu đầu tiên phải làm là gì?", Options = new() { "Lập tức dùng tay không kéo mạnh nạn nhân ra ngoài", "Tìm cách ngắt ngay cầu dao điện gần nhất hoặc dùng vật khô không dẫn điện (như gậy gỗ, tre khô) cô lập nguồn điện khỏi nạn nhân", "Dội nước vào nạn nhân để hạ nhiệt", "Gọi điện thoại cấp cứu 115 trước rồi đứng chờ cứu hộ đến" }, CorrectOptionIndex = 1 },
            new() { Text = "Hóa chất nguy hiểm sử dụng trong sản xuất cần được bảo quản và lưu trữ như thế nào?", Options = new() { "Để chung với vật tư thiết bị điện để dễ tìm kiếm", "Đựng trong thùng chứa chuyên dụng có nhãn mác rõ ràng, bảo quản trong kho thông gió, có rãnh chống tràn và biển cảnh báo nguy hiểm", "Để tự do trên sàn xưởng gần lối đi cho tiện sử dụng", "Đựng vào chai nhựa nước ngọt cũ không cần ghi nhãn" }, CorrectOptionIndex = 1 },
            new() { Text = "Bảng chỉ dẫn an toàn hóa chất bắt buộc phải đi kèm mỗi loại hóa chất công nghiệp có tên viết tắt quốc tế là gì?", Options = new() { "ISO 9001", "KPI", "MSDS (Material Safety Data Sheet) hoặc SDS", "WHO" }, CorrectOptionIndex = 2 },
            new() { Text = "Khi thực hiện pha loãng Axit Sunfuric (H2SO4) đậm đặc từ bình chứa sang cốc thí nghiệm, quy tắc rót an toàn là gì?", Options = new() { "Rót nước thật nhanh vào cốc chứa sẵn axit đậm đặc", "Rót từ từ axit đậm đặc dọc theo đũa thủy tinh vào cốc chứa sẵn nước và khuấy đều nhẹ nhàng", "Đổ đồng thời cả nước và axit vào cốc cùng một lúc", "Quy tắc nào cũng được miễn là đeo kính bảo hộ" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi không may bị hóa chất ăn mòn (như axit hoặc kiềm) bắn vào mắt, bạn cần làm gì ngay lập tức tại hiện trường?", Options = new() { "Dùng tay dụi mắt thật mạnh để trôi hóa chất", "Đến ngay bồn rửa mắt khẩn cấp, mở to mắt rửa liên tục dưới vòi nước sạch chảy nhẹ trong ít nhất 15-20 phút rồi mới chuyển đến cơ sở y tế", "Dùng dung dịch thuốc nhỏ mắt thông thường để rửa", "Đợi hóa chất tự khô rồi lau sạch" }, CorrectOptionIndex = 1 },
            new() { Text = "Ký hiệu hình xương chéo nằm trong hình thoi viền đỏ trên nhãn chai hóa chất cảnh báo điều gì?", Options = new() { "Chất dễ cháy nổ", "Hóa chất ăn mòn da", "Chất độc nguy hiểm cấp tính, có thể gây tử vong khi hít phải, nuốt phải hoặc tiếp xúc qua da", "Chất gây nguy hại cho môi trường nước" }, CorrectOptionIndex = 2 },
            new() { Text = "Tại sao tuyệt đối không được lưu trữ hoặc để hóa chất dễ cháy gần tủ điện, bảng điện hoặc thiết bị phát sinh tia lửa?", Options = new() { "Vì tủ điện chiếm diện tích để hóa chất", "Vì tia lửa điện hoặc nhiệt độ từ thiết bị điện có thể kích hoạt đám cháy hoặc vụ nổ hóa chất cực kỳ nghiêm trọng", "Vì hóa chất làm rỉ sét vỏ kim loại của tủ điện", "Vì quy định thẩm mỹ của nhà máy" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi ngửi thấy mùi khí gas rò rỉ nồng nặc trong phòng chứa gas của nhà máy, hành động nào sau đây bị NGHIÊM CẤM thực hiện?", Options = new() { "Bật quạt hút thông gió điện hoặc bật/tắt bất kỳ công tắc điện nào để tránh phát sinh tia lửa điện gây nổ", "Mở rộng tất cả các cửa sổ và cửa ra vào để thông gió tự nhiên", "Khóa van tổng nguồn cấp gas", "Di chuyển nhanh ra ngoài khu vực thoáng khí và gọi cứu hộ" }, CorrectOptionIndex = 0 },
            new() { Text = "Trong quy trình LOTO (Lockout/Tagout), bước 'Tagout' có nghĩa là gì?", Options = new() { "Khóa chặt tay ga của van khí", "Gắn thẻ cảnh báo ghi rõ tên người sửa chữa, ngày giờ và nội dung công việc lên vị trí thiết bị khóa nguồn điện/năng lượng", "Đo dòng điện rò vỏ máy", "Kiểm tra xem máy có khởi động được không" }, CorrectOptionIndex = 1 },
            new() { Text = "Trường hợp quần áo bảo hộ lao động bị dính hóa chất độc hại đậm đặc, người lao động phải làm thế nào?", Options = new() { "Vẫn mặc tiếp cho đến hết ca làm việc để giặt một thể", "Thay ngay lập tức quần áo dính hóa chất, tắm rửa sạch vùng da tiếp xúc bằng nước ấm và sử dụng trang phục dự phòng", "Dùng khăn giấy lau khô chỗ dính rồi làm việc tiếp", "Phun nước trực tiếp lên người khi vẫn đang mặc nguyên bộ quần áo đó" }, CorrectOptionIndex = 1 },
            new() { Text = "Việc tự ý đổ chất thải hóa chất nguy hại trực tiếp xuống cống thoát nước chung của nhà máy sẽ dẫn đến hậu quả gì?", Options = new() { "Giúp thông tắc cống nhanh hơn nhờ tính axit", "Là hành vi vi phạm pháp luật môi trường nghiêm trọng, gây ô nhiễm nguồn nước và công ty sẽ bị phạt rất nặng", "Không ảnh hưởng gì nếu đổ lượng nhỏ dưới 5 lít", "Chỉ bị khiển trách nhẹ nội bộ" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examHse3.ExamId, catSafety, hseCh3Questions, 6.66m);

        // --- PRO102 Chương 1: 10 câu ---
        var proCh1Questions = new List<SeedQuestion>
        {
            new() { Text = "Tiêu chuẩn 5S bắt nguồn từ quốc gia nào?", Options = new() { "Mỹ", "Đức", "Nhật Bản", "Hàn Quốc" }, CorrectOptionIndex = 2 },
            new() { Text = "Chữ S đầu tiên 'Seiri' (Sàng lọc) có nghĩa là gì?", Options = new() { "Sắp đặt ngăn nắp", "Phân loại và loại bỏ vật dùng thừa không cần thiết", "Lau chùi vệ sinh máy móc", "Học tập thói quen tốt" }, CorrectOptionIndex = 1 },
            new() { Text = "Chữ S thứ hai 'Seiton' (Sắp xếp) yêu cầu điều gì?", Options = new() { "Sắp đặt mọi thứ ngăn nắp, có nhãn mác vị trí rõ ràng", "Bỏ tất cả vào tủ khóa lại", "Lau dọn xưởng hàng tuần", "Vứt bỏ tài liệu cũ" }, CorrectOptionIndex = 0 },
            new() { Text = "Chữ S thứ ba 'Seiso' (Sạch sẽ) khuyên chúng ta làm gì?", Options = new() { "Phê bình đồng nghiệp lười biếng", "Vệ sinh máy móc, dụng cụ và khu vực làm việc sạch sẽ trước/sau ca", "Đeo khẩu trang an toàn", "Vẽ sơ đồ vị trí công cụ" }, CorrectOptionIndex = 1 },
            new() { Text = "Chữ S thứ tư 'Seiketsu' (Săn sóc) nhằm mục đích gì?", Options = new() { "Duy trì và chuẩn hóa việc thực hiện 3S đầu tiên liên tục", "Tặng quà cho công nhân xuất sắc", "Mua sắm máy mới", "Lập báo cáo hàng tháng" }, CorrectOptionIndex = 0 },
            new() { Text = "Chữ S thứ năm 'Shitsuke' (Sẵn sàng) có nghĩa là gì?", Options = new() { "Chuẩn bị sẵn vật tư dự phòng", "Tự giác rèn luyện thói quen tuân thủ các quy chuẩn an toàn, vệ sinh", "Bật nguồn máy trước 10 phút", "Sẵn sàng làm tăng ca" }, CorrectOptionIndex = 1 },
            new() { Text = "Lợi ích lớn nhất của việc áp dụng tiêu chuẩn 5S tại phân xưởng sản xuất là gì?", Options = new() { "Để xưởng trông đẹp mắt hơn", "Tăng năng suất, giảm thiểu lãng phí thời gian tìm kiếm và tăng tính an toàn", "Giảm lương công nhân", "Tăng số lượng phế liệu thu hồi" }, CorrectOptionIndex = 1 },
            new() { Text = "Ai chịu trách nhiệm thực hiện 5S tại khu vực làm việc cá nhân?", Options = new() { "Trưởng phòng Kỹ thuật", "Chính nhân viên làm việc tại vị trí đó", "Đội vệ sinh chuyên trách", "Tổ trưởng sản xuất" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi phát hiện một công cụ không dùng đến để bừa bãi trên bàn điều khiển máy, bước 5S nào bị vi phạm?", Options = new() { "Chỉ vi phạm Seiketsu", "Vi phạm cả Seiri (Sàng lọc) và Seiton (Sắp xếp)", "Chỉ vi phạm Shitsuke", "Không vi phạm bước nào" }, CorrectOptionIndex = 1 },
            new() { Text = "Tần suất thực hiện vệ sinh máy móc (Seiso) nên là bao lâu?", Options = new() { "Hàng tuần", "Hàng tháng", "Hàng ngày trước và sau mỗi ca làm việc", "Chỉ làm khi máy bị hỏng" }, CorrectOptionIndex = 2 }
        };
        await SeedExamQuestions(context, examPro1.ExamId, catSafety, proCh1Questions, 10m);

        // --- PRO102 Chương 2: 10 câu ---
        var proCh2Questions = new List<SeedQuestion>
        {
            new() { Text = "Trước khi ấn nút khởi động máy, công nhân phải kiểm tra yếu tố nào?", Options = new() { "Xem máy có sạch bụi không", "Ngoại quan máy, dây cáp điện và tấm chắn bảo vệ", "Nhiệt độ ngoài trời", "Hỏi xem ca trước máy chạy thế nào" }, CorrectOptionIndex = 1 },
            new() { Text = "Nút dừng khẩn cấp (Emergency Stop) thường có đặc điểm gì nổi bật?", Options = new() { "Nút gạt màu xanh nhỏ", "Hình nấm màu đỏ nổi bật trên nền vàng", "Công tắc xoay màu đen", "Nút bấm phẳng chìm màu trắng" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi máy móc đang hoạt động xảy ra tiếng kêu rít lạ hoặc bốc khói, bạn cần làm gì?", Options = new() { "Lập tức ấn nút dừng khẩn cấp và báo cho tổ kỹ thuật bảo trì", "Cứ chạy tiếp cho hết mẻ hàng", "Mở nắp máy ra xem ngay", "Đi tìm xô nước dội vào" }, CorrectOptionIndex = 0 },
            new() { Text = "Việc bảo trì máy cấp 1 (kiểm tra dầu mỡ, vệ sinh nhẹ) do ai thực hiện?", Options = new() { "Đội bảo trì chuyên trách", "Công nhân vận hành trực tiếp máy đó", "Trưởng phòng Kỹ thuật", "Nhân sự ngoài nhà máy" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi máy đang chạy bị kẹt sản phẩm, hành động nào là đúng an toàn?", Options = new() { "Dùng tay thò vào gỡ nhanh sản phẩm ra", "Tắt máy, ngắt điện hoàn toàn rồi mới tiến hành gỡ kẹt", "Dùng thanh sắt gạt khi máy vẫn chạy", "Nhấn nút tăng tốc lực để máy đẩy qua" }, CorrectOptionIndex = 1 },
            new() { Text = "Việc tự ý thay đổi kết cấu hoặc tháo bỏ tấm chắn bảo vệ máy bị coi là gì?", Options = new() { "Hành động sáng tạo cải tiến kỹ thuật", "Hành vi vi phạm nội quy an toàn lao động nghiêm trọng", "Tiết kiệm thời gian vận hành", "Không có vấn đề gì nếu chạy cẩn thận" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi rời khỏi vị trí máy vận hành trong thời gian nghỉ giữa ca, bạn cần làm gì?", Options = new() { "Cứ để máy chạy tự động", "Tắt máy và ngắt nguồn điện cấp cho máy", "Hờ hờ nút dừng máy", "Nhờ công nhân máy bên cạnh trông hộ khi máy vẫn chạy" }, CorrectOptionIndex = 1 },
            new() { Text = "Tại sao phải ghi chép nhật ký vận hành máy hàng ngày?", Options = new() { "Để đối phó với bộ phận quản lý", "Để theo dõi hiệu suất, thời gian hoạt động và phát hiện sớm các dấu hiệu hỏng hóc", "Để tính lương tăng ca", "Để làm sạch tài liệu cũ" }, CorrectOptionIndex = 1 },
            new() { Text = "Chất bôi trơn máy (dầu, mỡ) bị rò rỉ tràn ra sàn xưởng cần xử lý thế nào?", Options = new() { "Cứ để đấy tự khô", "Lau sạch ngay lập tức và dùng cát/chất thấm để tránh trơn trượt ngã", "Đợi đến cuối tuần dọn một thể", "Rải bao tải gai đè lên" }, CorrectOptionIndex = 1 },
            new() { Text = "Tiêu chuẩn bàn giao máy giữa hai ca làm việc yêu cầu điều gì?", Options = new() { "Máy sạch sẽ, vận hành tốt và báo cáo đầy đủ tình trạng cho ca sau", "Ca trước chỉ cần về đúng giờ, không cần dọn máy", "Chỉ cần tắt nguồn máy", "Tự khóa máy lại mang chìa khóa về" }, CorrectOptionIndex = 0 }
        };
        await SeedExamQuestions(context, examPro2.ExamId, catSafety, proCh2Questions, 10m);

        // --- ITSEC101 Chương 1: 10 câu ---
        var itSecCh1Questions = new List<SeedQuestion>
        {
            new() { Text = "Một mật khẩu được coi là mạnh khi đáp ứng tiêu chí tối thiểu nào?", Options = new() { "Là ngày sinh hoặc số điện thoại của bạn", "Dài từ 8 ký tự trở lên, gồm chữ hoa, chữ thường, số và ký tự đặc biệt", "Chỉ gồm chữ thường để dễ nhớ", "Là tên viết tắt của công ty" }, CorrectOptionIndex = 1 },
            new() { Text = "Tại sao không nên dùng chung một mật khẩu cho nhiều tài khoản khác nhau?", Options = new() { "Vì hệ thống sẽ không cho phép thiết lập", "Nếu một tài khoản bị lộ, các tài khoản khác cũng sẽ dễ dàng bị xâm nhập", "Làm chậm tốc độ truy cập internet", "Kế toán không tính được chi phí" }, CorrectOptionIndex = 1 },
            new() { Text = "Xác thực đa yếu tố (MFA / 2FA) hoạt động dựa trên nguyên tắc nào?", Options = new() { "Yêu cầu đổi mật khẩu 2 lần", "Yêu cầu mật khẩu kết hợp thêm mã gửi về điện thoại hoặc ứng dụng xác thực", "Quét dấu vân tay cả hai bàn tay", "Bắt buộc đăng nhập từ hai thiết bị cùng lúc" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi trình duyệt hỏi có muốn lưu mật khẩu trên máy tính công cộng không, bạn chọn gì?", Options = new() { "Chọn Đồng ý để lần sau vào nhanh hơn", "Chọn Không lưu để tránh bị lộ tài khoản", "Chọn Lưu tạm thời", "Tắt trình duyệt luôn" }, CorrectOptionIndex = 1 },
            new() { Text = "Tần suất khuyến nghị để nhân viên chủ động thay đổi mật khẩu hệ thống là bao lâu?", Options = new() { "Định kỳ mỗi 3 đến 6 tháng một lần", "Hàng tuần", "Chỉ khi nào bị hack mới đổi", "5 năm một lần" }, CorrectOptionIndex = 0 },
            new() { Text = "Hành vi viết mật khẩu ra giấy note dán lên màn hình máy tính làm việc bị đánh giá như thế nào?", Options = new() { "Là sự cẩn thận đáng khen ngợi", "Hành vi để lộ thông tin nghiêm trọng, tạo sơ hở cho kẻ xấu", "Giúp IT hỗ trợ máy nhanh hơn", "Không ảnh hưởng vì có camera giám sát" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi bạn rời khỏi bàn làm việc đi họp hoặc đi vệ sinh, phím tắt nào giúp khóa nhanh màn hình Windows?", Options = new() { "Ctrl + Alt + Del", "Windows + L", "Alt + F4", "Ctrl + Shift + Esc" }, CorrectOptionIndex = 1 },
            new() { Text = "Phần mềm độc hại tự động mã hóa toàn bộ dữ liệu máy tính của nạn nhân rồi đòi tiền chuộc gọi là gì?", Options = new() { "Trojan Horse", "Ransomware (Mã độc tống tiền)", "Spyware", "Adware" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi nhận được yêu cầu cung cấp mật khẩu qua điện thoại từ một người tự xưng là IT công ty, bạn ứng xử thế nào?", Options = new() { "Lập tức đọc mật khẩu để hỗ trợ công việc", "Từ chối cung cấp và báo cáo vì IT chính thống không bao giờ hỏi mật khẩu cá nhân của bạn", "Gửi tin nhắn mật khẩu qua Zalo", "Bảo người đó tự vào hệ thống lấy" }, CorrectOptionIndex = 1 },
            new() { Text = "Việc kết nối máy tính công ty vào mạng Wi-Fi công cộng không có mật khẩu (như tại quán cafe) ẩn chứa nguy hiểm gì?", Options = new() { "Tốn pin máy tính hơn", "Bị kẻ xấu trên cùng mạng chặn thu dữ liệu và đánh cắp thông tin truyền tải", "Làm hỏng card mạng của máy", "Bị giới hạn dung lượng download" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examItSec1.ExamId, catTech, itSecCh1Questions, 10m);

        // --- ITSEC101 Chương 2: 10 câu ---
        var itSecCh2Questions = new List<SeedQuestion>
        {
            new() { Text = "Tấn công giả mạo (Phishing) thường được tin tặc thực hiện qua kênh nào phổ biến nhất?", Options = new() { "Gọi điện thoại trực tiếp", "Email giả mạo có liên kết độc hại hoặc tệp đính kèm chứa mã độc", "Gửi thư tay đến văn phòng", "Nhắn tin SMS thương hiệu" }, CorrectOptionIndex = 1 },
            new() { Text = "Dấu hiệu nào nghi ngờ một email là email lừa đảo giả mạo (Phishing)?", Options = new() { "Tên miền người gửi khác lạ, có lỗi chính tả, văn phong hối thúc hoặc yêu cầu đăng nhập tài khoản đột ngột", "Email có định dạng đẹp mắt", "Email gửi vào ban đêm", "Email đính kèm file PDF hóa đơn thông thường" }, CorrectOptionIndex = 0 },
            new() { Text = "Khi nhận được email thông báo tài khoản của bạn bị khóa và yêu cầu nhấp vào liên kết để xác minh, bạn nên làm gì?", Options = new() { "Nhấp vào liên kết ngay để tài khoản không bị khóa", "Không nhấp liên kết, báo cáo cho bộ phận IT hoặc an ninh thông tin kiểm tra xác minh", "Gửi email đó cho đồng nghiệp cùng kiểm tra", "Xóa email đi và coi như không có chuyện gì" }, CorrectOptionIndex = 1 },
            new() { Text = "Làm thế nào để kiểm tra liên kết thực sự của một dòng chữ trong email trước khi click?", Options = new() { "Nhấp chuột phải chọn Properties", "Di con trỏ chuột lên liên kết (không click) để xem địa chỉ URL thực tế xuất hiện ở góc dưới màn hình", "Nhấp đúp chuột thật nhanh", "Không thể kiểm tra được" }, CorrectOptionIndex = 1 },
            new() { Text = "Nếu lỡ nhấp vào liên kết nghi vấn và điền mật khẩu tài khoản công ty, bạn cần làm gì đầu tiên?", Options = new() { "Thay đổi mật khẩu tài khoản đó ngay lập tức và báo cáo khẩn cấp cho IT", "Đợi xem có ai đăng nhập trái phép không", "Tắt máy tính đi ngủ", "Cài đặt lại hệ điều hành Windows" }, CorrectOptionIndex = 0 },
            new() { Text = "Tệp tin đính kèm email có phần mở rộng nào dưới đây có mức độ nguy hại cao nhất?", Options = new() { ".pdf hoặc .txt", ".docx hoặc .xlsx", ".exe, .scr, .bat hoặc các file nén chứa file chạy này", ".png hoặc .jpg" }, CorrectOptionIndex = 2 },
            new() { Text = "Tên miền email chính thức của Công ty Ba Sáu dùng để trao đổi nội bộ và đối tác là gì?", Options = new() { "@basau.com", "@basau.net", "@basau-company.com", "@basau-group.net" }, CorrectOptionIndex = 1 },
            new() { Text = "Email gửi từ địa chỉ 'admin@basau-hr.com' thông báo tăng lương có phải email chính chủ của công ty?", Options = new() { "Đúng, vì có chữ basau trong tên miền", "Không, vì sai tên miền chính thức basau.net, đây là email mạo danh", "Chắc chắn đúng vì nội dung về lương thưởng", "Chỉ đúng khi có đóng dấu đỏ scan kèm theo" }, CorrectOptionIndex = 1 },
            new() { Text = "Quy trình chuẩn khi phát hiện một email lừa đảo gửi vào hòm thư công ty của bạn là gì?", Options = new() { "Forward email đó cho toàn bộ phòng ban cảnh báo", "Báo cáo cho bộ phận IT bằng cách gửi dưới dạng đính kèm (Attachment) hoặc dùng nút Report Phishing", "Trả lời email mắng mỏ kẻ lừa đảo", "Báo cáo với Công an phường" }, CorrectOptionIndex = 1 },
            new() { Text = "Quy tắc vàng khi xử lý các dữ liệu thông tin mật của công ty là gì?", Options = new() { "Chia sẻ tự do trên nhóm chat cộng đồng", "Chỉ chia sẻ cho đúng người có thẩm quyền thông qua kênh chính thống và bảo mật", "Lưu trữ tự do trên máy tính cá nhân", "In ra để bừa bãi tại bàn tiếp khách" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examItSec2.ExamId, catTech, itSecCh2Questions, 10m);

        // --- SALES201 Chương 1: 10 câu ---
        var salesCh1Questions = new List<SeedQuestion>
        {
            new() { Text = "Bước đầu tiên trong quy trình tìm kiếm khách hàng tiềm năng là gì?", Options = new() { "Gọi điện telesales ngay lập tức", "Xác định rõ chân dung khách hàng mục tiêu (Buyer Persona)", "Gửi báo giá sản phẩm", "Soạn thảo hợp đồng mua bán" }, CorrectOptionIndex = 1 },
            new() { Text = "Kênh thông tin nào sau đây là nguồn tìm kiếm thông tin đối tác B2B uy tín nhất?", Options = new() { "Các mạng xã hội giải trí TikTok", "LinkedIn, cổng đăng ký doanh nghiệp quốc gia và các danh bạ ngành", "Các diễn đàn truyện tranh", "Hỏi người thân trong gia đình" }, CorrectOptionIndex = 1 },
            new() { Text = "Telesales trong hoạt động kinh doanh của doanh nghiệp được định nghĩa là gì?", Options = new() { "Bán hàng qua truyền hình", "Hoạt động giới thiệu, bán hàng và tiếp cận đối tác qua điện thoại", "Gửi tin nhắn quảng cáo tự động", "Chăm sóc khách hàng trực tiếp tại showroom" }, CorrectOptionIndex = 1 },
            new() { Text = "Trong cuộc gọi telesales tiếp cận đầu tiên, mục tiêu cốt lõi của nhân viên sales là gì?", Options = new() { "Ép khách hàng chuyển tiền đặt cọc ngay", "Thiết lập một cuộc hẹn gặp trực tiếp hoặc cuộc gọi tư vấn sâu hơn", "Đọc toàn bộ tính năng kỹ thuật của sản phẩm trong 10 phút", "Hỏi thăm sức khỏe gia đình khách hàng" }, CorrectOptionIndex = 1 },
            new() { Text = "Khoảng thời gian vàng để thực hiện cuộc gọi chào hàng đạt tỷ lệ bắt máy cao nhất là khi nào?", Options = new() { "Trước 7:00 sáng hoặc sau 9:00 tối", "Giờ nghỉ trưa từ 12:00 đến 1:30 chiều", "Từ 9:00 - 11:00 sáng hoặc 2:00 - 4:00 chiều trong giờ hành chính", "Bất kỳ lúc nào sales rảnh" }, CorrectOptionIndex = 2 },
            new() { Text = "Bộ tài liệu giới thiệu ngắn gọn về công ty và giải pháp sản phẩm gửi cho khách hàng gọi là gì?", Options = new() { "Phiếu chi chi phí", "Company Profile hoặc Sales Deck", "Hóa đơn VAT", "Nhật ký vận hành máy" }, CorrectOptionIndex = 1 },
            new() { Text = "Hệ thống phần mềm chuyên dụng dùng để lưu trữ và quản lý thông tin khách hàng là gì?", Options = new() { "ERP", "CRM (Customer Relationship Management)", "LMS", "CAD" }, CorrectOptionIndex = 1 },
            new() { Text = "Kỹ thuật lắng nghe chủ động (Active Listening) yêu cầu nhân viên bán hàng phải làm gì?", Options = new() { "Nói liên tục không cho khách hàng ngắt lời", "Tập trung lắng nghe, ghi chú thông tin, thể hiện sự đồng cảm và đặt câu hỏi gợi mở nhu cầu", "Vừa nghe điện thoại vừa lướt Facebook", "Đồng ý với tất cả yêu cầu giảm giá của khách hàng" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi khách hàng đồng ý nhận email tài liệu sản phẩm, bạn nên gửi trong thời gian bao lâu?", Options = new() { "Đợi đến cuối tuần gửi hàng loạt", "Trong vòng tối đa 2 giờ sau cuộc gọi khi khách hàng còn nhớ thông tin", "Sau 3 ngày để tạo độ khan hiếm", "Không cần gửi nếu khách hàng không giục" }, CorrectOptionIndex = 1 },
            new() { Text = "Chỉ số KPI quan trọng nhất đánh giá hiệu suất của hoạt động Telesales hàng ngày là gì?", Options = new() { "Số phút đàm thoại", "Số lượng cuộc gọi kết nối thành công và số lượng cơ hội hẹn gặp được thiết lập", "Số trang tài liệu đã in", "Thời gian đi làm đúng giờ" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examSales1.ExamId, catLeader, salesCh1Questions, 10m);

        // --- SALES201 Chương 2: 10 câu ---
        var salesCh2Questions = new List<SeedQuestion>
        {
            new() { Text = "Khi khách hàng phản hồi 'Giá sản phẩm của bên bạn quá đắt', sales nên ứng xử thế nào?", Options = new() { "Lập tức giảm giá 50% để giữ khách", "Lắng nghe, tỏ thái độ đồng cảm, hỏi rõ lý do so sánh rồi phân tích giá trị vượt trội sản phẩm mang lại", "Nói rằng tiền nào của nấy và tỏ ý chê khách hàng không có tiền", "Im lặng và cúp máy" }, CorrectOptionIndex = 1 },
            new() { Text = "Kỹ thuật 'Chốt sales giả định' (Assumptive Close) là gì?", Options = new() { "Giả vờ như chưa chốt được", "Mặc định khách hàng đã đồng ý mua và chuyển sang hỏi về phương thức giao hàng hoặc thông tin xuất hóa đơn", "Bắt khách hàng ký cam kết trước", "Hỏi xem khách hàng có cần cân nhắc thêm không" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi khách hàng từ chối mua vì 'Hiện tại bên anh chưa có nhu cầu', hành động tiếp theo của sales là gì?", Options = new() { "Xóa thông tin khách hàng khỏi CRM", "Xin phép giữ liên lạc, gửi thông tin bản tin hữu ích định kỳ để duy trì mối quan hệ và đón đầu cơ hội tương lai", "Nài nỉ khách hàng mua ủng hộ", "Tỏ thái độ khó chịu" }, CorrectOptionIndex = 1 },
            new() { Text = "Phương pháp 'Cô lập lời từ chối' (Isolating Objections) nhằm mục đích gì?", Options = new() { "Bắt bẻ khách hàng", "Xác định xem lý do từ chối đó có phải là rào cản thực sự duy nhất hay còn lý do nào khác", "Chia rẽ các thành viên trong ban mua hàng", "Hạn chế khách hàng đặt câu hỏi" }, CorrectOptionIndex = 1 },
            new() { Text = "Kỹ thuật đưa ra hai lựa chọn cụ thể (Ví dụ: 'Bên em giao hàng vào thứ 2 hay thứ 5?') thuộc phương pháp chốt sales nào?", Options = new() { "Chốt sales khan hiếm", "Chốt lựa chọn kép (Alternative Close)", "Chốt thử nghiệm", "Chốt giảm giá trực tiếp" }, CorrectOptionIndex = 1 },
            new() { Text = "Kỹ năng thương lượng về giá cả yêu cầu người bán hàng làm gì?", Options = new() { "Tập trung giải thích giá trị lợi ích của sản phẩm mang lại thay vì chỉ giảm giá bán trực tiếp", "Nhất quyết không bớt một đồng nào", "Đồng ý giảm giá ngay khi khách hàng yêu cầu", "Đổ lỗi cho phòng kế toán quy định" }, CorrectOptionIndex = 0 },
            new() { Text = "Khi khách hàng đồng ý chốt đơn, bước tiếp theo ngay lập tức của sales là gì?", Options = new() { "Đi ăn mừng và tạm dừng chăm sóc", "Gửi email xác nhận đơn hàng, soạn hợp đồng chính thức và phối hợp bộ phận kế toán/kho để thực hiện", "Hỏi khách hàng có muốn mua thêm sản phẩm khác không ngay lập tức", "Đợi khách hàng chủ động chuyển tiền" }, CorrectOptionIndex = 1 },
            new() { Text = "Việc thực hiện hoạt động chăm sóc khách hàng sau bán hàng (After-sales service) nhằm mục đích gì?", Options = new() { "Để đòi nợ tiền hàng", "Đảm bảo khách hàng hài lòng, giải quyết khiếu nại kịp thời nhằm tăng tỷ lệ tái mua và giới thiệu khách hàng mới", "Thực hiện đúng thủ tục cho xong việc", "Hỏi thăm đời tư của khách hàng" }, CorrectOptionIndex = 1 },
            new() { Text = "Khi khách hàng gọi điện phàn nàn gay gắt về lỗi sản phẩm, thái độ đúng đắn nhất của sales là gì?", Options = new() { "Đổ lỗi cho bộ phận sản xuất hoặc khâu vận chuyển", "Lắng nghe chân thành, xin lỗi vì sự bất tiện, nhận trách nhiệm và phối hợp kỹ thuật xử lý khắc phục nhanh nhất", "Tranh cãi với khách hàng xem lỗi do ai", "Tránh mặt không nghe điện thoại" }, CorrectOptionIndex = 1 },
            new() { Text = "Chỉ số tỷ lệ chốt sales thành công (Win Rate) được tính toán như thế nào?", Options = new() { "Tổng số khách hàng gọi điện", "Số cơ hội bán hàng ký kết hợp đồng thành công chia cho tổng số cơ hội bán hàng nhận được trong kỳ", "Tổng doanh số bán hàng trong tháng", "Số lượng nhân viên sales trong phòng" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examSales2.ExamId, catLeader, salesCh2Questions, 10m);

        // --- FIN201 Chương 1: 10 câu ---
        var finCh1Questions = new List<SeedQuestion>
        {
            new() { Text = "Loại hóa đơn/chứng từ mua vào nào bắt buộc phải có để phòng kế toán duyệt chi thanh toán hợp lệ?", Options = new() { "Giấy biên nhận viết tay không có dấu", "Hóa đơn giá trị gia tăng (GTGT) - hóa đơn điện tử hợp pháp được xuất từ bên bán", "Ảnh chụp sản phẩm mua về", "Phiếu đề xuất mua sắm của phòng ban" }, CorrectOptionIndex = 1 },
            new() { Text = "Thông tin mã số thuế của Công ty Ba Sáu ghi nhận trên hóa đơn GTGT mua vào phải đáp ứng yêu cầu gì?", Options = new() { "Không cần ghi rõ", "Phải chính xác tuyệt đối theo giấy chứng nhận đăng ký doanh nghiệp của công ty", "Chỉ cần ghi đúng tên công ty, mã số thuế ghi sai cũng được", "Có thể dùng mã số thuế cá nhân của người mua hàng" }, CorrectOptionIndex = 1 },
            new() { Text = "Theo quy định pháp luật thuế hiện hành, hóa đơn mua hàng có giá trị thanh toán từ bao nhiêu tiền trở lên bắt buộc phải chuyển khoản qua ngân hàng?", Options = new() { "Từ 5 triệu đồng trở lên", "Từ 20 triệu đồng trở lên (đã bao gồm VAT)", "Từ 50 triệu đồng trở lên", "Không giới hạn số tiền thanh toán bằng tiền mặt" }, CorrectOptionIndex = 1 },
            new() { Text = "Tài liệu nào chứng minh việc giao nhận hàng hóa thực tế giữa nhà cung cấp và công ty đã hoàn tất?", Options = new() { "Hợp đồng mua bán ký trước đó", "Biên bản bàn giao hàng hóa có đầy đủ chữ ký của đại diện bên giao và bên nhận", "Email trao đổi thỏa thuận", "Báo giá sản phẩm" }, CorrectOptionIndex = 1 },
            new() { Text = "Hóa đơn điện tử hợp pháp mua vào được kiểm tra và xác thực tính pháp lý trên trang thông tin điện tử của cơ quan nào?", Options = new() { "Trang thông tin của Bộ Công thương", "Hệ thống hóa đơn điện tử của Tổng cục Thuế", "Cổng dịch vụ công quốc gia", "Trang chủ của doanh nghiệp bán hàng" }, CorrectOptionIndex = 1 },
            new() { Text = "Chứng từ thanh toán bằng tiền mặt trực tiếp tại quỹ của công ty do thủ quỹ ký chi tiền gọi là gì?", Options = new() { "Phiếu thu", "Phiếu chi", "Phiếu nhập kho", "Ủy nhiệm chi" }, CorrectOptionIndex = 1 },
            new() { Text = "Thời hạn chuẩn để nhân viên nộp đầy đủ hồ sơ đề nghị thanh toán chi phí phát sinh trong tháng về phòng kế toán là khi nào?", Options = new() { "Đến cuối năm nộp một thể", "Trước ngày 5 của tháng tiếp theo để kịp hạch toán và báo cáo thuế", "Bất kỳ lúc nào rảnh", "Sau 3 tháng kể từ ngày phát sinh" }, CorrectOptionIndex = 1 },
            new() { Text = "Một bộ hồ sơ thanh toán mua sắm tài sản cố định chuẩn gửi phòng kế toán gồm những tài liệu gì?", Options = new() { "Chỉ cần hóa đơn GTGT", "Tờ trình mua sắm được duyệt, báo giá so sánh, hợp đồng mua bán, hóa đơn GTGT, biên bản bàn giao và nghiệm thu", "Ảnh chụp tài sản", "Biên bản họp của Ban giám đốc" }, CorrectOptionIndex = 1 },
            new() { Text = "Ai là người có thẩm quyền ký phê duyệt cuối cùng cho các hồ sơ đề xuất chi tiền tại công ty?", Options = new() { "Nhân viên kế toán thanh toán", "Giám đốc Điều hành (CEO) hoặc người được ủy quyền hợp pháp bằng văn bản", "Trưởng phòng Nhân sự", "Thủ quỹ công ty" }, CorrectOptionIndex = 1 },
            new() { Text = "Tại sao phòng kế toán sẽ từ chối thanh toán đối với những hóa đơn GTGT bị tẩy xóa, rách nát hoặc mờ thông tin?", Options = new() { "Vì kế toán không thích đọc hóa đơn xấu", "Vì hóa đơn không đủ điều kiện pháp lý để khấu trừ thuế đầu vào và hạch toán chi phí hợp lệ của doanh nghiệp", "Vì sợ tốn diện tích lưu trữ", "Vì hóa đơn đó không có màu đẹp" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examFin1.ExamId, catFinance, finCh1Questions, 10m);

        // --- FIN201 Chương 2: 10 câu ---
        var finCh2Questions = new List<SeedQuestion>
        {
            new() { Text = "Mục đích chính của quy trình tạm ứng tiền mặt nội bộ tại doanh nghiệp là gì?", Options = new() { "Để cho nhân viên vay tiền tiêu dùng cá nhân", "Cấp trước một khoản kinh phí định mức phục vụ công việc chung hoặc công tác phí trước khi thực hiện", "Để giảm số tiền trong quỹ công ty", "Để tăng lương cho nhân viên" }, CorrectOptionIndex = 1 },
            new() { Text = "Phiếu đề xuất tạm ứng chi phí đi công tác phải được phê duyệt bước đầu bởi ai trước khi chuyển cho phòng kế toán?", Options = new() { "Trưởng phòng Kế toán", "Trưởng bộ phận trực tiếp quản lý của nhân viên đề xuất", "Nhân viên thủ quỹ", "Đồng nghiệp cùng phòng" }, CorrectOptionIndex = 1 },
            new() { Text = "Thời hạn tối đa để nhân viên hoàn thành thủ tục hoàn ứng (thanh quyết toán) sau khi kết thúc đợt công tác là bao lâu?", Options = new() { "Trong vòng 30 ngày làm việc", "Trong vòng 5 ngày làm việc kể từ ngày trở lại công ty làm việc", "Không giới hạn thời gian", "Trước kỳ quyết toán thuế cuối năm" }, CorrectOptionIndex = 1 },
            new() { Text = "Trường hợp số tiền thực chi (được duyệt chi) nhỏ hơn số tiền nhân viên đã nhận tạm ứng thì xử lý thế nào?", Options = new() { "Nhân viên được giữ lại tiêu dùng cá nhân", "Nhân viên phải nộp trả lại số tiền thừa vào quỹ công ty hoặc chuyển khoản hoàn trả ngay khi làm thủ tục hoàn ứng", "Trừ vào lương tháng sau", "Kế toán tự hạch toán xóa nợ" }, CorrectOptionIndex = 1 },
            new() { Text = "Trường hợp số tiền thực chi hợp lệ lớn hơn số tiền đã tạm ứng, phần chi trội sẽ được giải quyết thế nào?", Options = new() { "Nhân viên tự chịu chi phí đó", "Công ty sẽ chi bổ sung phần chênh lệch cho nhân viên sau khi hồ sơ hoàn ứng được phê duyệt", "Kế toán ghi nợ cho đợt công tác sau", "Trừ vào ngân sách phòng ban" }, CorrectOptionIndex = 1 },
            new() { Text = "Bộ hồ sơ thanh toán hoàn ứng đi công tác bắt buộc phải đính kèm chứng từ cốt lõi nào dưới đây?", Options = new() { "Chỉ cần ghi chép viết tay lịch trình", "Quyết định cử đi công tác, vé xe/vé máy bay, hóa đơn tiền phòng và hóa đơn ăn uống tiếp khách hợp lệ", "Ảnh lưu niệm chuyến đi", "Báo cáo tiến độ bán hàng" }, CorrectOptionIndex = 1 },
            new() { Text = "Kế toán thanh toán có quyền từ chối duyệt tạm ứng mới cho nhân viên trong trường hợp nào?", Options = new() { "Nhân viên đi muộn", "Nhân viên vẫn chưa hoàn tất thủ tục thanh quyết toán khoản tạm ứng quá hạn trước đó", "Phòng kế toán đang bận", "Nhân viên chưa đạt KPI doanh số" }, CorrectOptionIndex = 1 },
            new() { Text = "Tiền tạm ứng nhận về để mua sắm vật tư sửa chữa máy móc xưởng có được sử dụng chi tiêu cá nhân không?", Options = new() { "Được phép dùng nếu hứa trả lại", "Tuyệt đối không, tự ý sử dụng vào mục đích cá nhân bị coi là vi phạm nghiêm trọng quy chế tài chính và có thể bị kỷ luật sa thải", "Được dùng dưới 1 triệu đồng", "Chỉ được dùng khi đi công tác xa" }, CorrectOptionIndex = 1 },
            new() { Text = "Hạn mức chi tạm ứng bằng tiền mặt tối đa cho một lần đề nghị mua sắm khẩn cấp thông thường là bao nhiêu?", Options = new() { "Không giới hạn hạn mức", "Dưới 5 triệu đồng theo quy chế quản lý tiền mặt nội bộ (trừ trường hợp đặc biệt được CEO duyệt)", "Trên 100 triệu đồng", "Chỉ được tối đa 500 nghìn đồng" }, CorrectOptionIndex = 1 },
            new() { Text = "Phiếu đề nghị tạm ứng hợp lệ gửi kế toán cần có đầy đủ chữ ký phê duyệt của những ai?", Options = new() { "Chỉ cần chữ ký người đề nghị", "Người đề nghị, Trưởng bộ phận quản lý trực tiếp, Kế toán trưởng và Giám đốc duyệt", "Người đề nghị và Thủ quỹ", "Người đề nghị và IT Support" }, CorrectOptionIndex = 1 }
        };
        await SeedExamQuestions(context, examFin2.ExamId, catFinance, finCh2Questions, 10m);

        // 16. Tạo một số Phân công đào tạo và Tiến trình học tập mẫu
        var workerCuong = userList.First(u => u.EmployeeCode == "CN0001");
        var managerTech = userList.First(u => u.EmployeeCode == "KT0001");

        // Giao khóa HSE cho công nhân Cường
        var assign1 = new TrainingAssignment
        {
            UserId = workerCuong.UserId,
            CourseId = courses[0].CourseId,
            AssignedBy = managerTech.UserId,
            AssignedDate = DateTime.Now.AddDays(-5),
            DueDate = DateTime.Now.AddDays(10),
            Priority = "High"
        };
        context.TrainingAssignments.Add(assign1);

        // Gieo tiến trình học tập mẫu cho Cường (Đang học và đạt 33% tiến độ - tương đương học xong chương 1)
        var enroll1 = new Enrollment
        {
            UserId = workerCuong.UserId,
            CourseId = courses[0].CourseId,
            EnrollDate = DateTime.Now.AddDays(-5),
            ProgressPercent = 33,
            Status = "InProgress"
        };
        context.Enrollments.Add(enroll1);

        // Ghi nhận lịch sử làm bài thi Chương 1 của Cường (Đạt 100 điểm)
        var userExam1 = new UserExam
        {
            UserId = workerCuong.UserId,
            ExamId = examHse1.ExamId,
            Score = 100m,
            IsFinish = true,
            EndTime = DateTime.Now.AddDays(-4),
            StartTime = DateTime.Now.AddDays(-4).AddMinutes(-10)
        };
        context.UserExams.Add(userExam1);

        // Đánh dấu Cường đã hoàn thành 2 bài học của Chương 1
        context.UserLessonLogs.AddRange(
            new() { UserId = workerCuong.UserId, LessonId = lessons[0].LessonId, Status = "Completed" },
            new() { UserId = workerCuong.UserId, LessonId = lessons[1].LessonId, Status = "Completed" }
        );

        // 17. Gieo Lộ trình học mẫu (Learning Paths)
        var pathTech = new LearningPath
        {
            PathName = "Lộ trình Kỹ thuật & Sản xuất Cơ bản",
            Description = "Lộ trình đào tạo cơ bản bắt buộc dành riêng cho nhân sự phòng Kỹ thuật & Sản xuất bao gồm các khóa học về An toàn lao động, Quy trình 5S và vận hành máy móc.",
            CreatedByDeptId = workerCuong.DepartmentId
        };
        var pathSecurity = new LearningPath
        {
            PathName = "Lộ trình An toàn thông tin & Kỹ năng văn phòng",
            Description = "Lộ trình nâng cao nhận thức bảo mật hệ thống và quy chế làm việc nội bộ.",
            CreatedByDeptId = workerCuong.DepartmentId
        };
        context.LearningPaths.AddRange(pathTech, pathSecurity);
        await context.SaveChangesAsync();

        // Gán Khóa học vào Lộ trình (PathCourses)
        context.PathCourses.AddRange(
            new PathCourse { PathId = pathTech.PathId, CourseId = courses[0].CourseId, StepOrder = 1 },
            new PathCourse { PathId = pathTech.PathId, CourseId = courses[1].CourseId, StepOrder = 2 },
            new PathCourse { PathId = pathSecurity.PathId, CourseId = courses[2].CourseId, StepOrder = 1 },
            new PathCourse { PathId = pathSecurity.PathId, CourseId = courses[4].CourseId, StepOrder = 2 }
        );

        // Gieo tiến độ Lộ trình cho Cường (Đang học lộ trình Kỹ thuật đạt 50%)
        var pathProgress = new UserPathProgress
        {
            UserId = workerCuong.UserId,
            PathId = pathTech.PathId,
            Status = "InProgress",
            PercentComplete = 50
        };
        context.UserPathProgresses.Add(pathProgress);

        // 18. Gieo Lịch học tập offline mẫu (OfflineTrainingEvents)
        var offlineEvent1 = new OfflineTrainingEvent
        {
            CourseId = courses[0].CourseId,
            Title = "Huấn luyện thực hành Phòng cháy chữa cháy & Cứu hộ cứu nạn 2026",
            Location = "Sân trước Nhà máy A",
            Instructor = "Phạm Văn Máy",
            StartTime = DateTime.Now.AddDays(2).Date.AddHours(9), // 9:00 AM 2 ngày tới
            EndTime = DateTime.Now.AddDays(2).Date.AddHours(11), // 11:00 AM
            DepartmentId = workerCuong.DepartmentId,
            Shift = "Ca Sáng",
            Session = "Offline",
            Status = "Active",
            Notes = "Yêu cầu mặc trang phục bảo hộ đầy đủ.",
            CreatedAt = DateTime.Now,
            CreatedBy = managerTech.UserId,
            AttendanceStartTime = DateTime.Now.AddDays(2).Date.AddHours(8).AddMinutes(30),
            AttendanceEndTime = DateTime.Now.AddDays(2).Date.AddHours(9).AddMinutes(30)
        };
        
        var offlineEvent2 = new OfflineTrainingEvent
        {
            CourseId = courses[1].CourseId,
            Title = "Đánh giá thực hành Quy trình 5S tại dây chuyền sản xuất",
            Location = "Xưởng sản xuất số 2",
            Instructor = "Phạm Văn Máy",
            StartTime = DateTime.Now.AddDays(4).Date.AddHours(14), // 2:00 PM 4 ngày tới
            EndTime = DateTime.Now.AddDays(4).Date.AddHours(16), // 4:00 PM
            DepartmentId = workerCuong.DepartmentId,
            Shift = "Ca Chiều",
            Session = "Offline",
            Status = "Active",
            Notes = "Đánh giá xếp loại 5S định kỳ.",
            CreatedAt = DateTime.Now,
            CreatedBy = managerTech.UserId,
            AttendanceStartTime = DateTime.Now.AddDays(4).Date.AddHours(13).AddMinutes(30),
            AttendanceEndTime = DateTime.Now.AddDays(4).Date.AddHours(14).AddMinutes(30)
        };
        context.OfflineTrainingEvents.AddRange(offlineEvent1, offlineEvent2);

        // 19. Gieo FAQs mẫu
        var faqs = new List<Faq>
        {
            new()
            {
                Question = "Làm thế nào để đổi mật khẩu cá nhân?",
                Answer = "Bạn có thể đổi mật khẩu bằng cách truy cập vào trang cá nhân (Profile) từ góc trên bên phải, kéo xuống phần 'Đổi mật khẩu', nhập mật khẩu cũ, mật khẩu mới và nhấn 'Lưu'.",
                CategoryId = categories.First(c => c.CategoryName == "Kỹ năng Mềm & Phát triển Bản thân").CategoryId
            },
            new()
            {
                Question = "Tôi phải làm gì nếu gặp sự cố khi đang học bài giảng video?",
                Answer = "Hãy kiểm tra lại kết nối mạng của bạn. Nếu video vẫn không tải được, vui lòng liên hệ bộ phận IT Support qua hotline nội bộ hoặc tạo yêu cầu hỗ trợ trực tiếp trên hệ thống.",
                CategoryId = categories.First(c => c.CategoryName == "Công nghệ & Bảo mật").CategoryId
            },
            new()
            {
                Question = "Thời hạn tối đa để nộp hồ sơ thanh toán tạm ứng là bao lâu?",
                Answer = "Theo quy trình tài chính, nhân viên phải hoàn tất thủ tục hoàn ứng và nộp đầy đủ hóa đơn chứng từ hợp lệ trong vòng tối đa 5 ngày làm việc kể từ ngày kết thúc công tác.",
                CategoryId = categories.First(c => c.CategoryName == "Tài chính & Quy trình").CategoryId
            },
            new()
            {
                Question = "Làm sao để đăng ký tham gia các lớp học Offline?",
                Answer = "Bạn hãy truy cập vào mục 'Đào tạo offline' trên thanh menu bên trái, tìm kiếm lớp học phù hợp và nhấn vào nút 'Đăng ký'. Hệ thống sẽ tự động gửi email xác nhận và thông báo lịch học đến bạn.",
                CategoryId = categories.First(c => c.CategoryName == "Kỹ năng Mềm & Phát triển Bản thân").CategoryId
            }
        };
        context.Faqs.AddRange(faqs);

        await context.SaveChangesAsync();
    }

    private static async Task SeedExamQuestions(CorporateLmsProContext context, int examId, int categoryId, List<SeedQuestion> questions, decimal pointsPerQuestion)
    {
        foreach (var q in questions)
        {
            var qb = new QuestionBank
            {
                CategoryId = categoryId,
                QuestionText = q.Text,
                Difficulty = q.Difficulty
            };
            context.QuestionBanks.Add(qb);
            await context.SaveChangesAsync(); // Lưu để sinh QuestionId tự động

            for (int i = 0; i < q.Options.Count; i++)
            {
                var opt = new QuestionOption
                {
                    QuestionId = qb.QuestionId,
                    OptionText = q.Options[i],
                    IsCorrect = (i == q.CorrectOptionIndex)
                };
                context.QuestionOptions.Add(opt);
            }
            await context.SaveChangesAsync();

            var eq = new ExamQuestion
            {
                ExamId = examId,
                QuestionId = qb.QuestionId,
                Points = pointsPerQuestion
            };
            context.ExamQuestions.Add(eq);
        }
        await context.SaveChangesAsync();
    }

    private static async Task ClearDatabaseAsync(CorporateLmsProContext context)
    {
        var sql = @"
            -- Disable all constraints
            DECLARE @disableSql NVARCHAR(MAX) = N'';
            SELECT @disableSql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ALL; '
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id;
            EXEC sp_executesql @disableSql;

            -- Delete all data from all tables dynamically
            DECLARE @deleteSql NVARCHAR(MAX) = N'';
            SELECT @deleteSql += N'DELETE FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N'; '
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name NOT LIKE '%Migration%';
            EXEC sp_executesql @deleteSql;

            -- Re-enable all constraints
            DECLARE @enableSql NVARCHAR(MAX) = N'';
            SELECT @enableSql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' WITH CHECK CHECK CONSTRAINT ALL; '
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id;
            EXEC sp_executesql @enableSql;
        ";
        try
        {
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClearDB] Error clearing database: {ex.Message}");
        }
    }

    public static void GenerateEmailAndUsername(string fullName, string employeeCode, out string username, out string email)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            username = "user" + Guid.NewGuid().ToString("N").Substring(0, 8);
            email = username + "@basau.net";
            return;
        }

        string unsignedName = RemoveSign4VietnameseString(fullName.Trim());
        var parts = unsignedName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            username = "user" + Guid.NewGuid().ToString("N").Substring(0, 8);
            email = username + "@basau.net";
            return;
        }

        string ten = parts[parts.Length - 1].ToLower();

        string hoDemChars = "";
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Length > 0)
            {
                hoDemChars += parts[i].Substring(0, 1).ToLower();
            }
        }

        string normalizedEmpCode = (employeeCode ?? "").Trim().ToLower();

        username = ten + hoDemChars + normalizedEmpCode;
        email = username + "@basau.net";
    }

    private static readonly string[] VietNameseSigns = new string[]
    {
        "aAeEoOuUiIdDyY",
        "áàạảãâấầậẩẫăắằặẳẵ",
        "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
        "éèẹẻẽêếềệểễ",
        "ÉÈẸẺẼÊẾỀỆỂỄ",
        "óòọỏõôốồộổỗơớờợởỡ",
        "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
        "úùụủũưứừựửữ",
        "ÚÙỤỦŨƯỨỪỰỬỮ",
        "íìịỉĩ",
        "ÍÌỊỈĨ",
        "đ",
        "Đ",
        "ýỳỵỷỹ",
        "ÝỲỴỶỸ"
    };

    public static string RemoveSign4VietnameseString(string str)
    {
        for (int i = 1; i < VietNameseSigns.Length; i++)
        {
            for (int j = 0; j < VietNameseSigns[i].Length; j++)
                str = str.Replace(VietNameseSigns[i][j], VietNameseSigns[0][i - 1]);
        }
        return str;
    }
}
