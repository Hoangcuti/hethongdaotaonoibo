using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KhoaHoc.Infrastructure;
using KhoaHoc.Models;

namespace KhoaHoc.Controllers;

public class ITController : Controller
{
    private sealed record PermissionCatalogItem(string Key, string Description, string Category, params string[] DefaultRoles);

    private readonly CorporateLmsProContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly KhoaHoc.Services.IAIService _aiService;
    private readonly ILogger<ITController> _logger;

    public ITController(CorporateLmsProContext db, IWebHostEnvironment env, KhoaHoc.Services.IAIService aiService, ILogger<ITController> logger)
    {
        _db = db;
        _env = env;
        _aiService = aiService;
        _logger = logger;
    }

    private IActionResult? RequireIT()
    {
        var role = HttpContext.Session.GetString("Role");
        if (HttpContext.Session.GetString("UserID") == null)
            return RedirectToAction("Login", "Auth");
        if (role != "IT")
            return RedirectToAction("Login", "Auth");
        return null;
    }

    private IActionResult? RequireITApi()
    {
        if (HttpContext.Session.GetString("UserID") == null)
            return Unauthorized(new { error = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });

        var role = HttpContext.Session.GetString("Role");
        // Cho phép IT và Manager (HR) truy cập các API quản lý nội dung
        if (role != "IT" && role != "Manager")
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Bạn không có quyền truy cập chức năng này." });

        return null;
    }

    private static int? NormalizeLevel(int? level)
    {
        if (!level.HasValue) return null;
        return level.Value is >= 1 and <= 3 ? level.Value : null;
    }

    private static int? ParseNullableInt(string? raw)
    {
        return int.TryParse(raw, out var value) ? value : null;
    }

    private sealed class LessonRequestData
    {
        public string Title { get; set; } = "";
        public string? ContentType { get; set; }
        public string? VideoUrl { get; set; }
        public bool HasVideoUrlField { get; set; }
        public string? ContentBody { get; set; }
        public bool HasContentBodyField { get; set; }
        public int? Level { get; set; }
        public int? SortOrder { get; set; }
        public IFormFile? VideoFile { get; set; }
        public IFormFile? DocumentFile { get; set; }
    }

    private async Task<LessonRequestData> ReadLessonRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new LessonRequestData
            {
                Title = form["title"].ToString(),
                ContentType = form.TryGetValue("contentType", out var contentType) ? contentType.ToString() : null,
                VideoUrl = form.TryGetValue("videoUrl", out var videoUrl) ? videoUrl.ToString() : null,
                HasVideoUrlField = form.ContainsKey("videoUrl"),
                ContentBody = form.TryGetValue("contentBody", out var contentBody) ? contentBody.ToString() : null,
                HasContentBodyField = form.ContainsKey("contentBody"),
                Level = ParseNullableInt(form["level"].ToString()),
                SortOrder = ParseNullableInt(form["sortOrder"].ToString()),
                VideoFile = form.Files.GetFile("videoFile"),
                DocumentFile = form.Files.GetFile("documentFile")
            };
        }

        var dto = await JsonSerializer.DeserializeAsync<ItCreateLessonDto>(Request.Body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ItCreateLessonDto();

        return new LessonRequestData
        {
            Title = dto.Title,
            ContentType = dto.ContentType,
            VideoUrl = dto.VideoUrl,
            HasVideoUrlField = dto.VideoUrl != null,
            ContentBody = dto.ContentBody,
            HasContentBodyField = dto.ContentBody != null,
            Level = dto.Level,
            SortOrder = dto.SortOrder
        };
    }

    private async Task<string> SaveLessonUploadAsync(int lessonId, IFormFile file, string subFolder)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "lessons", lessonId.ToString(), subFolder);
        Directory.CreateDirectory(uploadsRoot);

        var safeFileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(uploadsRoot, safeFileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);
        return $"/uploads/lessons/{lessonId}/{subFolder}/{safeFileName}";
    }

    private void DeleteUploadIfOwned(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !filePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return;

        var physicalPath = Path.Combine(_env.WebRootPath, filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }

    private async Task ApplyLessonAssetsAsync(Lesson lesson, LessonRequestData request, bool isUpdate)
    {
        var hasVideoFile = request.VideoFile != null && request.VideoFile.Length > 0;
        var hasDocumentFile = request.DocumentFile != null && request.DocumentFile.Length > 0;
        var hasVideoUrl = !string.IsNullOrWhiteSpace(request.VideoUrl);
        var hasContentBody = !string.IsNullOrWhiteSpace(request.ContentBody);

        // Priority 1: New Video File Upload
        if (hasVideoFile)
        {
            DeleteUploadIfOwned(lesson.VideoUrl);
            lesson.VideoUrl = await SaveLessonUploadAsync(lesson.LessonId, request.VideoFile!, "video");
            lesson.ContentType = "Video";
            lesson.ContentBody = null;
        }
        // Priority 2: New Document File Upload
        else if (hasDocumentFile)
        {
            DeleteUploadIfOwned(lesson.VideoUrl);
            lesson.VideoUrl = await SaveLessonUploadAsync(lesson.LessonId, request.DocumentFile!, "attachments");
            lesson.ContentType = "Document";
            lesson.ContentBody = null;
        }
        // Priority 3: Explicit clear URL/Video action
        else if (request.HasVideoUrlField && string.IsNullOrWhiteSpace(request.VideoUrl) && isUpdate)
        {
            DeleteUploadIfOwned(lesson.VideoUrl);
            lesson.VideoUrl = null;
        }
        // Priority 4: URL Link (Video or Document)
        else if (hasVideoUrl)
        {
            DeleteUploadIfOwned(lesson.VideoUrl);
            lesson.VideoUrl = request.VideoUrl!.Trim();
            lesson.ContentType = request.ContentType ?? "Video";
            lesson.ContentBody = null;
        }
        // Priority 5: AI / Text Body
        else if (hasContentBody)
        {
            DeleteUploadIfOwned(lesson.VideoUrl);
            lesson.VideoUrl = null;
            lesson.ContentType = "Text";
            lesson.ContentBody = request.ContentBody;
        }
        // Fallback: If just updating metadata but keeping existing assets
        else if (isUpdate)
        {
             if (!string.IsNullOrWhiteSpace(request.ContentType)) lesson.ContentType = request.ContentType;
        }
    }

    private static bool IsVideoRequest(LessonRequestData request)
    {
        return string.Equals(request.ContentType, "Video", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextRequest(LessonRequestData request)
    {
        return string.Equals(request.ContentType, "Text", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PermissionCatalogItem> GetPermissionCatalog() =>
    [
        new("dashboard.view", "Xem dashboard", "Tổng quan", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("users.manage", "Quản lý người dùng", "Quản trị hệ thống", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("departments.manage", "Quản lý phòng ban", "Quản trị hệ thống", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("courses.manage", "Quản lý khóa học", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("course.levels.manage", "Quản lý level khóa học", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("content.modules.manage", "QL kho chương", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("content.lessons.manage", "QL bài học", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("content.documents.manage", "QL kho tài liệu", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("content.quizzes.manage", "QL kho quiz", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("categories.manage", "Quản lý danh mục", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("faqs.manage", "Quản lý FAQ", "Nội dung đào tạo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("jobtitles.manage", "Qu?n l? ch?c danh", "Nh?n s?", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("schedules.manage", "Quản lý lịch học", "Đào tạo offline", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("attendance.manage", "Quản lý điểm danh", "Đào tạo offline", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("analytics.view", "Xem ph?n t?ch n?ng cao", "Ph?n t?ch & b?o c?o", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("reports.export", "Xu?t b?o c?o", "Ph?n t?ch & b?o c?o", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("auditlogs.view", "Xem nhật ký hoạt động", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin"),
        new("backup.manage", "Quản lý backup", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin"),
        new("permissions.manage", "Ph?n quy?n h? th?ng", "B?o m?t & h? th?ng", "IT", "IT Admin", "Administrator", "Admin"),
        new("newsletter.manage", "Quản lý newsletter", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("settings.manage", "Quản lý cài đặt hệ thống", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin"),
        new("system.admin", "Toàn quyền hệ thống", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin")
    ];

    private static bool RoleMatches(string? actualRoleName, string expectedRole)
    {
        if (string.IsNullOrWhiteSpace(actualRoleName)) return false;
        return string.Equals(actualRoleName.Trim(), expectedRole, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetDefaultPermissionKeysForRole(string? roleName)
    {
        var catalog = GetPermissionCatalog();
        if (string.IsNullOrWhiteSpace(roleName)) return [];

        if (RoleMatches(roleName, "IT") || RoleMatches(roleName, "IT Admin") || RoleMatches(roleName, "Administrator") || RoleMatches(roleName, "Admin"))
            return catalog.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return catalog
            .Where(item => item.DefaultRoles.Any(r => RoleMatches(roleName, r)))
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task EnsureCompatibilitySchemaAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(DatabaseCompatibility.SchemaPatchSql);
    }

    private async Task EnsureDefaultPermissionsAsync()
    {
        await EnsureCompatibilitySchemaAsync();
        var catalog = GetPermissionCatalog();
        var existingPermissions = await _db.Permissions.ToListAsync();
        var existingByKey = existingPermissions
            .Where(p => !string.IsNullOrWhiteSpace(p.PermissionKey))
            .GroupBy(p => p.PermissionKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in catalog)
        {
            if (existingByKey.TryGetValue(item.Key, out var permission))
            {
                permission.Description = item.Description;
            }
            else
            {
                _db.Permissions.Add(new Permission
                {
                    PermissionKey = item.Key,
                    Description = item.Description
                });
            }
        }

        await _db.SaveChangesAsync();

        var permissions = await _db.Permissions.ToListAsync();
        var permissionsByKey = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p.PermissionKey))
            .ToDictionary(p => p.PermissionKey!, StringComparer.OrdinalIgnoreCase);

        var roles = await _db.Roles.Include(r => r.Permissions).ToListAsync();
        foreach (var role in roles)
        {
            var defaultKeys = GetDefaultPermissionKeysForRole(role.RoleName);
            foreach (var key in defaultKeys)
            {
                if (!permissionsByKey.TryGetValue(key, out var permission))
                    continue;

                if (!role.Permissions.Any(p => p.PermissionId == permission.PermissionId))
                    role.Permissions.Add(permission);
            }
        }

        await _db.SaveChangesAsync();
    }

    // Dashboard chính IT
    public async Task<IActionResult> Index()
    {
        var auth = RequireIT();
        if (auth != null) return auth;
        return View();
    }

    // API: Reset và Gieo dữ liệu mẫu thực tế
    [HttpPost("/api/it/force-seed")]
    public async Task<IActionResult> ForceSeed()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        try
        {
            await KhoaHoc.Infrastructure.DatabaseSeeder.SeedAsync(_db, forceReset: true);
            return Ok(new { success = true, message = "Gieo dữ liệu thực tế thành công!" });
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException != null ? $"\nChi tiết: {ex.InnerException.Message}" : "";
            return StatusCode(500, new { error = "Lỗi khi gieo dữ liệu: " + ex.Message + inner });
        }
    }

    // API: Thống kê tổng quan hệ thống
    [HttpGet("/api/it/stats")]
    public async Task<IActionResult> Stats()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var totalUsers = await _db.Users.CountAsync();
        var activeUsers = await _db.Users.CountAsync(u => u.Status == "Active");
        var totalDepartments = await _db.Departments.CountAsync();
        
        var userRoleDist = await _db.Roles
            .AsNoTracking()
            .Select(r => new
            {
                role = r.RoleName ?? "Chưa gán role",
                count = r.Users.Count()
            })
            .Where(x => x.count > 0)
            .OrderByDescending(x => x.count)
            .ToDictionaryAsync(x => x.role, x => x.count);

        if (!userRoleDist.Any())
        {
            userRoleDist["IT Admin"] = await _db.Users.CountAsync(u => u.IsItadmin == true);
            userRoleDist["Học viên"] = await _db.Users.CountAsync(u => u.IsItadmin != true);
        }

        return Json(new
        {
            totalUsers,
            activeUsers,
            totalDepartments,
            userRoleDist
        });
    }

    // API: Danh s?ch users (ph?n trang, t?m ki?m)
    [HttpGet("/api/it/users")]
    public async Task<IActionResult> GetUsers(string? search, string? status, int page = 1, int pageSize = 15)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var query = _db.Users
            .Include(u => u.Department)
            .Include(u => u.JobTitle)
            .Include(u => u.Roles)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(u => (u.FullName != null && u.FullName.Contains(search))
                                  || (u.Username != null && u.Username.Contains(search))
                                  || (u.Email != null && u.Email.Contains(search))
                                  || (u.EmployeeCode != null && u.EmployeeCode.Contains(search)));

        if (!string.IsNullOrEmpty(status))
            query = query.Where(u => u.Status == status);

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.UserId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                userId = u.UserId,
                employeeCode = u.EmployeeCode,
                fullName = u.FullName,
                username = u.Username,
                email = u.Email,
                department = u.Department != null ? u.Department.DepartmentName : "N/A",
                departmentId = u.DepartmentId,
                jobTitle = u.JobTitle != null ? u.JobTitle.TitleName : "N/A",
                isItadmin = u.IsItadmin,
                status = u.Status,
                lastLogin = u.LastLogin,
                roles = u.Roles.Select(r => new { r.RoleId, r.RoleName })
            })
            .ToListAsync();

        return Json(new { total, page, pageSize, users });
    }

    // API: Tạo user mới
    [HttpPost("/api/it/users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { error = "Username và Password là bắt buộc." });

        // Chuẩn hóa email và username sang chữ thường, bắt buộc đuôi @basau.net
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            dto.Email = dto.Email.Split('@')[0].Trim().ToLower() + "@basau.net";
        }
        if (!string.IsNullOrWhiteSpace(dto.Username))
        {
            dto.Username = dto.Username.Split('@')[0].Trim().ToLower();
        }
        else if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            dto.Username = dto.Email.Split('@')[0].Trim().ToLower();
        }

        // Kiểm tra username trùng
        if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest(new { error = "Username đã tồn tại." });

        var passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(dto.Password));

        var user = new User
        {
            Username = dto.Username,
            FullName = dto.FullName,
            Email = dto.Email,
            EmployeeCode = dto.EmployeeCode,
            DepartmentId = dto.DepartmentId,
            IsItadmin = dto.IsItAdmin,
            PasswordHash = passwordHash,
            Status = "Active"
        };

        try
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Ghi AuditLog
            var currentUserIdStr = HttpContext.Session.GetString("UserID");
            var currentUserId = string.IsNullOrEmpty(currentUserIdStr) ? 1 : int.Parse(currentUserIdStr);
            
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUserId,
                ActionType = "INSERT",
                TableName = "Users",
                Description = $"Tạo tài khoản mới: {dto.Username} (ID: {user.UserId})",
                Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

            return Ok(new { success = true, userId = user.UserId });
        }
        catch (Exception ex)
        {
            Console.WriteLine("======= CREATE USER ERROR =======");
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null) Console.WriteLine("INNER: " + ex.InnerException.Message);
            Console.WriteLine("=================================");
            return StatusCode(500, new { error = ex.InnerException != null ? ex.InnerException.Message : ex.Message });
        }
    }

    // API: Cập nhật user
    [HttpPut("/api/it/users/{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Chuẩn hóa email nếu có cập nhật
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            dto.Email = dto.Email.Split('@')[0].Trim().ToLower() + "@basau.net";
        }

        user.FullName = dto.FullName ?? user.FullName;
        user.Email = dto.Email ?? user.Email;
        user.Status = dto.Status ?? user.Status;
        user.DepartmentId = dto.DepartmentId ?? user.DepartmentId;
        user.IsItadmin = dto.IsItAdmin ?? user.IsItadmin;

        if (!string.IsNullOrEmpty(dto.NewPassword))
            user.PasswordHash = SHA256.HashData(Encoding.UTF8.GetBytes(dto.NewPassword));

        await _db.SaveChangesAsync();

        // Ghi AuditLog
        var currentUserId = int.Parse(HttpContext.Session.GetString("UserID")!);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUserId,
            ActionType = "UPDATE",
            TableName = "Users",
            Description = $"Cập nhật tài khoản UserID: {id}",
            Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // API: Xóa (soft delete) user
    [HttpDelete("/api/it/users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users
            .Include(u => u.Enrollments)
            .Include(u => u.TrainingAssignmentUsers)
            .Include(u => u.UserExams)
            .Include(u => u.UserLessonLogs)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user == null) return NotFound();

        using var transaction = await _db.Database.BeginTransactionAsync();
        try {
            // 1. Xóa các dữ liệu học tập & đánh giá (Sử dụng SQL trực tiếp để tối ưu hiệu suất)
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserAnswers WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE UserID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM QuizSessionStates WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE UserID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserExams WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Enrollments WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM TrainingAssignments WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserLessonLogs WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserRoles WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserPermissions WHERE UserID = {0}", id);

            // 2. Xóa các thành tích & dữ liệu phụ trợ
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM AttendanceLogs WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Certificates WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserBadges WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserSkills WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SurveyResults WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserPoints WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM NewsletterSubscriptions WHERE UserID = {0}", id);

            // 3. Xử lý các liên kết lịch sử (Chuyển sang NULL để bảo toàn tính toàn vẹn)
            await _db.Database.ExecuteSqlRawAsync("UPDATE Courses SET CreatedBy = NULL WHERE CreatedBy = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET UserID = NULL WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE TrainingAssignments SET AssignedBy = NULL WHERE AssignedBy = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE IT_Movement_Logs SET EmployeeID = NULL WHERE EmployeeID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE IT_Movement_Logs SET ActionBy = NULL WHERE ActionBy = {0}", id);

            // 4. Xóa chính User
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Users WHERE UserID = {0}", id);

            // 5. Ghi AuditLog hoạt động xóa (của người thực hiện xóa)
            var currentUserIdStr = HttpContext.Session.GetString("UserID");
            var currentUserId = string.IsNullOrEmpty(currentUserIdStr) ? 0 : int.Parse(currentUserIdStr);
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUserId,
                ActionType = "DELETE",
                TableName = "Users",
                Description = $"Xóa vĩnh viễn tài khoản: {user.Username} (ID: {id})",
                Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
            return Ok(new { success = true });
        } catch (Exception ex) {
            await transaction.RollbackAsync();
            var inner = ex.InnerException != null ? $"\nChi tiết: {ex.InnerException.Message}" : "";
            return StatusCode(500, new { error = "Lỗi khi xóa tài khoản: " + ex.Message + inner });
        }
    }

    // API: Danh sách roles
    [HttpGet("/api/it/roles")]
    public async Task<IActionResult> GetRoles()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var roles = await _db.Roles
            .Select(r => new { r.RoleId, r.RoleName })
            .ToListAsync();
        return Json(roles);
    }

    [HttpGet("/api/it/permission-target-users")]
    public async Task<IActionResult> GetPermissionTargetUsers()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == "Active")
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName,
                username = u.Username
            })
            .ToListAsync();

        return Json(users);
    }

    [HttpGet("/api/it/my-permissions")]
    public async Task<IActionResult> GetMyPermissions()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureDefaultPermissionsAsync();

        var userIdStr = HttpContext.Session.GetString("UserID");
        if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        var userId = int.Parse(userIdStr);
        
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return NotFound(new { error = "Không tìm thấy người dùng." });

        List<string> grantedPermissions;
        if (user.IsItadmin == true)
        {
            // IT Admin gets everything
            grantedPermissions = await _db.Permissions
                .AsNoTracking()
                .Where(p => p.PermissionKey != null)
                .Select(p => p.PermissionKey!)
                .Distinct()
                .ToListAsync();
        }
        else
        {
            grantedPermissions = user.Roles
                .SelectMany(r => r.Permissions)
                .Concat(user.UserPermissions.Select(up => up.Permission))
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.PermissionKey))
                .Select(p => p!.PermissionKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        return Json(new
        {
            userId = user.UserId,
            fullName = user.FullName ?? user.Username,
            roleNames = user.Roles.Select(r => r.RoleName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
            permissions = grantedPermissions,
            isItAdmin = user.IsItadmin == true
        });
    }

    // API: Gán role cho user
    [HttpPost("/api/it/users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> AssignRole(int userId, int roleId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserId == userId);
        var role = await _db.Roles.FindAsync(roleId);
        if (user == null || role == null) return NotFound();

        if (!user.Roles.Any(r => r.RoleId == roleId))
            user.Roles.Add(role);

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveRole(int userId, int roleId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound();

        var role = user.Roles.FirstOrDefault(r => r.RoleId == roleId);
        if (role != null)
        {
            user.Roles.Remove(role);
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    // API: System settings
    [HttpGet("/api/it/settings")]
    public async Task<IActionResult> GetSettings()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var settings = await _db.SystemSettings
            .Select(s => new { s.SettingKey, s.SettingValue, s.ModifiedAt, Description = (string?)null })
            .ToListAsync();
        return Json(settings);
    }

    // API: Cập nhật setting
    [HttpPut("/api/it/settings/{key}")]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] string value)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key);
        if (setting == null) return NotFound();

        setting.SettingValue = value;
        setting.ModifiedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // API: Audit logs
    [HttpGet("/api/it/auditlogs")]
    public async Task<IActionResult> GetAuditLogs(string? actionType, int page = 1, int pageSize = 20)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var query = _db.AuditLogs
            .Include(a => a.User)
            .AsQueryable();

        if (!string.IsNullOrEmpty(actionType))
            query = query.Where(a => a.ActionType == actionType);

        var total = await query.CountAsync();
        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                logId = a.LogId,
                userName = a.User != null ? a.User.FullName : "Hệ thống",
                actionType = a.ActionType,
                tableName = a.TableName,
                description = a.Description,
                ipAddress = a.Ipaddress,
                createdAt = a.CreatedAt
            })
            .ToListAsync();

        return Json(new { total, page, logs });
    }

    // API: Danh sách departments
    [HttpGet("/api/it/departments")]
    public async Task<IActionResult> GetDepartments()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var departments = await _db.Departments
            .Select(d => new {
                d.DepartmentId,
                d.DepartmentName,
                userCount = d.Users.Count(),
                managerId = d.ManagerId,
                managerName = _db.Users.Where(u => u.UserId == d.ManagerId).Select(u => u.FullName).FirstOrDefault()
            })
            .ToListAsync();
        return Json(departments);
    }

    [HttpGet("/api/it/departments/{id}")]
    public async Task<IActionResult> GetDepartmentDetails(int id)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var department = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == id);
        if (department == null) return NotFound(new { error = "Khong tim thay phong ban." });

        var employees = await _db.Users
            .Include(u => u.JobTitle)
            .Include(u => u.Roles)
            .Where(u => u.DepartmentId == id)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName,
                username = u.Username,
                email = u.Email,
                employeeCode = u.EmployeeCode,
                status = u.Status,
                jobTitle = u.JobTitle != null ? u.JobTitle.TitleName : null,
                isDepartmentManager = department.ManagerId == u.UserId,
                roles = u.Roles.Select(r => new { r.RoleId, r.RoleName }).ToList()
            })
            .ToListAsync();

        return Json(new
        {
            departmentId = department.DepartmentId,
            departmentName = department.DepartmentName,
            managerId = department.ManagerId,
            employees
        });
    }

    [HttpPut("/api/it/departments/{id}/manager")]
    public async Task<IActionResult> AssignDepartmentManager(int id, [FromBody] AssignDepartmentManagerDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var department = await _db.Departments.FindAsync(id);
        if (department == null) return NotFound(new { error = "Khong tim thay phong ban." });

        var user = await _db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.UserId == dto.UserId);
        if (user == null) return NotFound(new { error = "Khong tim thay nhan vien." });

        user.DepartmentId = id;
        user.IsDeptAdmin = true;
        department.ManagerId = user.UserId;

        var managerRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName != null && r.RoleName.ToUpper() == "MANAGER");
        if (managerRole == null)
        {
            managerRole = new Role { RoleName = "Manager" };
            _db.Roles.Add(managerRole);
            await _db.SaveChangesAsync();
        }

        if (!user.Roles.Any(r => r.RoleId == managerRole.RoleId))
            user.Roles.Add(managerRole);

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ======================================
    // API: COURSES MANAGEMENT
    // ======================================
    [HttpGet("/api/it/courses")]
    public async Task<IActionResult> GetItCourses(string? search)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var query = _db.Courses.AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Title != null && c.Title.Contains(search));

        var courses = await query.Select(c => new
        {
            CourseId = c.CourseId,
            CourseCode = c.CourseCode,
            Title = c.Title,
            Level = c.Level,
            CategoryId = c.CategoryId,
            Category = c.Category != null ? c.Category.CategoryName : "Chung",
            IsMandatory = c.IsMandatory,
            Status = c.Status,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            TargetDepartmentId = c.TargetDepartmentId,
            TargetDepartmentIds = c.TargetDepartmentIds,
            Description = c.Description
        }).OrderByDescending(c => c.CourseId).ToListAsync();

        return Json(new { courses });
    }

    [HttpPost("/api/it/courses")]
    public async Task<IActionResult> CreateItCourse([FromBody] ItCreateCourseDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest("Title is required");
        if (string.IsNullOrWhiteSpace(dto.CourseCode)) return BadRequest(new { error = "M? kh?a h?c l? b?t bu?c." });

        var normalizedTitle = dto.Title.Trim().ToLower();
        var normalizedCode = dto.CourseCode.Trim().ToUpper();
        if (await _db.Courses.AnyAsync(c => c.Title != null && c.Title.ToLower() == normalizedTitle))
            return BadRequest(new { error = $"Kh?a h?c {dto.Title.Trim()} d? t?n t?i, kh?ng th? th?m." });
        if (await _db.Courses.AnyAsync(c => c.CourseCode != null && c.CourseCode.ToUpper() == normalizedCode))
            return BadRequest(new { error = $"M? kh?a h?c {dto.CourseCode.Trim()} d? t?n t?i, kh?ng th? th?m." });

        var course = new Course
        {
            CourseCode = dto.CourseCode.Trim(),
            Title = dto.Title,
            Description = dto.Description,
            Level = NormalizeLevel(dto.Level),
            CategoryId = dto.CategoryId,
            Status = dto.Status ?? "Active",
            IsMandatory = dto.IsMandatory,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            TargetDepartmentId = dto.TargetDepartmentIds != null && dto.TargetDepartmentIds.Any() ? dto.TargetDepartmentIds.First() : null,
            TargetDepartmentIds = dto.TargetDepartmentIds != null ? string.Join(",", dto.TargetDepartmentIds) : null,
            CreatedAt = DateTime.Now,
            CreatedBy = int.Parse(HttpContext.Session.GetString("UserID") ?? "1")
        };

        _db.Courses.Add(course);
        await _db.SaveChangesAsync();

        if (dto.TargetDepartmentIds != null && dto.TargetDepartmentIds.Any() && course.IsMandatory == true)
        {
            var deptUsers = await _db.Users.Where(u => u.DepartmentId.HasValue && dto.TargetDepartmentIds.Contains(u.DepartmentId.Value) && u.Status == "Active").ToListAsync();
            foreach (var u in deptUsers)
            {
                _db.TrainingAssignments.Add(new TrainingAssignment
                {
                    CourseId = course.CourseId,
                    UserId = u.UserId,
                    AssignedBy = course.CreatedBy,
                    AssignedDate = DateTime.Now,
                    DueDate = course.EndDate ?? DateTime.Now.AddDays(30),
                    Priority = "High"
                });
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true, id = course.CourseId });
    }

    [HttpPut("/api/it/courses/{id}")]
    public async Task<IActionResult> UpdateItCourse(int id, [FromBody] ItCreateCourseDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();

        var normalizedTitle = dto.Title?.Trim().ToLower();
        var normalizedCode = dto.CourseCode?.Trim().ToUpper();
        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            await _db.Courses.AnyAsync(c => c.CourseId != id && c.Title != null && c.Title.ToLower() == normalizedTitle))
            return BadRequest(new { error = $"Kh?a h?c {dto.Title!.Trim()} d? t?n t?i, kh?ng th? c?p nh?t tr?ng." });
        if (!string.IsNullOrWhiteSpace(normalizedCode) &&
            await _db.Courses.AnyAsync(c => c.CourseId != id && c.CourseCode != null && c.CourseCode.ToUpper() == normalizedCode))
            return BadRequest(new { error = $"M? kh?a h?c {dto.CourseCode!.Trim()} d? t?n t?i, kh?ng th? c?p nh?t tr?ng." });

        course.CourseCode = dto.CourseCode ?? course.CourseCode;
        course.Title = dto.Title ?? course.Title;
        course.Description = dto.Description;
        course.Level = NormalizeLevel(dto.Level);
        course.CategoryId = dto.CategoryId;
        course.Status = dto.Status ?? course.Status;
        course.IsMandatory = dto.IsMandatory;
        course.StartDate = dto.StartDate;
        course.EndDate = dto.EndDate;
        course.TargetDepartmentId = dto.TargetDepartmentIds != null && dto.TargetDepartmentIds.Any() ? dto.TargetDepartmentIds.First() : null;
        course.TargetDepartmentIds = dto.TargetDepartmentIds != null ? string.Join(",", dto.TargetDepartmentIds) : null;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/courses/{id}")]
    public async Task<IActionResult> DeleteItCourse(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // 1. Xóa dữ liệu liên quan đến thi (Quiz/Exam)
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserAnswers WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE ExamID IN (SELECT ExamID FROM Exams WHERE CourseID = {0}))", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM QuizSessionStates WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE ExamID IN (SELECT ExamID FROM Exams WHERE CourseID = {0}))", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserExams WHERE ExamID IN (SELECT ExamID FROM Exams WHERE CourseID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM ExamQuestions WHERE ExamID IN (SELECT ExamID FROM Exams WHERE CourseID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Exams WHERE CourseID = {0}", id);

            // 2. Xóa dữ liệu liên quan đến bài học (Lessons/Modules)
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserLessonLogs WHERE LessonID IN (SELECT LessonID FROM Lessons WHERE ModuleID IN (SELECT ModuleID FROM CourseModules WHERE CourseID = {0}))", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM LessonAttachments WHERE LessonID IN (SELECT LessonID FROM Lessons WHERE ModuleID IN (SELECT ModuleID FROM CourseModules WHERE CourseID = {0}))", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Lessons WHERE ModuleID IN (SELECT ModuleID FROM CourseModules WHERE CourseID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM CourseModules WHERE CourseID = {0}", id);

            // 3. Xóa các quan hệ mức khóa học
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Enrollments WHERE CourseID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Certificates WHERE CourseID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM CourseFeedback WHERE CourseID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM CourseCosts WHERE CourseID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM PathCourses WHERE CourseID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM TrainingAssignments WHERE CourseID = {0}", id);

            // 4. Xóa sự kiện offline và điểm danh
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM AttendanceLogs WHERE EventID IN (SELECT EventID FROM OfflineTrainingEvents WHERE CourseID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM OfflineTrainingEvents WHERE CourseID = {0}", id);

            // 5. Xóa chính khóa học
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Courses WHERE CourseID = {0}", id);

            await transaction.CommitAsync();

            // Ghi log
            var adminIdStr = HttpContext.Session.GetString("UserID");
            int? adminId = !string.IsNullOrEmpty(adminIdStr) ? int.Parse(adminIdStr) : null;
            
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                ActionType = "DELETE",
                TableName = "Courses",
                Description = $"Xóa khóa học ID: {id}, Tiêu đề: {course.Title}",
                Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { error = "Lỗi hệ thống khi xóa khóa học: " + ex.Message });
        }
    }

    // ======================================
    // API: DEPARTMENTS MANAGEMENT
    // ======================================

    [HttpPost("/api/it/departments")]
    public async Task<IActionResult> CreateItDepartment([FromBody] CreateDepartmentDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.DepartmentName)) return BadRequest("Name required");
        if (await _db.Departments.AnyAsync(d => d.DepartmentName.ToLower() == dto.DepartmentName.Trim().ToLower()))
            return BadRequest(new { error = $"Ph?ng ban {dto.DepartmentName.Trim()} d? t?n t?i." });
        
        var dept = new Department { DepartmentName = dto.DepartmentName };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPut("/api/it/departments/{id}")]
    public async Task<IActionResult> UpdateItDepartment(int id, [FromBody] CreateDepartmentDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();

        if (await _db.Departments.AnyAsync(d => d.DepartmentId != id && d.DepartmentName.ToLower() == dto.DepartmentName.Trim().ToLower()))
            return BadRequest(new { error = $"Ph?ng ban {dto.DepartmentName.Trim()} d? t?n t?i." });

        dept.DepartmentName = dto.DepartmentName;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/departments/{id}")]
    public async Task<IActionResult> DeleteItDepartment(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();

        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    // ======================================
    // API: COURSE CONTENT (MODULES, LESSONS, EXAMS)
    // ======================================
    [HttpGet("/api/it/courses/{courseId}/content")]
    public async Task<IActionResult> GetCourseContent(int courseId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var modules = await _db.CourseModules
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.SortOrder)
            .Select(m => new {
                m.ModuleId, m.Title, m.SortOrder, m.Level,
                Lessons = m.Lessons.OrderBy(l => l.SortOrder)
                    .Select(l => new
                    {
                        l.LessonId,
                        l.Title,
                        l.ContentType,
                        l.VideoUrl,
                        l.ContentBody,
                        l.Level,
                        Attachments = l.LessonAttachments.Select(a => new { a.AttachmentId, a.FileName, a.FilePath }).ToList()
                    }).ToList()
            }).ToListAsync();
        
        var exams = await _db.Exams
            .Where(e => e.CourseId == courseId)
            .Select(e => new {
                e.ExamId, e.ExamTitle, e.DurationMinutes, e.PassScore, e.Level,
                e.MaxAttempts, e.StartDate, e.EndDate, e.TargetDepartmentId,
                QuestionsCount = _db.ExamQuestions.Count(q => q.ExamId == e.ExamId)
            }).ToListAsync();

        var documents = modules
            .SelectMany(m => m.Lessons.SelectMany(l => l.Attachments.Select(a => new
            {
                moduleId = m.ModuleId,
                moduleTitle = m.Title,
                lessonId = l.LessonId,
                lessonTitle = l.Title,
                attachmentId = a.AttachmentId,
                fileName = a.FileName,
                filePath = a.FilePath
            })))
            .ToList();

        return Json(new { modules, exams, documents });
    }

    [HttpGet("/api/it/content-library")]
    public async Task<IActionResult> GetContentLibrary()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var courses = await _db.Courses
            .AsNoTracking()
            .OrderBy(c => c.Title)
            .Select(c => new
            {
                c.CourseId,
                c.Title,
                c.CourseCode,
                c.Level
            })
            .ToListAsync();

        var modules = await _db.CourseModules
            .AsNoTracking()
            .Include(m => m.Course)
            .Include(m => m.Lessons)
            .OrderBy(m => m.Course != null ? m.Course.Title : string.Empty)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.Title)
            .Select(m => new
            {
                m.ModuleId,
                m.Title,
                m.Level,
                m.SortOrder,
                m.CourseId,
                CourseTitle = m.Course != null ? m.Course.Title : null,
                CourseCode = m.Course != null ? m.Course.CourseCode : null,
                LessonsCount = m.Lessons.Count
            })
            .ToListAsync();

        var lessons = await _db.Lessons
            .AsNoTracking()
            .Include(l => l.Module)
                .ThenInclude(m => m!.Course)
            .Include(l => l.LessonAttachments)
            .OrderBy(l => l.Module != null && l.Module.Course != null ? l.Module.Course.Title : string.Empty)
            .ThenBy(l => l.Module != null ? l.Module.Title : string.Empty)
            .ThenBy(l => l.SortOrder)
            .ThenBy(l => l.Title)
            .Select(l => new
            {
                l.LessonId,
                l.Title,
                l.Level,
                l.ContentType,
                l.VideoUrl,
                l.ContentBody,
                l.ModuleId,
                ModuleTitle = l.Module != null ? l.Module.Title : null,
                CourseId = l.Module != null ? l.Module.CourseId : null,
                CourseTitle = l.Module != null && l.Module.Course != null ? l.Module.Course.Title : null,
                CourseCode = l.Module != null && l.Module.Course != null ? l.Module.Course.CourseCode : null,
                AttachmentsCount = l.LessonAttachments.Count,
                Attachments = l.LessonAttachments
                    .OrderBy(a => a.AttachmentId)
                    .Select(a => new { a.AttachmentId, a.FileName, a.FilePath })
                    .ToList()
            })
            .ToListAsync();

        var exams = await _db.Exams
            .AsNoTracking()
            .Include(e => e.Course)
            .Include(e => e.ExamQuestions)
            .OrderBy(e => e.Course != null ? e.Course.Title : string.Empty)
            .ThenBy(e => e.ExamTitle)
            .Select(e => new
            {
                e.ExamId,
                e.ExamTitle,
                e.Level,
                e.DurationMinutes,
                e.PassScore,
                e.CourseId,
                e.MaxAttempts,
                e.StartDate,
                e.EndDate,
                e.TargetDepartmentId,
                CourseTitle = e.Course != null ? e.Course.Title : null,
                CourseCode = e.Course != null ? e.Course.CourseCode : null,
                QuestionsCount = e.ExamQuestions.Count
            })
            .ToListAsync();

        return Json(new { courses, modules, lessons, exams });
    }

    [HttpPost("/api/it/courses/{courseId}/modules")]
    public async Task<IActionResult> CreateModule(int courseId, [FromBody] ItCreateModuleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Tên chương là bắt buộc." });
        
        // Cảnh báo nếu tên chương đã tồn tại trong KHO tài liệu hệ thống
        if (await _db.CourseModules.AnyAsync(m => m.Title != null && m.Title.ToLower() == dto.Title.Trim().ToLower()))
        {
            return BadRequest(new { error = $"Chương '{dto.Title.Trim()}' đã tồn tại trong kho tài liệu. Vui lòng lấy từ kho hoặc dùng tên khác." });
        }

        var mod = new CourseModule { 
            CourseId = courseId > 0 ? courseId : null, 
            Title = dto.Title.Trim(), 
            SortOrder = dto.SortOrder ?? 0, 
            Level = NormalizeLevel(dto.Level),
            TargetDepartmentId = dto.TargetDepartmentId
        };
        _db.CourseModules.Add(mod);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, id = mod.ModuleId });
    }

    [HttpPost("/api/it/lessons/{id}/unlink")]
    [HttpPost("/api/it/lessons/{id}/unlink-from-module")]
    public async Task<IActionResult> UnlinkLesson(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });
        var lesson = await _db.Lessons.FindAsync(id);
        if (lesson == null) return NotFound();
        lesson.ModuleId = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/it/modules/{id}/unlink")]
    [HttpPost("/api/it/modules/{id}/unlink-from-course/{courseId}")]
    public async Task<IActionResult> UnlinkModule(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });
        var mod = await _db.CourseModules.FindAsync(id);
        if (mod == null) return NotFound();
        mod.CourseId = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/it/exams/{examId}/unlink-from-course/{courseId}")]
    public async Task<IActionResult> UnlinkExamFromCourse(int examId, int courseId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound(new { error = "Khong tim thay bai thi." });

        exam.CourseId = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/it/lessons")]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> CreateLesson()
    {
        // Validation and error handling for mixed lesson sources.
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();
        var dto = await ReadLessonRequestAsync();

        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Tên bài học là bắt buộc." });

        if (IsVideoRequest(dto) && (dto.VideoFile == null || dto.VideoFile.Length == 0) && string.IsNullOrWhiteSpace(dto.VideoUrl))
            return BadRequest(new { error = "BÃ i video cáº§n chá»n file video hoáº·c nháº­p link video." });
        if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody))
            return BadRequest(new { error = "BÃ i AI / vÄƒn báº£n cáº§n cÃ³ ná»™i dung." });

        var lesson = new Lesson 
        { 
            ModuleId = null, 
            Title = dto.Title.Trim(), 
            ContentType = string.IsNullOrWhiteSpace(dto.ContentType) ? "Document" : dto.ContentType,
            VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim(),
            ContentBody = string.IsNullOrWhiteSpace(dto.ContentBody) ? null : dto.ContentBody,
            SortOrder = dto.SortOrder ?? 0, 
            Level = NormalizeLevel(dto.Level) 
        };
        _db.Lessons.Add(lesson);
        await _db.SaveChangesAsync();
        await ApplyLessonAssetsAsync(lesson, dto, isUpdate: false);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, id = lesson.LessonId, videoUrl = lesson.VideoUrl, contentType = lesson.ContentType });
    }

    [HttpPost("/api/it/modules/{moduleId}/lessons")]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> CreateLesson(int moduleId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();
        var dto = await ReadLessonRequestAsync();

        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Tên bài học là bắt buộc." });
        if (await _db.Lessons.AnyAsync(l => l.ModuleId == moduleId && l.Title != null && l.Title.ToLower() == dto.Title.Trim().ToLower()))
            return BadRequest(new { error = $"Bài học '{dto.Title.Trim()}' đã tồn tại trong chương này." });

        if (IsVideoRequest(dto) && (dto.VideoFile == null || dto.VideoFile.Length == 0) && string.IsNullOrWhiteSpace(dto.VideoUrl))
            return BadRequest(new { error = "BÃ i video cáº§n chá»n file video hoáº·c nháº­p link video." });
        if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody))
            return BadRequest(new { error = "BÃ i AI / vÄƒn báº£n cáº§n cÃ³ ná»™i dung." });

        var lesson = new Lesson
        {
            ModuleId = moduleId,
            Title = dto.Title.Trim(),
            ContentType = string.IsNullOrWhiteSpace(dto.ContentType) ? "Document" : dto.ContentType,
            VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim(),
            ContentBody = string.IsNullOrWhiteSpace(dto.ContentBody) ? null : dto.ContentBody,
            SortOrder = dto.SortOrder ?? 0,
            Level = NormalizeLevel(dto.Level)
        };
        _db.Lessons.Add(lesson);
        await _db.SaveChangesAsync();
        await ApplyLessonAssetsAsync(lesson, dto, isUpdate: false);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, id = lesson.LessonId, videoUrl = lesson.VideoUrl, contentType = lesson.ContentType });
    }

    [HttpPost("/api/it/lessons/{lessonId}/attachments/upload")]
    public async Task<IActionResult> UploadLessonAttachment(int lessonId, IFormFile? file)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Bạn chưa chọn file tài liệu." });

        var lesson = await _db.Lessons.FindAsync(lessonId);
        if (lesson == null) return NotFound(new { error = "Không tìm thấy bài học." });

        var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "lessons", lessonId.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var safeFileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(uploadsRoot, safeFileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new LessonAttachment
        {
            LessonId = lessonId,
            FileName = file.FileName,
            FilePath = $"/uploads/lessons/{lessonId}/{safeFileName}"
        };

        _db.LessonAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, attachmentId = attachment.AttachmentId, fileName = attachment.FileName, filePath = attachment.FilePath });
    }

    [HttpPost("/api/it/lessons/{lessonId}/attachments/link")]
    public async Task<IActionResult> CreateLessonAttachmentLink(int lessonId, [FromBody] LessonAttachmentLinkDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest(new { error = "Link tài liệu không được để trống." });

        var lesson = await _db.Lessons.FindAsync(lessonId);
        if (lesson == null) return NotFound(new { error = "Không tìm thấy bài học." });

        var attachment = new LessonAttachment
        {
            LessonId = lessonId,
            FileName = string.IsNullOrWhiteSpace(dto.FileName) ? dto.Url.Trim() : dto.FileName.Trim(),
            FilePath = dto.Url.Trim()
        };

        _db.LessonAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, attachmentId = attachment.AttachmentId });
    }

    [HttpDelete("/api/it/attachments/{id}")]
    public async Task<IActionResult> DeleteLessonAttachment(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var attachment = await _db.LessonAttachments.FindAsync(id);
        if (attachment == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(attachment.FilePath) && attachment.FilePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var physicalPath = Path.Combine(_env.WebRootPath, attachment.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(physicalPath))
                System.IO.File.Delete(physicalPath);
        }

        _db.LessonAttachments.Remove(attachment);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/lessons/{id}")]
    public async Task<IActionResult> DeleteLesson(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        try {
            var lesson = await _db.Lessons
                .Include(l => l.LessonAttachments)
                .FirstOrDefaultAsync(l => l.LessonId == id);
            
            if (lesson != null)
            {
                // 1. Xóa nhật ký học tập liên quan
                var logs = await _db.UserLessonLogs.Where(l => l.LessonId == id).ToListAsync();
                if (logs.Any()) _db.UserLessonLogs.RemoveRange(logs);

                // 2. Xóa tài liệu đính kèm
                if (lesson.LessonAttachments != null && lesson.LessonAttachments.Any())
                {
                    _db.LessonAttachments.RemoveRange(lesson.LessonAttachments);
                }

                // 3. Xóa chính bài học
                _db.Lessons.Remove(lesson);
                
                await _db.SaveChangesAsync();
            }
            return Ok(new { success = true });
        } catch (Exception ex) {
            return StatusCode(500, new { error = "Lỗi khi xóa dữ liệu liên quan: " + ex.Message });
        }
    }

    [HttpPost("/api/it/courses/{courseId}/exams")]
    public async Task<IActionResult> CreateExam(int courseId, [FromBody] ItCreateExamDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        if (string.IsNullOrWhiteSpace(dto.ExamTitle))
            return BadRequest(new { error = "Tên quiz là bắt buộc." });

        try
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                int? effectiveCourseId = courseId > 0 ? courseId : null;
                
                // Check for duplicate title globally to ensure unique naming across the LMS
                if (await _db.Exams.AnyAsync(e => e.ExamTitle != null && e.ExamTitle.ToLower() == dto.ExamTitle.Trim().ToLower()))
                {
                   return BadRequest(new { error = $"Không thể tạo: Tiêu đề quiz '{dto.ExamTitle.Trim()}' đã tồn tại trong hệ thống. Vui lòng chọn tên khác." });
                }

                var exam = new Exam 
                { 
                    CourseId = effectiveCourseId, 
                    ExamTitle = dto.ExamTitle.Trim(), 
                    DurationMinutes = dto.DurationMinutes, 
                    PassScore = dto.PassScore, 
                    Level = NormalizeLevel(dto.Level), 
                    MaxAttempts = dto.MaxAttempts, 
                    StartDate = dto.StartDate, 
                    EndDate = dto.EndDate, 
                    TargetDepartmentId = dto.TargetDepartmentId 
                };
                
                _db.Exams.Add(exam);
                await _db.SaveChangesAsync();

                // Handle AI-generated questions or bundled questions
                if (dto.AiQuestions != null && dto.AiQuestions.Any())
                {
                    foreach (var qDto in dto.AiQuestions)
                    {
                        if (string.IsNullOrWhiteSpace(qDto.QuestionText)) continue;

                        var q = new QuestionBank { 
                            QuestionText = qDto.QuestionText.Trim(), 
                            Difficulty = "Medium" 
                        };
                        _db.QuestionBanks.Add(q);
                        await _db.SaveChangesAsync();

                        if (qDto.Options != null)
                        {
                            foreach (var opt in qDto.Options)
                            {
                                if (string.IsNullOrWhiteSpace(opt.OptionText)) continue;
                                _db.QuestionOptions.Add(new QuestionOption { 
                                    QuestionId = q.QuestionId, 
                                    OptionText = opt.OptionText.Trim(), 
                                    IsCorrect = opt.IsCorrect 
                                });
                            }
                        }
                        
                        _db.ExamQuestions.Add(new ExamQuestion { 
                            ExamId = exam.ExamId, 
                            QuestionId = q.QuestionId, 
                            Points = qDto.Points 
                        });
                    }
                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                return Ok(new { success = true, examId = exam.ExamId });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw; 
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating exam: " + ex.Message);
            return StatusCode(500, new { error = "Lỗi hệ thống khi lưu bài quiz: " + ex.Message });
        }
    }

    [HttpGet("/api/it/exams/{examId}/questions")]
    public async Task<IActionResult> GetExamQuestions(int examId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var questions = await _db.ExamQuestions
            .Include(eq => eq.Question)
                .ThenInclude(q => q.QuestionOptions)
            .Where(eq => eq.ExamId == examId)
            .Select(eq => new {
                eq.QuestionId, 
                eq.Points,
                eq.Question.QuestionText,
                Options = eq.Question.QuestionOptions.Select(o => new { o.OptionId, o.OptionText, o.IsCorrect }).ToList()
            }).ToListAsync();

        return Json(questions);
    }

    [HttpPost("/api/it/exams/{examId}/questions")]
    public async Task<IActionResult> AddExamQuestion(int examId, [FromBody] ItCreateQuestionDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var q = new QuestionBank { QuestionText = dto.QuestionText, Difficulty = "Medium" };
            _db.QuestionBanks.Add(q);
            await _db.SaveChangesAsync();

            if (dto.Options != null)
            {
                foreach(var opt in dto.Options) {
                    _db.QuestionOptions.Add(new QuestionOption { QuestionId = q.QuestionId, OptionText = opt.OptionText, IsCorrect = opt.IsCorrect });
                }
            }

            _db.ExamQuestions.Add(new ExamQuestion { ExamId = examId, QuestionId = q.QuestionId, Points = dto.Points });
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
            return Ok(new { success = true });
        }
        catch
        {
            await transaction.RollbackAsync();
            return BadRequest("L?i luu c?u h?i");
        }
    }

    [HttpPost("/api/it/exams/{examId}/questions/batch")]
    public async Task<IActionResult> SaveExamQuestionsBatch(int examId, [FromBody] List<ItCreateQuestionDto>? questions)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        if (questions == null || questions.Count == 0)
            return BadRequest(new { error = "Danh sÃ¡ch cÃ¢u há»i trá»‘ng." });

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            if (!await _db.Exams.AnyAsync(e => e.ExamId == examId))
                return NotFound(new { error = "KhÃ´ng tÃ¬m tháº¥y bÃ i kiá»ƒm tra." });

            var oldExamQuestions = await _db.ExamQuestions.Where(eq => eq.ExamId == examId).ToListAsync();
            if (oldExamQuestions.Count > 0)
            {
                _db.ExamQuestions.RemoveRange(oldExamQuestions);
                await _db.SaveChangesAsync();
            }

            foreach (var dto in questions.Where(q => !string.IsNullOrWhiteSpace(q.QuestionText)))
            {
                var question = new QuestionBank
                {
                    QuestionText = dto.QuestionText.Trim(),
                    Difficulty = "Medium"
                };
                _db.QuestionBanks.Add(question);
                await _db.SaveChangesAsync();

                if (dto.Options != null)
                {
                    foreach (var opt in dto.Options.Where(o => !string.IsNullOrWhiteSpace(o.OptionText)))
                    {
                        _db.QuestionOptions.Add(new QuestionOption
                        {
                            QuestionId = question.QuestionId,
                            OptionText = opt.OptionText.Trim(),
                            IsCorrect = opt.IsCorrect
                        });
                    }
                }

                _db.ExamQuestions.Add(new ExamQuestion
                {
                    ExamId = examId,
                    QuestionId = question.QuestionId,
                    Points = dto.Points
                });
                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error saving question batch for exam {ExamId}", examId);
            return StatusCode(500, new { error = "Lá»—i lÆ°u bá»™ cÃ¢u há»i: " + ex.Message });
        }
    }

    [HttpDelete("/api/it/exams/{examId}/questions/{questionId}")]
    public async Task<IActionResult> DeleteExamQuestion(int examId, int questionId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var eq = await _db.ExamQuestions.FirstOrDefaultAsync(x => x.ExamId == examId && x.QuestionId == questionId);
        if (eq != null) {
            _db.ExamQuestions.Remove(eq);
            await _db.SaveChangesAsync();
        }
        return Ok(new { success = true });
    }

    // ============================================================
    // API: MODULE - UPDATE & DELETE
    // ============================================================
    [HttpPut("/api/it/modules/{moduleId}")]
    public async Task<IActionResult> UpdateModule(int moduleId, [FromBody] ItCreateModuleDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var mod = await _db.CourseModules.FindAsync(moduleId);
        if (mod == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.Title) &&
            await _db.CourseModules.AnyAsync(m => m.ModuleId != moduleId && m.CourseId == mod.CourseId && m.Title != null && m.Title.ToLower() == dto.Title.Trim().ToLower()))
            return BadRequest(new { error = $"Chuong {dto.Title.Trim()} d? t?n t?i trong kh?a h?c n?y." });
        if (!string.IsNullOrWhiteSpace(dto.Title)) mod.Title = dto.Title;
        if (dto.SortOrder.HasValue) mod.SortOrder = dto.SortOrder.Value;
        mod.Level = NormalizeLevel(dto.Level);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/modules/{moduleId}")]
    public async Task<IActionResult> DeleteModule(int moduleId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var mod = await _db.CourseModules
            .Include(m => m.Lessons)
            .FirstOrDefaultAsync(m => m.ModuleId == moduleId);
        if (mod == null) return NotFound();

        // Thay vì xóa bài học, chúng ta chỉ gỡ liên kết (set ModuleId = null)
        // để tránh việc xóa nhầm dữ liệu video/tài liệu của người dùng
        foreach (var lesson in mod.Lessons)
        {
            lesson.ModuleId = null;
        }

        _db.CourseModules.Remove(mod);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: LESSON - UPDATE
    // ============================================================
    [HttpPut("/api/it/lessons/{lessonId}")]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> UpdateLesson(int lessonId)
    {
        try
        {
            var auth = RequireITApi();
            if (auth != null) return auth;

            await EnsureCompatibilitySchemaAsync();
            var dto = await ReadLessonRequestAsync();

            var lesson = await _db.Lessons.FindAsync(lessonId);
            if (lesson == null) return NotFound();
            if (!string.IsNullOrWhiteSpace(dto.Title) &&
                await _db.Lessons.AnyAsync(l => l.LessonId != lessonId && l.ModuleId == lesson.ModuleId && l.Title != null && l.Title.ToLower() == dto.Title.Trim().ToLower()))
                return BadRequest(new { error = $"B?i h?c {dto.Title.Trim()} d? t?n t?i trong chuong n?y." });

            bool isClearingVideo = dto.HasVideoUrlField && string.IsNullOrWhiteSpace(dto.VideoUrl) && (dto.VideoFile == null || dto.VideoFile.Length == 0);

            if (IsVideoRequest(dto) && !isClearingVideo && string.IsNullOrWhiteSpace(lesson.VideoUrl) && (dto.VideoFile == null || dto.VideoFile.Length == 0) && string.IsNullOrWhiteSpace(dto.VideoUrl))
                return BadRequest(new { error = "Bài video cần chọn file video hoặc nhập link video." });
            if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody) && string.IsNullOrWhiteSpace(lesson.ContentBody))
                return BadRequest(new { error = "Bài AI / văn bản cần có nội dung." });

            if (!string.IsNullOrWhiteSpace(dto.Title)) lesson.Title = dto.Title.Trim();
            if (dto.SortOrder.HasValue) lesson.SortOrder = dto.SortOrder.Value;
            lesson.Level = NormalizeLevel(dto.Level);
            await ApplyLessonAssetsAsync(lesson, dto, isUpdate: true);
            if (!string.IsNullOrWhiteSpace(dto.ContentType) && string.IsNullOrWhiteSpace(lesson.VideoUrl) && string.IsNullOrWhiteSpace(lesson.ContentBody))
                lesson.ContentType = dto.ContentType;
            await _db.SaveChangesAsync();
            return Ok(new { success = true, videoUrl = lesson.VideoUrl, contentType = lesson.ContentType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating lesson {LessonId}", lessonId);
            return StatusCode(500, new { error = "Loi cap nhat bai hoc: " + ex.Message });
        }
    }

    // ============================================================
    // API: EXAM - UPDATE & DELETE
    // ============================================================
    [HttpPut("/api/it/exams/{examId}")]
    public async Task<IActionResult> UpdateExam(int examId, [FromBody] ItCreateExamDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.ExamTitle) &&
            await _db.Exams.AnyAsync(e => e.ExamId != examId && e.CourseId == exam.CourseId && e.ExamTitle != null && e.ExamTitle.ToLower() == dto.ExamTitle.Trim().ToLower()))
            return BadRequest(new { error = $"Quiz {dto.ExamTitle.Trim()} d? t?n t?i trong kh?a h?c n?y." });

        if (!string.IsNullOrWhiteSpace(dto.ExamTitle)) exam.ExamTitle = dto.ExamTitle;
        exam.DurationMinutes = dto.DurationMinutes;
        exam.PassScore = dto.PassScore;
        exam.Level = NormalizeLevel(dto.Level);
        exam.MaxAttempts = dto.MaxAttempts;
        exam.StartDate = dto.StartDate;
        exam.EndDate = dto.EndDate;
        exam.TargetDepartmentId = dto.TargetDepartmentId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/exams/{examId}")]
    public async Task<IActionResult> DeleteExam(int examId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var exam = await _db.Exams
            .Include(e => e.ExamQuestions)
            .Include(e => e.UserExams)
            .FirstOrDefaultAsync(e => e.ExamId == examId);

        if (exam == null) return NotFound();

        try {
            // 1. Clear related Student data first
            var userExamIds = exam.UserExams.Select(ue => ue.UserExamId).ToList();
            if (userExamIds.Any()) {
                var answers = await _db.UserAnswers.Where(a => userExamIds.Contains(a.UserExamId)).ToListAsync();
                _db.UserAnswers.RemoveRange(answers);

                var sessions = await _db.QuizSessionStates.Where(s => userExamIds.Contains(s.UserExamId)).ToListAsync();
                _db.QuizSessionStates.RemoveRange(sessions);
            }

            // 2. Clear Exam links and results
            _db.ExamQuestions.RemoveRange(exam.ExamQuestions);
            _db.UserExams.RemoveRange(exam.UserExams);

            // 3. Delete the exam object
            _db.Exams.Remove(exam);

            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        } catch (Exception ex) {
            return StatusCode(500, new { error = "Loi khi xoa du lieu lien quan: " + ex.Message });
        }
    }

    // ============================================================
    // API: CATEGORIES MANAGEMENT
    // ============================================================
    [HttpPost("/api/it/courses/{courseId}/exams/copy-from/{sourceExamId}")]
    public async Task<IActionResult> CloneExam(int courseId, int sourceExamId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        try
        {
            var source = await _db.Exams
                .Include(e => e.ExamQuestions)
                    .ThenInclude(eq => eq.Question)
                        .ThenInclude(q => q.QuestionOptions)
                .FirstOrDefaultAsync(e => e.ExamId == sourceExamId);

            if (source == null) return NotFound("Nguon khong ton tai");

            int? effectiveCourseId = courseId > 0 ? courseId : null;
            string newTitle = source.ExamTitle + " (Bản sao)";

            if (await _db.Exams.AnyAsync(e => e.ExamTitle == newTitle))
            {
                newTitle = source.ExamTitle + " (Sao chép " + DateTime.Now.ToString("HHmm") + ")";
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var newExam = new Exam
                {
                    CourseId = effectiveCourseId,
                    ExamTitle = newTitle,
                    Level = source.Level,
                    DurationMinutes = source.DurationMinutes,
                    PassScore = source.PassScore,
                    MaxAttempts = source.MaxAttempts
                };

                _db.Exams.Add(newExam);
                await _db.SaveChangesAsync();

                foreach (var sq in source.ExamQuestions)
                {
                    if (sq.Question == null) continue;

                    var newQ = new QuestionBank
                    {
                        QuestionText = sq.Question.QuestionText,
                        Difficulty = sq.Question.Difficulty,
                        CategoryId = sq.Question.CategoryId
                    };
                    _db.QuestionBanks.Add(newQ);
                    await _db.SaveChangesAsync();

                    foreach (var opt in sq.Question.QuestionOptions)
                    {
                        _db.QuestionOptions.Add(new QuestionOption
                        {
                            QuestionId = newQ.QuestionId,
                            OptionText = opt.OptionText,
                            IsCorrect = opt.IsCorrect
                        });
                    }

                    _db.ExamQuestions.Add(new ExamQuestion
                    {
                        ExamId = newExam.ExamId,
                        QuestionId = newQ.QuestionId,
                        Points = sq.Points
                    });
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { success = true, id = newExam.ExamId, title = newExam.ExamTitle });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Loi sao chep: " + ex.Message });
        }
    }

    [HttpPost("/api/it/exams/generate")]
    public async Task<IActionResult> GenerateExamWithAI([FromBody] PromptDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });
        if (string.IsNullOrWhiteSpace(dto.Prompt)) return BadRequest("Prompt required");

        try
        {
            var quizData = await _aiService.GenerateQuizAsync(dto.Prompt);
            return Ok(quizData);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("/api/it/exams/generate-from-file")]
    public async Task<IActionResult> GenerateExamFromFile([FromBody] PromptFileDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });
        if (string.IsNullOrWhiteSpace(dto.Base64Data)) return BadRequest("File data required");

        try
        {
            var quizData = await _aiService.GenerateQuizFromDocumentAsync(dto.Base64Data, dto.MimeType);
            return Ok(quizData);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("/api/it/exams/{examId}/link-to-course/{courseId}")]
    public async Task<IActionResult> LinkExamToCourse(int examId, int courseId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound("Khong tim thay bai thi");

        if (exam.CourseId == courseId && courseId > 0)
        {
            return Ok(new { success = true, info = "Quiz này đã có sẵn trong khóa học này." });
        }

        exam.CourseId = courseId > 0 ? courseId : null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, title = exam.ExamTitle });
    }

    [HttpPost("/api/it/modules/{moduleId}/link-to-course/{courseId}")]
    public async Task<IActionResult> LinkModuleToCourse(int moduleId, int courseId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var mod = await _db.CourseModules.FindAsync(moduleId);
        if (mod == null) return NotFound("Khong tim thay chuong");

        mod.CourseId = courseId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/it/lessons/{lessonId}/link-to-module/{moduleId}")]
    public async Task<IActionResult> LinkLessonToModule(int lessonId, int moduleId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var lesson = await _db.Lessons.FindAsync(lessonId);
        if (lesson == null) return NotFound("Khong tim thay bai giang");

        lesson.ModuleId = moduleId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("/api/it/categories")]
    public async Task<IActionResult> GetCategories()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var cats = await _db.Categories
            .Select(c => new
            {
                categoryId = c.CategoryId,
                categoryName = c.CategoryName,
                ownerDeptId = c.OwnerDeptId,
                courseCount = c.Courses.Count(),
                faqCount = c.Faqs.Count(),
                questionBankCount = c.QuestionBanks.Count()
            })
            .ToListAsync();
        return Json(cats);
    }

    [HttpPost("/api/it/categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.CategoryName))
            return BadRequest(new { error = "Tên danh mục không được trống." });

        var cat = new Category { CategoryName = dto.CategoryName, OwnerDeptId = dto.OwnerDeptId };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, categoryId = cat.CategoryId });
    }

    [HttpPut("/api/it/categories/{id}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.CategoryName)) cat.CategoryName = dto.CategoryName;
        cat.OwnerDeptId = dto.OwnerDeptId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/categories/{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound();

        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: FAQ MANAGEMENT
    // ============================================================
    [HttpGet("/api/it/faqs")]
    public async Task<IActionResult> GetFaqs(string? search)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var query = _db.Faqs.Include(f => f.Category).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(f => (f.Question != null && f.Question.Contains(search)) || (f.Answer != null && f.Answer.Contains(search)));

        var faqs = await query
            .OrderByDescending(f => f.Faqid)
            .Select(f => new
            {
                faqId = f.Faqid,
                question = f.Question,
                answer = f.Answer,
                categoryId = f.CategoryId,
                categoryName = f.Category != null ? f.Category.CategoryName : "Chung"
            })
            .ToListAsync();
        return Json(faqs);
    }

    [HttpPost("/api/it/faqs")]
    public async Task<IActionResult> CreateFaq([FromBody] FaqDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.Question)) return BadRequest(new { error = "C?u h?i kh?ng du?c tr?ng." });

        var faq = new Faq { Question = dto.Question, Answer = dto.Answer, CategoryId = dto.CategoryId };
        _db.Faqs.Add(faq);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, faqId = faq.Faqid });
    }

    [HttpPut("/api/it/faqs/{id}")]
    public async Task<IActionResult> UpdateFaq(int id, [FromBody] FaqDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var faq = await _db.Faqs.FindAsync(id);
        if (faq == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Question)) faq.Question = dto.Question;
        faq.Answer = dto.Answer;
        faq.CategoryId = dto.CategoryId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/faqs/{id}")]
    public async Task<IActionResult> DeleteFaq(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var faq = await _db.Faqs.FindAsync(id);
        if (faq == null) return NotFound();

        _db.Faqs.Remove(faq);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: BACKUP LOG
    // ============================================================
    [HttpGet("/api/it/backuplogs")]
    public async Task<IActionResult> GetBackupLogs()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var logs = await _db.BackupLogs
            .OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .Select(b => new
            {
                backupId = b.BackupId,
                fileName = b.FileName,
                backupType = b.BackupType,
                createdAt = b.CreatedAt
            })
            .ToListAsync();
        return Json(logs);
    }

    [HttpPost("/api/it/backuplogs")]
    public async Task<IActionResult> CreateBackup([FromBody] CreateBackupDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var fileName = $"LMS_Backup_{dto.BackupType}_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
        var backup = new BackupLog
        {
            FileName = fileName,
            BackupType = dto.BackupType ?? "Manual",
            CreatedAt = DateTime.Now
        };
        _db.BackupLogs.Add(backup);
        await _db.SaveChangesAsync();

        // Write physical SQL backup file to disk under wwwroot/backups
        try
        {
            var backupDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "backups");
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }
            var filePath = Path.Combine(backupDir, fileName);

            var sqlContent = await GenerateSqlDumpAsync();
            await System.IO.File.WriteAllTextAsync(filePath, sqlContent, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating physical backup file: " + fileName);
        }

        return Ok(new { success = true, fileName, backupId = backup.BackupId });
    }

    [HttpGet("/api/it/backuplogs/download/{id}")]
    public async Task<IActionResult> DownloadBackup(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var backup = await _db.BackupLogs.FindAsync(id);
        if (backup == null || string.IsNullOrEmpty(backup.FileName)) return NotFound(new { error = "Không tìm thấy bản backup hoặc tên file trống" });

        // Ensure file name matches .sql or keep original
        var downloadFileName = backup.FileName;
        if (downloadFileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            downloadFileName = Path.ChangeExtension(downloadFileName, ".sql");
        }

        var backupDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "backups");
        var filePath = Path.Combine(backupDir, backup.FileName);

        if (!System.IO.File.Exists(filePath))
        {
            try
            {
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }
                var sqlContent = await GenerateSqlDumpAsync();
                await System.IO.File.WriteAllTextAsync(filePath, sqlContent, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating backup file on the fly: " + backup.FileName);
            }
        }

        if (System.IO.File.Exists(filePath))
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(bytes, "text/plain", downloadFileName);
        }

        return NotFound(new { error = "Không thể sinh hoặc tìm thấy file backup vật lý" });
    }

    [HttpDelete("/api/it/backuplogs/{id}")]
    public async Task<IActionResult> DeleteBackup(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var backup = await _db.BackupLogs.FindAsync(id);
        if (backup == null || string.IsNullOrEmpty(backup.FileName)) return NotFound(new { error = "Không tìm thấy bản backup" });

        var backupDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "backups");
        var filePath = Path.Combine(backupDir, backup.FileName);

        if (System.IO.File.Exists(filePath))
        {
            try
            {
                System.IO.File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting backup file: " + filePath);
            }
        }

        _db.BackupLogs.Remove(backup);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    private async Task<string> GenerateSqlDumpAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ========================================================");
        sb.AppendLine($"-- LMS Corporate Database Backup SQL Dump");
        sb.AppendLine($"-- Generated At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Host database: db48874.public.databaseasp.net");
        sb.AppendLine("-- ========================================================");
        sb.AppendLine();

        // Disable constraint checks to allow inserting out-of-order safely
        sb.AppendLine("EXEC sp_MSforeachtable \"ALTER TABLE ? NOCHECK CONSTRAINT all\";");
        sb.AppendLine();

        try
        {
            sb.Append(GenerateTableInserts(await _db.SystemSettings.ToListAsync(), "SystemSettings"));
            sb.Append(GenerateTableInserts(await _db.Roles.ToListAsync(), "Roles"));
            sb.Append(GenerateTableInserts(await _db.Departments.ToListAsync(), "Departments"));
            sb.Append(GenerateTableInserts(await _db.JobTitles.ToListAsync(), "JobTitles"));
            sb.Append(GenerateTableInserts(await _db.Users.ToListAsync(), "Users"));
            sb.Append(GenerateTableInserts(await _db.Permissions.ToListAsync(), "Permissions"));
            sb.Append(GenerateTableInserts(await _db.UserPermissions.ToListAsync(), "UserPermissions"));
            sb.Append(GenerateTableInserts(await _db.Categories.ToListAsync(), "Categories"));
            sb.Append(GenerateTableInserts(await _db.Courses.ToListAsync(), "Courses"));
            sb.Append(GenerateTableInserts(await _db.CourseModules.ToListAsync(), "CourseModules"));
            sb.Append(GenerateTableInserts(await _db.Lessons.ToListAsync(), "Lessons"));
            sb.Append(GenerateTableInserts(await _db.LessonAttachments.ToListAsync(), "LessonAttachments"));
            sb.Append(GenerateTableInserts(await _db.Exams.ToListAsync(), "Exams"));
            sb.Append(GenerateTableInserts(await _db.ExamQuestions.ToListAsync(), "ExamQuestions"));
            sb.Append(GenerateTableInserts(await _db.QuestionOptions.ToListAsync(), "QuestionOptions"));
            sb.Append(GenerateTableInserts(await _db.Enrollments.ToListAsync(), "Enrollments"));
            sb.Append(GenerateTableInserts(await _db.UserExams.ToListAsync(), "UserExams"));
            sb.Append(GenerateTableInserts(await _db.UserAnswers.ToListAsync(), "UserAnswers"));
            sb.Append(GenerateTableInserts(await _db.UserLessonLogs.ToListAsync(), "UserLessonLogs"));
            sb.Append(GenerateTableInserts(await _db.TrainingAssignments.ToListAsync(), "TrainingAssignments"));
            sb.Append(GenerateTableInserts(await _db.Faqs.ToListAsync(), "Faqs"));
            sb.Append(GenerateTableInserts(await _db.OfflineTrainingEvents.ToListAsync(), "OfflineTrainingEvents"));
            sb.Append(GenerateTableInserts(await _db.AttendanceLogs.ToListAsync(), "AttendanceLogs"));
            sb.Append(GenerateTableInserts(await _db.DocumentLibraries.ToListAsync(), "DocumentLibrary"));
            sb.Append(GenerateTableInserts(await _db.Badges.ToListAsync(), "Badges"));
            sb.Append(GenerateTableInserts(await _db.UserBadges.ToListAsync(), "UserBadges"));
            sb.Append(GenerateTableInserts(await _db.Certificates.ToListAsync(), "Certificates"));
            sb.Append(GenerateTableInserts(await _db.Skills.ToListAsync(), "Skills"));
            sb.Append(GenerateTableInserts(await _db.UserSkills.ToListAsync(), "UserSkills"));
            sb.Append(GenerateTableInserts(await _db.DeptRequiredSkills.ToListAsync(), "DeptRequiredSkills"));
            sb.Append(GenerateTableInserts(await _db.LearningPaths.ToListAsync(), "LearningPaths"));
            sb.Append(GenerateTableInserts(await _db.PathCourses.ToListAsync(), "PathCourses"));
            sb.Append(GenerateTableInserts(await _db.UserPathProgresses.ToListAsync(), "UserPathProgresses"));
            sb.Append(GenerateTableInserts(await _db.Surveys.ToListAsync(), "Surveys"));
            sb.Append(GenerateTableInserts(await _db.SurveyResults.ToListAsync(), "SurveyResults"));
            sb.Append(GenerateTableInserts(await _db.NewsletterSubscriptions.ToListAsync(), "NewsletterSubscriptions"));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"-- ERROR during backup generation: {ex.Message}");
            _logger.LogError(ex, "Error generating tables data inserts in SQL backup");
        }

        sb.AppendLine();
        // Re-enable constraint checks
        sb.AppendLine("EXEC sp_MSforeachtable \"ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all\";");

        return sb.ToString();
    }

    private string GenerateTableInserts<T>(List<T> items, string tableName)
    {
        if (items == null || !items.Any()) return $"-- Table {tableName} is empty\n\n";

        var sb = new StringBuilder();
        var type = typeof(T);

        // Get properties that map to database columns and are not NotMapped
        var properties = type.GetProperties()
            .Where(p => {
                var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                var isPrimitiveOrBasic = t.IsPrimitive ||
                       t == typeof(string) ||
                       t == typeof(DateTime) ||
                       t == typeof(decimal) ||
                       t == typeof(Guid);
                if (!isPrimitiveOrBasic) return false;

                var isNotMapped = p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), true).Any();
                return !isNotMapped;
            })
            .ToList();

        if (!properties.Any()) return $"-- Table {tableName} has no mapable columns\n\n";

        // Map property name to column name using ColumnAttribute if present
        var columnMappings = properties.Select(p => {
            var colAttr = p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), true)
                .FirstOrDefault() as System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
            return new { Property = p, ColumnName = colAttr?.Name ?? p.Name };
        }).ToList();

        var columnsList = string.Join(", ", columnMappings.Select(c => $"[{c.ColumnName}]"));

        // Determine if table has an identity column using EF Core Metadata
        string? identityColumnName = null;
        try
        {
            var entityType = _db.Model.FindEntityType(type);
            var primaryKey = entityType?.FindPrimaryKey();
            var hasIdentity = primaryKey?.Properties.Any(p => p.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd) ?? false;
            if (hasIdentity && primaryKey != null && primaryKey.Properties.Any())
            {
                var keyProp = primaryKey.Properties.First();
                var propInfo = type.GetProperty(keyProp.Name);
                var colAttr = propInfo?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), true)
                    .FirstOrDefault() as System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
                identityColumnName = colAttr?.Name ?? keyProp.Name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting identity column for " + tableName);
        }

        sb.AppendLine($"-- Table: {tableName}");
        if (!string.IsNullOrEmpty(identityColumnName))
        {
            sb.AppendLine($"SET IDENTITY_INSERT [{tableName}] ON;");
        }

        foreach (var item in items)
        {
            var valuesList = new List<string>();
            foreach (var mapping in columnMappings)
            {
                var val = mapping.Property.GetValue(item);
                if (val == null)
                {
                    valuesList.Add("NULL");
                }
                else
                {
                    var underlyingType = Nullable.GetUnderlyingType(mapping.Property.PropertyType) ?? mapping.Property.PropertyType;
                    if (underlyingType == typeof(string))
                    {
                        var str = val.ToString()!.Replace("'", "''");
                        valuesList.Add($"N'{str}'");
                    }
                    else if (underlyingType == typeof(DateTime))
                    {
                        var dt = (DateTime)val;
                        valuesList.Add($"'{dt:yyyy-MM-dd HH:mm:ss.fff}'");
                    }
                    else if (underlyingType == typeof(bool))
                    {
                        valuesList.Add((bool)val ? "1" : "0");
                    }
                    else if (underlyingType == typeof(decimal) || underlyingType == typeof(double) || underlyingType == typeof(float))
                    {
                        valuesList.Add(Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture)!);
                    }
                    else
                    {
                        valuesList.Add(val.ToString()!);
                    }
                }
            }
            var values = string.Join(", ", valuesList);
            sb.AppendLine($"INSERT INTO [{tableName}] ({columnsList}) VALUES ({values});");
        }

        if (!string.IsNullOrEmpty(identityColumnName))
        {
            sb.AppendLine($"SET IDENTITY_INSERT [{tableName}] OFF;");
        }
        sb.AppendLine();

        return sb.ToString();
    }



    // ============================================================
    // API: PERMISSIONS
    // ============================================================
    [HttpGet("/api/it/permissions")]
    public async Task<IActionResult> GetPermissions()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureDefaultPermissionsAsync();

        var permissions = await _db.Permissions
            .Include(p => p.Roles)
            .Select(p => new
            {
                permissionId = p.PermissionId,
                permissionKey = p.PermissionKey,
                description = p.Description,
                roles = p.Roles.Select(r => new { r.RoleId, r.RoleName })
            })
            .ToListAsync();
        return Json(permissions);
    }

    [HttpGet("/api/it/roles/{roleId}/permissions")]
    public async Task<IActionResult> GetRolePermissionBoard(int roleId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureDefaultPermissionsAsync();

        var role = await _db.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId);
        if (role == null) return NotFound(new { error = "Không tìm thấy role." });

        var enabled = role.Permissions.Select(p => p.PermissionId).ToHashSet();
        var catalogByKey = GetPermissionCatalog().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.PermissionKey)
            .Select(p => new
            {
                permissionId = p.PermissionId,
                permissionKey = p.PermissionKey,
                description = p.Description,
                enabled = enabled.Contains(p.PermissionId),
                category = p.PermissionKey != null && catalogByKey.ContainsKey(p.PermissionKey) ? catalogByKey[p.PermissionKey].Category : "Khác",
                source = enabled.Contains(p.PermissionId) ? "role" : "none"
            })
            .ToListAsync();

        return Json(new { targetName = role.RoleName, permissions });
    }

    [HttpGet("/api/it/users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPermissionBoard(int userId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureDefaultPermissionsAsync();

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound(new { error = "Không tìm thấy người dùng." });

        var inherited = user.Roles.SelectMany(r => r.Permissions).Select(p => p.PermissionId).ToHashSet();
        var direct = user.UserPermissions.Select(p => p.PermissionId).ToHashSet();
        var catalogByKey = GetPermissionCatalog().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.PermissionKey)
            .Select(p => new
            {
                permissionId = p.PermissionId,
                permissionKey = p.PermissionKey,
                description = p.Description,
                enabled = inherited.Contains(p.PermissionId) || direct.Contains(p.PermissionId),
                inherited = inherited.Contains(p.PermissionId),
                direct = direct.Contains(p.PermissionId),
                category = p.PermissionKey != null && catalogByKey.ContainsKey(p.PermissionKey) ? catalogByKey[p.PermissionKey].Category : "Khác",
                source = direct.Contains(p.PermissionId) && inherited.Contains(p.PermissionId)
                    ? "role+user"
                    : direct.Contains(p.PermissionId)
                        ? "user"
                        : inherited.Contains(p.PermissionId)
                            ? "role"
                            : "none"
            })
            .ToListAsync();

        return Json(new
        {
            targetName = user.FullName ?? user.Username,
            roleNames = user.Roles.Select(r => r.RoleName).ToList(),
            permissions
        });
    }

    [HttpPost("/api/it/roles/{roleId}/permissions/{permissionId}/toggle")]
    public async Task<IActionResult> ToggleRolePermission(int roleId, int permissionId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureDefaultPermissionsAsync();

        var role = await _db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == roleId);
        var permission = await _db.Permissions.FindAsync(permissionId);
        if (role == null || permission == null) return NotFound();

        var existing = role.Permissions.FirstOrDefault(p => p.PermissionId == permissionId);
        var enabled = existing == null;
        if (enabled)
            role.Permissions.Add(permission);
        else
            role.Permissions.Remove(existing!);

        await _db.SaveChangesAsync();
        return Ok(new { success = true, enabled });
    }

    [HttpPost("/api/it/users/{userId}/permissions/{permissionId}/toggle")]
    public async Task<IActionResult> ToggleUserPermission(int userId, int permissionId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureDefaultPermissionsAsync();

        var user = await _db.Users
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.UserId == userId);
        var permission = await _db.Permissions.FindAsync(permissionId);
        if (user == null || permission == null) return NotFound();

        var existing = await _db.UserPermissions.FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permissionId);
        var inherited = user.Roles.SelectMany(r => r.Permissions).Any(p => p.PermissionId == permissionId);
        if (inherited)
            return BadRequest(new { error = "Quy?n n?y dang du?c k? th?a t? role. H?y ch?nh ? ph?n quy?n role n?u mu?n t?t." });

        var enabled = existing == null;

        if (enabled)
            _db.UserPermissions.Add(new UserPermission { UserId = userId, PermissionId = permissionId, CreatedAt = DateTime.Now });
        else
            _db.UserPermissions.Remove(existing!);

        await _db.SaveChangesAsync();
        return Ok(new { success = true, enabled });
    }

    [HttpPut("/api/it/permissions/{id}")]
    public async Task<IActionResult> UpdatePermissionRoles(int id, [FromBody] UpdatePermissionRolesDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var permission = await _db.Permissions
            .Include(p => p.Roles)
            .FirstOrDefaultAsync(p => p.PermissionId == id);
        if (permission == null) return NotFound();

        var roleIds = (dto.RoleIds ?? new List<int>()).Distinct().ToList();
        var roles = await _db.Roles.Where(r => roleIds.Contains(r.RoleId)).ToListAsync();

        permission.Roles.Clear();
        foreach (var role in roles)
        {
            permission.Roles.Add(role);
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: NEWSLETTER SUBSCRIPTIONS
    // ============================================================
    [HttpGet("/api/it/newsletter")]
    public async Task<IActionResult> GetNewsletterSubscriptions()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var subs = await _db.NewsletterSubscriptions
            .Include(s => s.User)
            .Select(s => new
            {
                subId = s.SubId,
                userId = s.UserId,
                fullName = s.User != null ? s.User.FullName : "N/A",
                email = s.User != null ? s.User.Email : "N/A",
                isSubscribed = s.IsSubscribed ?? false
            })
            .ToListAsync();

        return Json(new
        {
            total = subs.Count,
            subscribed = subs.Count(s => s.isSubscribed),
            unsubscribed = subs.Count(s => !s.isSubscribed),
            subscriptions = subs
        });
    }

    [HttpPut("/api/it/newsletter/{id}")]
    public async Task<IActionResult> UpdateNewsletterSub(int id, [FromBody] UpdateNewsletterDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var sub = await _db.NewsletterSubscriptions.FindAsync(id);
        if (sub == null) return NotFound();
        sub.IsSubscribed = dto.IsSubscribed;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: TH?NG K? N?NG CAO (ANALYTICS)
    // ============================================================
    [HttpGet("/api/it/analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        // Ph?n b? user theo ph?ng ban
        var userByDept = await _db.Departments
            .Select(d => new
            {
                department = d.DepartmentName,
                userCount = d.Users.Count()
            })
            .OrderByDescending(d => d.userCount)
            .Take(10)
            .ToListAsync();

        // Ph?n b? kh?a h?c theo category
        var courseByCategory = await _db.Categories
            .Select(c => new
            {
                category = c.CategoryName,
                courseCount = c.Courses.Count()
            })
            .Where(c => c.courseCount > 0)
            .ToListAsync();

        // Tổng số enrollment theo tháng (6 tháng gần nhất)
        var sixMonthsAgo = DateTime.Now.AddMonths(-6);
        var enrollmentByMonth = await _db.Enrollments
            .Where(e => e.EnrollDate >= sixMonthsAgo)
            .GroupBy(e => new { e.EnrollDate!.Value.Year, e.EnrollDate!.Value.Month })
            .Select(g => new
            {
                year = g.Key.Year,
                month = g.Key.Month,
                count = g.Count()
            })
            .OrderBy(g => g.year).ThenBy(g => g.month)
            .ToListAsync();

        // Top 5 khóa học có nhiều enrollment nhất
        var topCourses = await _db.Courses
            .Select(c => new
            {
                title = c.Title,
                enrollments = c.Enrollments.Count()
            })
            .OrderByDescending(c => c.enrollments)
            .Take(5)
            .ToListAsync();

        // Tỷ lệ pass/fail quiz
        var totalExamAttempts = await _db.UserExams.CountAsync(ue => ue.IsFinish == true);
        var passedAttempts = await _db.UserExams
            .Include(ue => ue.Exam)
            .CountAsync(ue => ue.IsFinish == true && ue.Exam != null && ue.Score >= ue.Exam.PassScore);

        return Json(new
        {
            userByDept,
            courseByCategory,
            enrollmentByMonth,
            topCourses,
            examStats = new
            {
                total = totalExamAttempts,
                passed = passedAttempts,
                failed = totalExamAttempts - passedAttempts,
                passRate = totalExamAttempts > 0 ? Math.Round((double)passedAttempts / totalExamAttempts * 100, 1) : 0
            }
        });
    }

    [HttpGet("/api/it/schedules")]
    public async Task<IActionResult> GetSchedules()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var schedules = await _db.OfflineTrainingEvents
            .AsNoTracking()
            .Include(e => e.Course)
            .Include(e => e.AttendanceLogs)
            .OrderBy(e => e.StartTime)
            .Select(e => new
            {
                eventId = e.EventId,
                title = e.Title ?? (e.Course != null ? e.Course.Title : "Lịch học"),
                courseId = e.CourseId,
                courseTitle = e.Course != null ? e.Course.Title : "N/A",
                instructor = e.Instructor,
                location = e.Location,
                startTime = e.StartTime,
                endTime = e.EndTime,
                departmentId = e.DepartmentId,
                attendanceStartTime = e.AttendanceStartTime,
                attendanceEndTime = e.AttendanceEndTime,
                currentParticipants = e.AttendanceLogs.Count(),
                notes = e.Notes,
                shift = e.Shift,
                session = e.Session,
                status = e.Status ?? (e.EndTime < DateTime.Now ? "Đã kết thúc" : (e.StartTime > DateTime.Now ? "Sắp diễn ra" : "Đang diễn ra"))
            })
            .ToListAsync();

        var courseOptions = await _db.Courses
            .AsNoTracking()
            .Where(c => c.Status != "Deleted")
            .OrderBy(c => c.Title)
            .Select(c => new { c.CourseId, c.Title })
            .ToListAsync();

        var deptOptions = await _db.Departments
            .AsNoTracking()
            .OrderBy(d => d.DepartmentName)
            .Select(d => new { d.DepartmentId, d.DepartmentName })
            .ToListAsync();

        return Json(new { schedules, courseOptions, deptOptions });
    }

    [HttpPost("/api/it/schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] ScheduleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        if (dto.CourseId <= 0) return BadRequest(new { error = "Bạn phải chọn khóa học." });
        if (dto.StartTime == null || dto.EndTime == null || dto.EndTime <= dto.StartTime)
            return BadRequest(new { error = "Thời gian lịch học không hợp lệ." });

        var schedule = new OfflineTrainingEvent
        {
            CourseId = dto.CourseId,
            Title = dto.Title?.Trim(),
            Instructor = dto.Instructor?.Trim(),
            Location = dto.Location?.Trim(),
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            DepartmentId = dto.DepartmentId,
            AttendanceStartTime = dto.AttendanceStartTime,
            AttendanceEndTime = dto.AttendanceEndTime,
            Notes = dto.Notes?.Trim(),
            Shift = dto.Shift?.Trim(),
            Session = dto.Session?.Trim() ?? CalculateSession(dto.StartTime),
            Status = dto.Status?.Trim() ?? (dto.EndTime < DateTime.Now ? "Đã kết thúc" : (dto.StartTime > DateTime.Now ? "Sắp diễn ra" : "Đang diễn ra")),
            CreatedBy = int.Parse(HttpContext.Session.GetString("UserID") ?? "1"),
            CreatedAt = DateTime.Now
        };

        _db.OfflineTrainingEvents.Add(schedule);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, eventId = schedule.EventId });
    }

    [HttpPut("/api/it/schedules/{id}")]
    public async Task<IActionResult> UpdateSchedule(int id, [FromBody] ScheduleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var schedule = await _db.OfflineTrainingEvents.FindAsync(id);
        if (schedule == null) return NotFound();
        if (dto.CourseId <= 0) return BadRequest(new { error = "Bạn phải chọn khóa học." });
        if (dto.StartTime == null || dto.EndTime == null || dto.EndTime <= dto.StartTime)
            return BadRequest(new { error = "Thời gian lịch học không hợp lệ." });

        schedule.CourseId = dto.CourseId;
        schedule.Title = dto.Title?.Trim();
        schedule.Instructor = dto.Instructor?.Trim();
        schedule.Location = dto.Location?.Trim();
        schedule.StartTime = dto.StartTime;
        schedule.EndTime = dto.EndTime;
        schedule.DepartmentId = dto.DepartmentId;
        schedule.AttendanceStartTime = dto.AttendanceStartTime;
        schedule.AttendanceEndTime = dto.AttendanceEndTime;
        schedule.Notes = dto.Notes?.Trim();
        schedule.Shift = dto.Shift?.Trim();
        schedule.Session = dto.Session?.Trim() ?? CalculateSession(dto.StartTime);
        schedule.Status = dto.Status?.Trim() ?? (dto.EndTime < DateTime.Now ? "Đã kết thúc" : (dto.StartTime > DateTime.Now ? "Sắp diễn ra" : "Đang diễn ra"));

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/schedules/{id}")]
    public async Task<IActionResult> DeleteSchedule(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var schedule = await _db.OfflineTrainingEvents
            .Include(e => e.AttendanceLogs)
            .FirstOrDefaultAsync(e => e.EventId == id);
        if (schedule == null) return NotFound();

        _db.AttendanceLogs.RemoveRange(schedule.AttendanceLogs);
        _db.OfflineTrainingEvents.Remove(schedule);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ======================================
    // API: ATTENDANCE MANAGEMENT (FOR IT ADMIN)
    // ======================================
    [HttpGet("/api/it/attendance")]
    public async Task<IActionResult> GetAttendance(int? eventId, int? departmentId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        if (eventId.HasValue && eventId.Value > 0)
        {
            var ev = await _db.OfflineTrainingEvents.FindAsync(eventId.Value);
            if (ev == null) return NotFound();

            var targetDeptId = departmentId ?? ev.DepartmentId;

            var userQuery = _db.Users.Where(u => u.Status == "Active");
            if (targetDeptId.HasValue && targetDeptId.Value > 0)
                userQuery = userQuery.Where(u => u.DepartmentId == targetDeptId.Value);

            var users = await userQuery.ToListAsync();
            var logs = await _db.AttendanceLogs.Where(a => a.EventId == eventId.Value).ToListAsync();

            var result = users.Select(u => {
                var log = logs.FirstOrDefault(l => l.UserId == u.UserId);
                return new {
                    userId = u.UserId,
                    eventId = eventId.Value,
                    fullName = u.FullName ?? u.Username,
                    employeeCode = u.EmployeeCode,
                    departmentName = _db.Departments.Where(d => d.DepartmentId == u.DepartmentId).Select(d => d.DepartmentName).FirstOrDefault() ?? "N/A",
                    eventName = ev.Title,
                    checkInTime = log?.CheckInTime,
                    status = log?.AttendanceStatus ?? "Absent",
                    cancelReason = log?.CancelReason
                };
            }).ToList();

            return Json(result);
        }
        else
        {
            var query = _db.AttendanceLogs
                .Include(a => a.User)
                    .ThenInclude(u => u.Department)
                .Include(a => a.Event)
                .AsQueryable();

            if (departmentId.HasValue && departmentId.Value > 0)
                query = query.Where(a => a.User.DepartmentId == departmentId.Value);

            var logs = await query
                .OrderByDescending(a => a.CheckInTime)
                .Take(200)
                .Select(a => new {
                    userId = a.UserId,
                    eventId = a.EventId,
                    fullName = a.User.FullName,
                    employeeCode = a.User.EmployeeCode,
                    departmentName = a.User.Department != null ? a.User.Department.DepartmentName : "N/A",
                    eventName = a.Event.Title,
                    checkInTime = a.CheckInTime,
                    status = a.AttendanceStatus,
                    cancelReason = a.CancelReason
                })
                .ToListAsync();

            return Json(logs);
        }
    }

    [HttpPost("/api/it/attendance/update")]
    public async Task<IActionResult> UpdateAttendanceStatus([FromBody] ItUpdateAttendanceDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var schedule = await _db.OfflineTrainingEvents.FindAsync(dto.EventId);
        if (schedule == null) return NotFound(new { error = "Sự kiện không tồn tại." });

        var log = await _db.AttendanceLogs.FirstOrDefaultAsync(a => a.EventId == dto.EventId && a.UserId == dto.UserId);

        if (dto.Status == "Present" && schedule.AttendanceEndTime.HasValue && DateTime.Now > schedule.AttendanceEndTime.Value)
        {
            if (log == null || log.AttendanceStatus != "Present")
            {
                return BadRequest(new { error = "Đã quá thời gian điểm danh, không thể đánh dấu Có mặt." });
            }
        }

        if (log == null)
        {
            log = new AttendanceLog {
                EventId = dto.EventId,
                UserId = dto.UserId,
                Status = dto.Status == "Present",
                AttendanceStatus = dto.Status,
                CheckInTime = dto.Status == "Present" ? DateTime.Now : null
            };
            _db.AttendanceLogs.Add(log);
        }
        else
        {
            log.Status = dto.Status == "Present";
            log.AttendanceStatus = dto.Status;
            if (dto.Status == "Present" && log.CheckInTime == null) log.CheckInTime = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/it/attendance/bulk-absent")]
    public async Task<IActionResult> BulkMarkAbsent([FromBody] ItBulkAbsentDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var logs = await _db.AttendanceLogs
            .Where(a => a.EventId == dto.EventId && (a.AttendanceStatus == "Registered" || a.AttendanceStatus == null))
            .ToListAsync();

        foreach (var log in logs)
        {
            log.Status = false;
            log.AttendanceStatus = "Absent";
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, count = logs.Count });
    }

    public class ItBulkAbsentDto
    {
        public int EventId { get; set; }
    }

    public class ItUpdateAttendanceDto
    {
        public int EventId { get; set; }
        public int UserId { get; set; }
        public string Status { get; set; } = "Absent";
    }

    // ============================================================
    // API: LẦN ĐĂNG NHẬP GẦN NHẤT / ACTIVE USERS
    // ============================================================
    [HttpGet("/api/it/users/active-stats")]
    public async Task<IActionResult> GetActiveUserStats()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var thirtyDaysAgo = DateTime.Now.AddDays(-30);
        var recentActive = await _db.Users
            .Where(u => u.LastLogin >= thirtyDaysAgo && u.Status == "Active")
            .CountAsync();
        var neverLoggedIn = await _db.Users.CountAsync(u => u.LastLogin == null && u.Status == "Active");

        return Json(new
        {
            recentlyActive = recentActive,
            neverLoggedIn,
            totalActive = await _db.Users.CountAsync(u => u.Status == "Active")
        });
    }

    // ============================================================
    // API: JOB TITLE MANAGEMENT (CRUD)
    // ============================================================
    [HttpGet("/api/it/jobtitles")]
    public async Task<IActionResult> GetJobTitles()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var titles = await _db.JobTitles
            .Select(j => new
            {
                jobTitleId = j.JobTitleId,
                titleName = j.TitleName,
                gradeLevel = j.GradeLevel,
                userCount = j.Users.Count()
            })
            .OrderBy(j => j.gradeLevel)
            .ThenBy(j => j.titleName)
            .ToListAsync();

        return Json(titles);
    }

    [HttpPost("/api/it/jobtitles")]
    public async Task<IActionResult> CreateJobTitle([FromBody] JobTitleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.TitleName))
            return BadRequest(new { error = "Ten chuc danh khong duoc trong." });

        if (await _db.JobTitles.AnyAsync(j => j.TitleName == dto.TitleName))
            return BadRequest(new { error = "Chuc danh nay da ton tai." });

        var jobTitle = new JobTitle { TitleName = dto.TitleName, GradeLevel = dto.GradeLevel };
        _db.JobTitles.Add(jobTitle);
        await _db.SaveChangesAsync();

        var uid = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        _db.AuditLogs.Add(new AuditLog { UserId = uid, ActionType = "INSERT", TableName = "JobTitles", Description = "Tao chuc danh: " + dto.TitleName, Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, jobTitleId = jobTitle.JobTitleId });
    }

    [HttpPut("/api/it/jobtitles/{id}")]
    public async Task<IActionResult> UpdateJobTitle(int id, [FromBody] JobTitleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var jt = await _db.JobTitles.FindAsync(id);
        if (jt == null) return NotFound(new { error = "Khong tim thay chuc danh." });

        if (!string.IsNullOrWhiteSpace(dto.TitleName)) jt.TitleName = dto.TitleName;
        jt.GradeLevel = dto.GradeLevel;
        await _db.SaveChangesAsync();

        var uid = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        _db.AuditLogs.Add(new AuditLog { UserId = uid, ActionType = "UPDATE", TableName = "JobTitles", Description = "Cap nhat chuc danh ID " + id, Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpDelete("/api/it/jobtitles/{id}")]
    public async Task<IActionResult> DeleteJobTitle(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var jt = await _db.JobTitles.Include(j => j.Users).FirstOrDefaultAsync(j => j.JobTitleId == id);
        if (jt == null) return NotFound(new { error = "Khong tim thay chuc danh." });

        if (jt.Users.Any())
            return BadRequest(new { error = "Khong the xoa! Con " + jt.Users.Count + " nhan vien dung chuc danh nay." });

        _db.JobTitles.Remove(jt);
        await _db.SaveChangesAsync();

        var uid = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        _db.AuditLogs.Add(new AuditLog { UserId = uid, ActionType = "DELETE", TableName = "JobTitles", Description = "Xoa chuc danh: " + jt.TitleName + " (ID " + id + ")", Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // ============================================================
    // API: EXPORT EXCEL
    // ============================================================

    [HttpGet("/api/it/export/users")]
    public async Task<IActionResult> ExportUsers(string? search, string? status)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var query = _db.Users.Include(u => u.Department).Include(u => u.JobTitle).Include(u => u.Roles).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(u => (u.FullName != null && u.FullName.Contains(search))
                || (u.Username != null && u.Username.Contains(search))
                || (u.Email != null && u.Email.Contains(search))
                || (u.EmployeeCode != null && u.EmployeeCode.Contains(search)));
        if (!string.IsNullOrEmpty(status)) query = query.Where(u => u.Status == status);

        var users = await query.OrderBy(u => u.Department != null ? u.Department.DepartmentName : "").ThenBy(u => u.FullName).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Nhan Vien");

        ws.Cell(1, 1).Value = "DANH SACH NHAN VIEN - " + DateTime.Now.ToString("dd/MM/yyyy");
        ws.Range(1, 1, 1, 10).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

        var hdrs = new[] { "STT", "Ma NV", "Ho va Ten", "Ten Dang Nhap", "Email", "Phong Ban", "Chuc Danh", "Roles", "Trang Thai", "Dang Nhap Cuoi" };
        for (int i = 0; i < hdrs.Length; i++)
        {
            ws.Cell(2, i + 1).Value = hdrs[i];
            ws.Cell(2, i + 1).Style.Font.Bold = true;
            ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
            ws.Cell(2, i + 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(2, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < users.Count; i++)
        {
            var u = users[i]; var row = i + 3;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = u.EmployeeCode ?? "";
            ws.Cell(row, 3).Value = u.FullName ?? "";
            ws.Cell(row, 4).Value = u.Username ?? "";
            ws.Cell(row, 5).Value = u.Email ?? "";
            ws.Cell(row, 6).Value = u.Department?.DepartmentName ?? "";
            ws.Cell(row, 7).Value = u.JobTitle?.TitleName ?? "";
            ws.Cell(row, 8).Value = string.Join(", ", u.Roles.Select(r => r.RoleName ?? ""));
            ws.Cell(row, 9).Value = u.Status ?? "";
            ws.Cell(row, 10).Value = u.LastLogin.HasValue ? u.LastLogin.Value.ToString("dd/MM/yyyy HH:mm") : "Chua dang nhap";
            if (i % 2 == 1) ws.Range(row, 1, row, hdrs.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f4ff");
            ws.Cell(row, 9).Style.Font.FontColor = u.Status == "Active" ? XLColor.FromHtml("#16a34a") : XLColor.FromHtml("#dc2626");
        }

        ws.Columns().AdjustToContents();
        if (users.Count > 0) {
            ws.Range(2, 1, users.Count + 2, hdrs.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(2, 1, users.Count + 2, hdrs.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "DanhSach_NhanVien_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
    }

    [HttpGet("/api/it/export/training-report")]
    public async Task<IActionResult> ExportTrainingReport()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var data = await _db.TrainingAssignments
            .Include(a => a.User).ThenInclude(u => u!.Department)
            .Include(a => a.User).ThenInclude(u => u!.JobTitle)
            .Include(a => a.Course)
            .OrderBy(a => a.User != null ? a.User.Department!.DepartmentName : "")
            .ThenBy(a => a.User != null ? a.User.FullName : "")
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Dao Tao");

        ws.Cell(1, 1).Value = "BAO CAO DAO TAO - " + DateTime.Now.ToString("dd/MM/yyyy");
        ws.Range(1, 1, 1, 10).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

        var hdrs = new[] { "STT", "Ma NV", "Ho va Ten", "Phong Ban", "Chuc Danh", "Khoa Hoc", "Ngay Giao", "Han Hoan Thanh", "Trang Thai", "Uu Tien" };
        for (int i = 0; i < hdrs.Length; i++)
        {
            ws.Cell(2, i + 1).Value = hdrs[i];
            ws.Cell(2, i + 1).Style.Font.Bold = true;
            ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
            ws.Cell(2, i + 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(2, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < data.Count; i++)
        {
            var a = data[i]; var row = i + 3;
            var isOverdue = a.DueDate.HasValue && a.DueDate.Value < DateTime.Now;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = a.User?.EmployeeCode ?? "";
            ws.Cell(row, 3).Value = a.User?.FullName ?? "";
            ws.Cell(row, 4).Value = a.User?.Department?.DepartmentName ?? "";
            ws.Cell(row, 5).Value = a.User?.JobTitle?.TitleName ?? "";
            ws.Cell(row, 6).Value = a.Course?.Title ?? "";
            ws.Cell(row, 7).Value = a.AssignedDate.HasValue ? a.AssignedDate.Value.ToString("dd/MM/yyyy") : "";
            ws.Cell(row, 8).Value = a.DueDate.HasValue ? a.DueDate.Value.ToString("dd/MM/yyyy") : "";
            ws.Cell(row, 9).Value = isOverdue ? "Qua Han" : (a.DueDate.HasValue ? "Chua Hoan Thanh" : "Chua Co Han");
            ws.Cell(row, 10).Value = a.Priority ?? "";
            if (i % 2 == 1) ws.Range(row, 1, row, hdrs.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#eff6ff");
            var sc = isOverdue ? XLColor.FromHtml("#dc2626") : XLColor.FromHtml("#9ca3af");
            ws.Cell(row, 9).Style.Font.FontColor = sc;
            if (isOverdue) { ws.Cell(row, 8).Style.Font.FontColor = XLColor.FromHtml("#dc2626"); ws.Cell(row, 8).Style.Font.Bold = true; }
        }

        ws.Columns().AdjustToContents();
        if (data.Count > 0) {
            ws.Range(2, 1, data.Count + 2, hdrs.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(2, 1, data.Count + 2, hdrs.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "BaoCao_DaoTao_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
    }

    [HttpGet("/api/it/export/exam-results")]
    public async Task<IActionResult> ExportExamResults()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var results = await _db.UserExams
            .Include(ue => ue.User).ThenInclude(u => u!.Department)
            .Include(ue => ue.Exam).ThenInclude(e => e!.Course)
            .Where(ue => ue.IsFinish == true)
            .OrderBy(ue => ue.User != null ? ue.User.Department!.DepartmentName : "")
            .ThenBy(ue => ue.User != null ? ue.User.FullName : "")
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ket Qua Thi");

        ws.Cell(1, 1).Value = "KET QUA BAI KIEM TRA - " + DateTime.Now.ToString("dd/MM/yyyy");
        ws.Range(1, 1, 1, 9).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#7c2d12");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

        var hdrs = new[] { "STT", "Ma NV", "Ho va Ten", "Phong Ban", "Khoa Hoc", "Bai Kiem Tra", "Diem So", "Diem Do", "Ket Qua" };
        for (int i = 0; i < hdrs.Length; i++)
        {
            ws.Cell(2, i + 1).Value = hdrs[i];
            ws.Cell(2, i + 1).Style.Font.Bold = true;
            ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#9a3412");
            ws.Cell(2, i + 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(2, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < results.Count; i++)
        {
            var ue = results[i]; var row = i + 3;
            var passed = ue.Exam != null && ue.Score >= ue.Exam.PassScore;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = ue.User?.EmployeeCode ?? "";
            ws.Cell(row, 3).Value = ue.User?.FullName ?? "";
            ws.Cell(row, 4).Value = ue.User?.Department?.DepartmentName ?? "";
            ws.Cell(row, 5).Value = ue.Exam?.Course?.Title ?? "";
            ws.Cell(row, 6).Value = ue.Exam?.ExamTitle ?? "";
            ws.Cell(row, 7).Value = ue.Score.HasValue ? (double)ue.Score.Value : 0;
            ws.Cell(row, 8).Value = (ue.Exam != null && ue.Exam.PassScore.HasValue) ? (double)ue.Exam.PassScore.Value : 0;
            ws.Cell(row, 9).Value = passed ? "DAT" : "KHONG DAT";
            if (i % 2 == 1) ws.Range(row, 1, row, hdrs.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff7ed");
            ws.Cell(row, 9).Style.Font.Bold = true;
            ws.Cell(row, 9).Style.Font.FontColor = passed ? XLColor.FromHtml("#16a34a") : XLColor.FromHtml("#dc2626");
        }

        ws.Columns().AdjustToContents();
        if (results.Count > 0) {
            ws.Range(2, 1, results.Count + 2, hdrs.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(2, 1, results.Count + 2, hdrs.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "KetQua_BaiKiemTra_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
    }

    [HttpPost("/api/it/generate-quiz-ai")]
    public async Task<IActionResult> GenerateQuizAI([FromBody] PromptDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var topic = dto.Prompt?.Trim() ?? "Bài tập tổng quát";
        var generatedData = await _aiService.GenerateQuizAsync(topic);

        return Ok(generatedData);
    }

    [HttpPost("/api/it/generate-module-ai")]
    public async Task<IActionResult> GenerateModuleAI([FromBody] PromptDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;
        var result = await _aiService.GenerateModuleAsync(dto.Prompt ?? "Chương mới");
        return Ok(result);
    }

    [HttpPost("/api/it/generate-lesson-ai")]
    public async Task<IActionResult> GenerateLessonAI([FromBody] PromptDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;
        var result = await _aiService.GenerateLessonAsync(dto.Prompt ?? "Bài giảng mới");
        return Ok(result);
    }

    [HttpPost("/api/it/generate-quiz-from-file")]
    public async Task<IActionResult> GenerateQuizFromFileAI([FromBody] PromptFileDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        if (string.IsNullOrEmpty(dto.Base64Data)) return BadRequest("File data is required");
        
        var generatedData = await _aiService.GenerateQuizFromDocumentAsync(dto.Base64Data, dto.MimeType);
        return Ok(generatedData);
    }

    private string CalculateSession(DateTime? startTime)
    {
        if (!startTime.HasValue) return "Sáng";
        var hour = startTime.Value.Hour;
        if (hour >= 5 && hour < 12) return "Sáng";
        if (hour >= 12 && hour < 18) return "Chiều";
        return "Tối";
    }
    // ============================================================
    // API: APPROVALS (PHÊ DUYỆT TÀI LIỆU)
    // ============================================================
    [HttpGet("/api/it/approvals")]
    public async Task<IActionResult> GetApprovals()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var docs = await (from d in _db.DocumentLibraries
                          join c in _db.Courses on d.CourseId equals c.CourseId into cj
                          from c in cj.DefaultIfEmpty()
                          join m in _db.CourseModules on d.ModuleId equals m.ModuleId into mj
                          from m in mj.DefaultIfEmpty()
                          join l in _db.Lessons on d.LessonId equals l.LessonId into lj
                          from l in lj.DefaultIfEmpty()
                          join e in _db.Exams on d.ExamId equals e.ExamId into ej
                          from e in ej.DefaultIfEmpty()
                          orderby d.Id descending
                          select new
                          {
                              id = d.Id,
                              title = d.Title,
                              filePath = d.FilePath,
                              createdBy = d.CreatedBy,
                              approvalStatus = d.ApprovalStatus,
                              rejectionReason = d.RejectionReason,
                              courseName = c != null ? c.Title : null,
                              moduleName = m != null ? m.Title : null,
                              lessonName = l != null ? l.Title : null,
                              examName = e != null ? e.ExamTitle : null,
                              newModuleName = d.NewModuleName,
                              newLessonName = d.NewLessonName,
                              newExamName = d.NewExamName,
                              pendingData = d.PendingData,
                              targetType = d.TargetType
                          }).ToListAsync();

        return Json(docs);
    }

    [HttpPost("/api/it/approvals/{id}/approve")]
    public async Task<IActionResult> ApproveDocument(int id)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var doc = await _db.DocumentLibraries.FindAsync(id);
        if (doc == null) return NotFound();
        if (doc.ApprovalStatus == "Approved") return BadRequest(new { error = "Tài liệu này đã được phê duyệt thành công. Không thể nhấp lại để tránh tạo dữ liệu trùng lặp." });

        // Create new content if requested using PendingData
        if (!string.IsNullOrWhiteSpace(doc.PendingData))
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            if (doc.TargetType == "module")
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<HrCreateModuleDto>(doc.PendingData, options);
                if (data != null)
                {
                    var newMod = new CourseModule { 
                        Title = data.Title, 
                        CourseId = doc.CourseId,
                        Level = data.Level,
                        SortOrder = (_db.CourseModules.Where(m => m.CourseId == doc.CourseId).Max(m => (int?)m.SortOrder) ?? 0) + 1
                    };
                    _db.CourseModules.Add(newMod);
                    await _db.SaveChangesAsync();
                    doc.ModuleId = newMod.ModuleId;
                }
            }
            else if (doc.TargetType == "lesson")
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<PendingLessonData>(doc.PendingData, options);
                if (data != null)
                {
                    // If creating a new module for this lesson
                    if (!string.IsNullOrWhiteSpace(data.NewModuleName))
                    {
                        var newMod = new CourseModule { 
                            Title = data.NewModuleName, 
                            CourseId = doc.CourseId,
                            SortOrder = (_db.CourseModules.Where(m => m.CourseId == doc.CourseId).Max(m => (int?)m.SortOrder) ?? 0) + 1
                        };
                        _db.CourseModules.Add(newMod);
                        await _db.SaveChangesAsync();
                        doc.ModuleId = newMod.ModuleId;
                    }
                    else if (data.ModuleId.HasValue)
                    {
                        doc.ModuleId = data.ModuleId;
                    }

                    var newLesson = new Lesson { 
                        Title = data.Title, 
                        ModuleId = doc.ModuleId,
                        ContentType = data.ContentType,
                        ContentBody = data.ContentBody,
                        VideoUrl = data.VideoUrl,
                        Level = data.Level,
                        SortOrder = (_db.Lessons.Where(l => l.ModuleId == doc.ModuleId).Max(l => (int?)l.SortOrder) ?? 0) + 1
                    };
                    _db.Lessons.Add(newLesson);
                    await _db.SaveChangesAsync();
                    doc.LessonId = newLesson.LessonId;

                    // Link the file as an attachment
                    var attachment = new LessonAttachment {
                        LessonId = newLesson.LessonId,
                        FileName = doc.Title ?? "Attachment",
                        FilePath = doc.FilePath ?? ""
                    };
                    _db.LessonAttachments.Add(attachment);
                }
            }
            else if (doc.TargetType == "quiz")
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<PendingExamData>(doc.PendingData, options);
                if (data != null)
                {
                    var newExam = new Exam { 
                        ExamTitle = data.ExamTitle, 
                        CourseId = doc.CourseId,
                        DurationMinutes = data.DurationMinutes,
                        PassScore = data.PassScore,
                        MaxAttempts = data.MaxAttempts,
                        Level = data.Level
                    };
                    _db.Exams.Add(newExam);
                    await _db.SaveChangesAsync();
                    doc.ExamId = newExam.ExamId;

                    if (data.Questions != null)
                    {
                        foreach (var q in data.Questions)
                        {
                            // 1. Create the base question in QuestionBank
                            var qb = new QuestionBank {
                                QuestionText = q.QuestionText,
                                Difficulty = data.Level?.ToString() ?? "1"
                            };
                            _db.QuestionBanks.Add(qb);
                            await _db.SaveChangesAsync();

                            // 2. Link QuestionBank to this Exam
                            var eq = new ExamQuestion {
                                ExamId = newExam.ExamId,
                                QuestionId = qb.QuestionId,
                                Points = 10 // Default points
                            };
                            _db.ExamQuestions.Add(eq);

                            // 3. Add Options
                            if (q.Options != null)
                            {
                                foreach (var opt in q.Options)
                                {
                                    _db.QuestionOptions.Add(new QuestionOption {
                                        QuestionId = qb.QuestionId,
                                        OptionText = opt.OptionText,
                                        IsCorrect = opt.IsCorrect
                                    });
                                }
                            }
                        }
                        await _db.SaveChangesAsync();
                    }
                }
            }
        }
        else 
        {
            // Fallback to simple name creation if PendingData is missing (for backward compatibility during dev)
            if (!string.IsNullOrWhiteSpace(doc.NewModuleName))
            {
                var newMod = new CourseModule { Title = doc.NewModuleName, CourseId = doc.CourseId };
                _db.CourseModules.Add(newMod); await _db.SaveChangesAsync(); doc.ModuleId = newMod.ModuleId;
            }
            if (!string.IsNullOrWhiteSpace(doc.NewLessonName))
            {
                var newLesson = new Lesson { Title = doc.NewLessonName, ModuleId = doc.ModuleId, ContentType = "Document" };
                _db.Lessons.Add(newLesson); await _db.SaveChangesAsync(); doc.LessonId = newLesson.LessonId;
                _db.LessonAttachments.Add(new LessonAttachment { LessonId = newLesson.LessonId, FileName = doc.Title ?? "Doc", FilePath = doc.FilePath ?? "" });
            }
            if (!string.IsNullOrWhiteSpace(doc.NewExamName))
            {
                var newExam = new Exam { ExamTitle = doc.NewExamName, CourseId = doc.CourseId };
                _db.Exams.Add(newExam); await _db.SaveChangesAsync(); doc.ExamId = newExam.ExamId;
            }
        }

        doc.ApprovalStatus = "Approved";
        doc.ApprovedBy = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        doc.ApprovedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/it/approvals/{id}/reject")]
    public async Task<IActionResult> RejectDocument(int id, [FromBody] RejectDocumentDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var doc = await _db.DocumentLibraries.FindAsync(id);
        if (doc == null) return NotFound();
        if (doc.ApprovalStatus == "Approved") return BadRequest(new { error = "Tài liệu này đã được phê duyệt thành công. Không thể thu hồi bằng nút Hủy." });

        doc.ApprovalStatus = "Rejected";
        doc.RejectionReason = dto?.Reason;
        doc.ApprovedBy = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        doc.ApprovedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
// DTOs
public class RejectDocumentDto
{
    public string? Reason { get; set; }
}

public class CreateUserDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? EmployeeCode { get; set; }
    public int? DepartmentId { get; set; }
    public bool? IsItAdmin { get; set; }
}

public class UpdateUserDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Status { get; set; }
    public int? DepartmentId { get; set; }
    public bool? IsItAdmin { get; set; }
    public string? NewPassword { get; set; }
}

public class ItCreateCourseDto
{
    public string CourseCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? Level { get; set; }
    public int? CategoryId { get; set; }
    public string? Status { get; set; }
    public bool IsMandatory { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<int>? TargetDepartmentIds { get; set; }
}

public class CreateDepartmentDto
{
    public string DepartmentName { get; set; } = "";
}

public class AssignDepartmentManagerDto
{
    public int UserId { get; set; }
}

public class ItCreateModuleDto
{
    public string Title { get; set; } = "";
    public int? Level { get; set; }
    public int? SortOrder { get; set; }
    public int? TargetDepartmentId { get; set; }
}

public class ItCreateLessonDto
{
    public string Title { get; set; } = "";
    public string ContentType { get; set; } = "Video";
    public string? VideoUrl { get; set; }
    public string? ContentBody { get; set; }
    public int? Level { get; set; }
    public int? SortOrder { get; set; }
}

public class ItCreateExamDto
{
    public string ExamTitle { get; set; } = "";
    public int DurationMinutes { get; set; } = 30;
    public decimal PassScore { get; set; } = 50;
    public int? Level { get; set; }
    /// <summary>Số lần làm tối đa (null = không giới hạn)</summary>
    public int? MaxAttempts { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>Phòng ban được làm bài kiểm tra (null = tất cả)</summary>
    public int? TargetDepartmentId { get; set; }
    /// <summary>Danh s?ch c?u h?i do AI t?o (n?u c?)</summary>
    public List<ItCreateQuestionDto>? AiQuestions { get; set; }
}

public class ItCreateQuestionDto
{
    public string QuestionText { get; set; } = "";
    public decimal Points { get; set; } = 10;
    public List<ItCreateOptionDto>? Options { get; set; }
}

public class ItCreateOptionDto
{
    public string OptionText { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public class CategoryDto
{
    public string CategoryName { get; set; } = "";
    public int? OwnerDeptId { get; set; }
}

public class FaqDto
{
    public string Question { get; set; } = "";
    public string? Answer { get; set; }
    public int? CategoryId { get; set; }
}

public class CreateBackupDto
{
    public string BackupType { get; set; } = "Manual";
}

public class UpdateNewsletterDto
{
    public bool IsSubscribed { get; set; }
}

public class UpdatePermissionRolesDto
{
    public List<int>? RoleIds { get; set; }
}

public class LessonAttachmentLinkDto
{
    public string? FileName { get; set; }
    public string Url { get; set; } = "";
}

public class ScheduleDto
{
    public int CourseId { get; set; }
    public string? Title { get; set; }
    public string? Instructor { get; set; }
    public string? Location { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? DepartmentId { get; set; }
    public DateTime? AttendanceStartTime { get; set; }
    public DateTime? AttendanceEndTime { get; set; }
    public string? Notes { get; set; }
    public string? Shift { get; set; }
    public string? Session { get; set; }
    public string? Status { get; set; }
}

public class JobTitleDto
{
    public string TitleName { get; set; } = "";
    public int? GradeLevel { get; set; }
}

public class PendingLessonData
{
    public string Title { get; set; } = "";
    public int? ModuleId { get; set; }
    public string? NewModuleName { get; set; }
    public string ContentType { get; set; } = "Document";
    public string? ContentBody { get; set; }
    public string? VideoUrl { get; set; }
    public int? Level { get; set; }
}

public class PendingExamData
{
    public string ExamTitle { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal PassScore { get; set; }
    public int? MaxAttempts { get; set; }
    public int? Level { get; set; }
    public List<PendingQuestionData>? Questions { get; set; }
}

public class PendingQuestionData
{
    public string QuestionText { get; set; } = "";
    public string QuestionType { get; set; } = "MultipleChoice";
    public List<PendingOptionData>? Options { get; set; }
}

public class PendingOptionData
{
    public string OptionText { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public class PromptFileDto
{
    public string Base64Data { get; set; } = "";
    public string MimeType { get; set; } = "application/pdf";
}






