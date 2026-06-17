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

public partial class ITController : Controller
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
        // Priority 3: URL Link (Video or Document)
        else if (hasVideoUrl)
        {
            DeleteUploadIfOwned(lesson.VideoUrl);
            lesson.VideoUrl = request.VideoUrl!.Trim();
            lesson.ContentType = request.ContentType ?? "Video";
            lesson.ContentBody = null;
        }
        // Priority 4: AI / Text Body
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
        new("jobtitles.manage", "Quản lý chức danh", "Nhân sự", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR"),
        new("schedules.manage", "Quản lý lịch học", "Đào tạo offline", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("attendance.manage", "Quản lý điểm danh", "Đào tạo offline", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("analytics.view", "Xem phân tích nâng cao", "Phân tích & báo cáo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("reports.export", "Xuất báo cáo", "Phân tích & báo cáo", "IT", "IT Admin", "Administrator", "Admin", "HR Manager", "HR", "Manager", "Dept Admin"),
        new("auditlogs.view", "Xem nhật ký hoạt động", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin"),
        new("backup.manage", "Quản lý backup", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin"),
        new("permissions.manage", "Phân quyền hệ thống", "Bảo mật & hệ thống", "IT", "IT Admin", "Administrator", "Admin"),
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

}
