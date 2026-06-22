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
    // API: Reset và Gieo dữ liệu mẫu thực tế
    // API: Thống kê tổng quan hệ thống
    // API: Danh s?ch users (ph?n trang, t?m ki?m)
    // API: Tạo user mới
    // API: Cập nhật user
    // API: Xóa (soft delete) user
    // API: Danh sách roles
    // API: Gán role cho user
    // API: System settings
    // API: Cập nhật setting
    // API: Audit logs
    // API: Danh sách departments
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
        if (string.IsNullOrWhiteSpace(dto.CourseCode)) return BadRequest(new { error = "Mã khóa học là bắt buộc." });

        var normalizedTitle = dto.Title.Trim().ToLower();
        var normalizedCode = dto.CourseCode.Trim().ToUpper();
        if (await _db.Courses.AnyAsync(c => c.Title != null && c.Title.ToLower() == normalizedTitle))
            return BadRequest(new { error = $"Khóa học {dto.Title.Trim()} đã tồn tại, không thể thêm." });
        if (await _db.Courses.AnyAsync(c => c.CourseCode != null && c.CourseCode.ToUpper() == normalizedCode))
            return BadRequest(new { error = $"Mã khóa học {dto.CourseCode.Trim()} đã tồn tại, không thể thêm." });

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
            return BadRequest(new { error = $"Khóa học {dto.Title!.Trim()} đã tồn tại, không thể cập nhật trùng." });
        if (!string.IsNullOrWhiteSpace(normalizedCode) &&
            await _db.Courses.AnyAsync(c => c.CourseId != id && c.CourseCode != null && c.CourseCode.ToUpper() == normalizedCode))
            return BadRequest(new { error = $"Mã khóa học {dto.CourseCode!.Trim()} đã tồn tại, không thể cập nhật trùng." });

        course.CourseCode = dto.CourseCode ?? course.CourseCode;
        course.Title = dto.Title ?? course.Title;
        course.Description = dto.Description;
        course.Level = NormalizeLevel(dto.Level);
        course.CategoryId = dto.CategoryId;
        course.Status = dto.Status ?? course.Status;
        course.IsMandatory = dto.IsMandatory;
        course.StartDate = dto.StartDate;
        course.EndDate = dto.EndDate;
        
        var assignments = await _db.TrainingAssignments.Where(ta => ta.CourseId == id).ToListAsync();
        foreach (var ta in assignments)
        {
            ta.DueDate = dto.EndDate;
        }
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
            return BadRequest(new { error = "Bài video cần chọn file video hoặc nhập link video." });
        if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody))
            return BadRequest(new { error = "Bài AI / văn bản cần có nội dung." });

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
            return BadRequest(new { error = "Bài video cần chọn file video hoặc nhập link video." });
        if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody))
            return BadRequest(new { error = "Bài AI / văn bản cần có nội dung." });

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
    [HttpPost("/api/hr/courses/{courseId}/exams")]
    public async Task<IActionResult> CreateExam(int courseId, [FromBody] ItCreateExamDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        if (string.IsNullOrWhiteSpace(dto.ExamTitle))
            return BadRequest(new { error = "Tên quiz là bắt buộc." });

        // Check for duplicate title before transaction
        if (await _db.Exams.AnyAsync(e => e.ExamTitle != null && e.ExamTitle.ToLower() == dto.ExamTitle.Trim().ToLower()))
        {
            return BadRequest(new { error = $"Không thể tạo: Tiêu đề quiz '{dto.ExamTitle.Trim()}' đã tồn tại trong hệ thống. Vui lòng chọn tên khác." });
        }

        try
        {
            int newExamId = 0;

            // Use execution strategy to support retry policy + manual transactions
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    int? effectiveCourseId = courseId > 0 ? courseId : null;

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
                    newExamId = exam.ExamId;

                    // Handle AI-generated questions or bundled questions
                    if (dto.AiQuestions != null && dto.AiQuestions.Any())
                    {
                        foreach (var qDto in dto.AiQuestions)
                        {
                            if (string.IsNullOrWhiteSpace(qDto.QuestionText)) continue;

                            var q = new QuestionBank
                            {
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
                                    _db.QuestionOptions.Add(new QuestionOption
                                    {
                                        QuestionId = q.QuestionId,
                                        OptionText = opt.OptionText.Trim(),
                                        IsCorrect = opt.IsCorrect
                                    });
                                }
                            }

                            _db.ExamQuestions.Add(new ExamQuestion
                            {
                                ExamId = exam.ExamId,
                                QuestionId = q.QuestionId,
                                Points = qDto.Points
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            return Ok(new { success = true, examId = newExamId });
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
            return BadRequest("Lỗi lưu câu hỏi");
        }
    }
    [HttpPost("/api/it/exams/{examId}/questions/batch")]
    public async Task<IActionResult> SaveExamQuestionsBatch(int examId, [FromBody] List<ItCreateQuestionDto>? questions)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        if (questions == null || questions.Count == 0)
            return BadRequest(new { error = "Danh sách câu hỏi trống." });

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            if (!await _db.Exams.AnyAsync(e => e.ExamId == examId))
                return NotFound(new { error = "Không tìm thấy bài kiểm tra." });

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
            return StatusCode(500, new { error = "Lỗi lưu bộ câu hỏi: " + ex.Message });
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
            return BadRequest(new { error = $"Chương {dto.Title.Trim()} đã tồn tại trong khóa học này." });
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
                return BadRequest(new { error = $"Bài học {dto.Title.Trim()} đã tồn tại trong chương này." });

            if (IsVideoRequest(dto) && string.IsNullOrWhiteSpace(lesson.VideoUrl) && (dto.VideoFile == null || dto.VideoFile.Length == 0) && string.IsNullOrWhiteSpace(dto.VideoUrl))
                return BadRequest(new { error = "Bai video can chon file video hoac nhap link video." });
            if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody) && string.IsNullOrWhiteSpace(lesson.ContentBody))
                return BadRequest(new { error = "Bai AI / van ban can co noi dung." });

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
            return BadRequest(new { error = $"Quiz {dto.ExamTitle.Trim()} đã tồn tại trong khóa học này." });

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
    [HttpPost("/api/hr/exams/generate")]
    public async Task<IActionResult> GenerateExamWithAI([FromBody] PromptDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;
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
    [HttpPost("/api/hr/exams/generate-from-file")]
    public async Task<IActionResult> GenerateExamFromFile([FromBody] PromptFileDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;
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

        if (string.IsNullOrWhiteSpace(dto.Question)) return BadRequest(new { error = "Câu hỏi không được trống." });

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
    // ============================================================
    // API: NEWSLETTER SUBSCRIPTIONS
    // ============================================================
    // ============================================================
    // API: TH?NG K? N?NG CAO (ANALYTICS)
    // ============================================================
    // ======================================
    // API: ATTENDANCE MANAGEMENT (FOR IT ADMIN)
    // ======================================
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
    // ============================================================
    // API: JOB TITLE MANAGEMENT (CRUD)
    // ============================================================
    // ============================================================
    // API: EXPORT EXCEL
    // ============================================================
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

    [HttpGet("/api/it/exams-with-stats")]
    public async Task<IActionResult> GetExamsWithStats()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var exams = await _db.Exams
            .Include(e => e.Course)
            .Include(e => e.ExamQuestions)
            .Include(e => e.UserExams)
            .OrderByDescending(e => e.ExamId)
            .Select(e => new
            {
                examId = e.ExamId,
                examTitle = e.ExamTitle,
                durationMinutes = e.DurationMinutes ?? 30,
                passScore = e.PassScore ?? 50m,
                maxAttempts = e.MaxAttempts,
                courseId = e.CourseId,
                courseTitle = e.Course != null ? e.Course.Title : null,
                questionsCount = e.ExamQuestions.Count,
                passedCount = e.UserExams.Count(ue => ue.IsFinish == true && (ue.Score ?? 0) >= (e.PassScore ?? 50m)),
                failedCount = e.UserExams.Count(ue => ue.IsFinish == true && (ue.Score ?? 0) < (e.PassScore ?? 50m)),
                startDate = e.StartDate,
                endDate = e.EndDate,
                targetDepartmentId = e.TargetDepartmentId,
                level = e.Level
            })
            .ToListAsync();

        return Json(exams);
    }

    [HttpGet("/api/it/questions-pool")]
    public async Task<IActionResult> GetQuestionsPool()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var questions = await _db.QuestionBanks
            .Include(q => q.QuestionOptions)
            .OrderByDescending(q => q.QuestionId)
            .Select(q => new
            {
                questionId = q.QuestionId,
                questionText = q.QuestionText,
                questionType = q.QuestionType ?? "MultipleChoice",
                difficulty = q.Difficulty ?? "Medium",
                options = q.QuestionOptions.OrderBy(o => o.OptionId).Select(o => new
                {
                    optionId = o.OptionId,
                    optionText = o.OptionText,
                    isCorrect = o.IsCorrect ?? false
                }).ToList()
            })
            .ToListAsync();

        return Json(questions);
    }

    [HttpPost("/api/it/questions-pool")]
    public async Task<IActionResult> CreateQuestionInPool([FromBody] QuestionPoolItemDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        if (string.IsNullOrWhiteSpace(dto.QuestionText))
            return BadRequest(new { error = "Nội dung câu hỏi là bắt buộc." });

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var q = new QuestionBank
                    {
                        QuestionText = dto.QuestionText.Trim(),
                        QuestionType = dto.QuestionType ?? "MultipleChoice",
                        Difficulty = dto.Difficulty ?? "Medium"
                    };
                    _db.QuestionBanks.Add(q);
                    await _db.SaveChangesAsync();

                    if (dto.Options != null && (dto.QuestionType == "MultipleChoice" || dto.QuestionType == "FillInTheBlank"))
                    {
                        foreach (var opt in dto.Options)
                        {
                            if (string.IsNullOrWhiteSpace(opt.OptionText)) continue;
                            _db.QuestionOptions.Add(new QuestionOption
                            {
                                QuestionId = q.QuestionId,
                                OptionText = opt.OptionText.Trim(),
                                IsCorrect = opt.IsCorrect
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                    return Ok(new { success = true, questionId = q.QuestionId });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi khi tạo câu hỏi: " + ex.Message });
        }
    }

    [HttpPut("/api/it/questions-pool/{id}")]
    public async Task<IActionResult> UpdateQuestionInPool(int id, [FromBody] QuestionPoolItemDto dto)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var q = await _db.QuestionBanks.Include(x => x.QuestionOptions).FirstOrDefaultAsync(x => x.QuestionId == id);
        if (q == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.QuestionText))
            return BadRequest(new { error = "Nội dung câu hỏi là bắt buộc." });

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    q.QuestionText = dto.QuestionText.Trim();
                    q.QuestionType = dto.QuestionType ?? "MultipleChoice";
                    q.Difficulty = dto.Difficulty ?? "Medium";

                    _db.QuestionOptions.RemoveRange(q.QuestionOptions);
                    await _db.SaveChangesAsync();

                    if (dto.Options != null && (dto.QuestionType == "MultipleChoice" || dto.QuestionType == "FillInTheBlank"))
                    {
                        foreach (var opt in dto.Options)
                        {
                            if (string.IsNullOrWhiteSpace(opt.OptionText)) continue;
                            _db.QuestionOptions.Add(new QuestionOption
                            {
                                QuestionId = q.QuestionId,
                                OptionText = opt.OptionText.Trim(),
                                IsCorrect = opt.IsCorrect
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                    return Ok(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi khi cập nhật câu hỏi: " + ex.Message });
        }
    }

    [HttpDelete("/api/it/questions-pool/{id}")]
    public async Task<IActionResult> DeleteQuestionInPool(int id)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var q = await _db.QuestionBanks.FindAsync(id);
        if (q == null) return NotFound();

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var options = await _db.QuestionOptions.Where(o => o.QuestionId == id).ToListAsync();
                    _db.QuestionOptions.RemoveRange(options);

                    var links = await _db.ExamQuestions.Where(eq => eq.QuestionId == id).ToListAsync();
                    _db.ExamQuestions.RemoveRange(links);

                    var answers = await _db.UserAnswers.Where(a => a.QuestionId == id).ToListAsync();
                    _db.UserAnswers.RemoveRange(answers);

                    _db.QuestionBanks.Remove(q);
                    await _db.SaveChangesAsync();

                    await transaction.CommitAsync();
                    return Ok(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi khi xóa câu hỏi: " + ex.Message });
        }
    }

    [HttpGet("/api/it/exams/{examId}/participants")]
    public async Task<IActionResult> GetExamParticipants(int examId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var exam = await _db.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam == null) return NotFound(new { error = "Không tìm thấy bài thi." });

        var courseId = exam.CourseId;
        
        var userExams = await _db.UserExams
            .Include(ue => ue.User)
                .ThenInclude(u => u.Department)
            .Where(ue => ue.ExamId == examId)
            .ToListAsync();

        var enrollments = new List<Enrollment>();
        if (courseId.HasValue)
        {
            enrollments = await _db.Enrollments
                .Include(e => e.User)
                    .ThenInclude(u => u.Department)
                .Where(e => e.CourseId == courseId && e.User != null && e.User.Status == "Active")
                .ToListAsync();
        }

        var allUsersMap = new Dictionary<int, User>();
        foreach (var e in enrollments)
        {
            if (e.User != null && !allUsersMap.ContainsKey(e.User.UserId))
            {
                allUsersMap[e.User.UserId] = e.User;
            }
        }
        foreach (var ue in userExams)
        {
            if (ue.User != null && !allUsersMap.ContainsKey(ue.User.UserId))
            {
                allUsersMap[ue.User.UserId] = ue.User;
            }
        }

        var passScore = exam.PassScore ?? 50m;
        var sortedUsers = allUsersMap.Values.OrderBy(u => u.FullName ?? "").ToList();
        var participants = new List<object>();

        foreach (var user in sortedUsers)
        {
            var attempts = userExams.Where(ue => ue.UserId == user.UserId).ToList();
            
            string statusText = "Chưa làm";
            string statusClass = "secondary";
            decimal? score = null;
            DateTime? endTime = null;

            if (attempts.Any(a => a.IsFinish == true))
            {
                var finishedAttempts = attempts.Where(a => a.IsFinish == true).ToList();
                var bestAttempt = finishedAttempts.OrderByDescending(a => a.Score).First();
                score = bestAttempt.Score;
                endTime = bestAttempt.EndTime;

                if (score >= passScore)
                {
                    statusText = "Đạt";
                    statusClass = "success";
                }
                else
                {
                    statusText = "Không đạt";
                    statusClass = "danger";
                }
            }
            else if (attempts.Any())
            {
                statusText = "Đang làm";
                statusClass = "warning";
            }

            participants.Add(new
            {
                employeeCode = user.EmployeeCode,
                fullName = user.FullName ?? "N/A",
                departmentName = user.Department?.DepartmentName ?? "N/A",
                score = score,
                statusText = statusText,
                statusClass = statusClass,
                endTime = endTime
            });
        }

        return Json(participants);
    }

    [HttpPost("/api/it/exams/{examId}/save-structure")]
    public async Task<IActionResult> SaveExamStructure(int examId, [FromBody] List<int> questionIds)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound(new { error = "Không tìm thấy bài thi." });

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var oldLinks = await _db.ExamQuestions.Where(eq => eq.ExamId == examId).ToListAsync();
                    _db.ExamQuestions.RemoveRange(oldLinks);
                    await _db.SaveChangesAsync();

                    foreach (var qId in questionIds)
                    {
                        _db.ExamQuestions.Add(new ExamQuestion
                        {
                            ExamId = examId,
                            QuestionId = qId,
                            Points = 10
                        });
                    }
                    await _db.SaveChangesAsync();

                    await transaction.CommitAsync();
                    return Ok(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi khi lưu cấu trúc bài thi: " + ex.Message });
        }
    }

    private int GetCurrentUserId() =>
        int.Parse(HttpContext.Session.GetString("UserID") ?? "0");

    private int GetCurrentDeptId() =>
        int.Parse(HttpContext.Session.GetString("DepartmentID") ?? "0");

    private async Task<string> GenerateUniqueCourseCode(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Course";
        }

        string cleanTitle = title.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in cleanTitle)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        string asciiTitle = sb.ToString().Replace("đ", "d").Replace("Đ", "D");

        var words = asciiTitle.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var initialsBuilder = new System.Text.StringBuilder();
        foreach (var w in words)
        {
            if (w.Length > 0 && char.IsLetterOrDigit(w[0]))
            {
                initialsBuilder.Append(char.ToUpper(w[0]));
            }
        }

        string prefix = initialsBuilder.ToString();
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "CRSE";
        }
        
        if (prefix.Length > 6)
        {
            prefix = prefix.Substring(0, 6);
        }

        int counter = 101;
        string candidateCode = $"{prefix}{counter}";
        while (await _db.Courses.AnyAsync(c => c.CourseCode == candidateCode))
        {
            counter++;
            candidateCode = $"{prefix}{counter}";
        }

        return candidateCode;
    }

    [HttpPost("/api/it/ai-create-course-from-word")]
    public async Task<IActionResult> CreateCourseFromWord([FromBody] ImportCourseFileDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        if (string.IsNullOrEmpty(dto.Base64Data)) return BadRequest("Dữ liệu file trống.");

        try
        {
            byte[] fileBytes = Convert.FromBase64String(dto.Base64Data);
            string wordText = "";

            using (var ms = new System.IO.MemoryStream(fileBytes))
            {
                using (var doc = Xceed.Words.NET.DocX.Load(ms))
                {
                    var linesList = new List<string>();
                    foreach (var p in doc.Paragraphs)
                    {
                        var t = p.Text.Trim();
                        if (!string.IsNullOrEmpty(t)) linesList.Add(t);
                    }
                    foreach (var table in doc.Tables)
                    {
                        foreach (var row in table.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                foreach (var p in cell.Paragraphs)
                                {
                                    var t = p.Text.Trim();
                                    if (!string.IsNullOrEmpty(t)) linesList.Add(t);
                                }
                            }
                        }
                    }
                    wordText = string.Join("\n", linesList);
                }
            }

            if (string.IsNullOrWhiteSpace(wordText))
            {
                return BadRequest("Không trích xuất được nội dung từ file Word.");
            }

            var generatedData = await _aiService.GenerateCourseFromWordTextAsync(wordText);

            var course = new Course
            {
                Title = generatedData.Title,
                Description = generatedData.Description,
                CourseCode = await GenerateUniqueCourseCode(generatedData.Title),
                OwnerDepartmentId = deptId,
                TargetDepartmentId = deptId,
                IsMandatory = false,
                Status = "Published",
                CreatedAt = DateTime.Now,
                CreatedBy = GetCurrentUserId()
            };
            _db.Courses.Add(course);
            await _db.SaveChangesAsync();

            int moduleOrder = 1;
            foreach (var mod in generatedData.Modules)
            {
                var module = new CourseModule
                {
                    CourseId = course.CourseId,
                    Title = mod.Title,
                    SortOrder = moduleOrder++
                };
                _db.CourseModules.Add(module);
                await _db.SaveChangesAsync();

                int lessonOrder = 1;
                var lessonContents = new StringBuilder();
                foreach (var lessonData in mod.Lessons)
                {
                    var lesson = new Lesson
                    {
                        ModuleId = module.ModuleId,
                        Title = lessonData.Title,
                        ContentType = "Document",
                        ContentBody = lessonData.ContentBody,
                        SortOrder = lessonOrder++
                    };
                    _db.Lessons.Add(lesson);
                    lessonContents.AppendLine($"<h3>{lessonData.Title}</h3>");
                    lessonContents.AppendLine(lessonData.ContentBody);
                }
                await _db.SaveChangesAsync();

                // Sinh quiz tự động cho chương này nếu có bài học
                if (mod.Lessons.Count > 0)
                {
                    try
                    {
                        var quizData = await _aiService.GenerateQuizFromLessonsAsync(mod.Title, lessonContents.ToString());
                        var exam = new Exam
                        {
                            CourseId = course.CourseId,
                            ModuleId = module.ModuleId,
                            ExamTitle = quizData.ExamTitle,
                            DurationMinutes = quizData.DurationMinutes,
                            PassScore = 80,
                            MaxAttempts = 3,
                            StartDate = DateTime.Now,
                            EndDate = DateTime.Now.AddYears(1),
                            TargetDepartmentId = deptId
                        };
                        _db.Exams.Add(exam);
                        await _db.SaveChangesAsync();

                        foreach (var qDto in quizData.Questions)
                        {
                            var question = new QuestionBank
                            {
                                QuestionText = qDto.QuestionText,
                                QuestionType = qDto.QuestionType,
                                Difficulty = "Medium"
                            };
                            _db.QuestionBanks.Add(question);
                            await _db.SaveChangesAsync();

                            if (qDto.QuestionType == "MultipleChoice")
                            {
                                foreach (var optDto in qDto.Options)
                                {
                                    var option = new QuestionOption
                                    {
                                        QuestionId = question.QuestionId,
                                        OptionText = optDto.OptionText,
                                        IsCorrect = optDto.IsCorrect
                                    };
                                    _db.QuestionOptions.Add(option);
                                }
                            }
                            else if (qDto.QuestionType == "FillInTheBlank")
                            {
                                var option = new QuestionOption
                                {
                                    QuestionId = question.QuestionId,
                                    OptionText = qDto.Options.Count > 0 ? qDto.Options[0].OptionText : "",
                                    IsCorrect = true
                                };
                                _db.QuestionOptions.Add(option);
                            }

                            var eq = new ExamQuestion
                            {
                                ExamId = exam.ExamId,
                                QuestionId = question.QuestionId,
                                Points = qDto.Points
                            };
                            _db.ExamQuestions.Add(eq);
                        }
                        await _db.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Lỗi tự động sinh quiz cho chương {mod.Title}: {ex.Message}");
                    }
                }
            }

            return Ok(new { success = true, courseId = course.CourseId, title = course.Title, code = course.CourseCode });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi khi nhập khóa học từ Word: " + ex.Message });
        }
    }

    [HttpPost("/api/it/generate-quiz-from-course")]
    public async Task<IActionResult> GenerateQuizFromCourse([FromBody] GenerateQuizFromCourseDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var course = await _db.Courses.FirstOrDefaultAsync(c => c.CourseId == dto.CourseId);
        if (course == null) return NotFound("Không tìm thấy khóa học.");

        try
        {
            var lessonsQuery = _db.Lessons.AsQueryable();
            string titlePrefix = course.Title ?? "Khóa học";

            if (dto.ModuleId.HasValue && dto.ModuleId.Value > 0)
            {
                var mod = await _db.CourseModules.FirstOrDefaultAsync(m => m.ModuleId == dto.ModuleId.Value);
                if (mod == null) return NotFound("Không tìm thấy chương học.");
                lessonsQuery = lessonsQuery.Where(l => l.ModuleId == dto.ModuleId.Value);
                titlePrefix = mod.Title ?? titlePrefix;
            }
            else
            {
                var moduleIds = await _db.CourseModules.Where(m => m.CourseId == dto.CourseId).Select(m => m.ModuleId).ToListAsync();
                lessonsQuery = lessonsQuery.Where(l => l.ModuleId != null && moduleIds.Contains(l.ModuleId.Value));
            }

            var lessons = await lessonsQuery.ToListAsync();
            if (lessons.Count == 0)
            {
                return BadRequest("Không có bài học nào trong khóa học/chương học này để quét nội dung.");
            }

            var lessonContents = new StringBuilder();
            foreach (var l in lessons)
            {
                lessonContents.AppendLine($"<h3>{l.Title}</h3>");
                lessonContents.AppendLine(l.ContentBody);
            }

            var quizData = await _aiService.GenerateQuizFromLessonsAsync(titlePrefix, lessonContents.ToString());

            // Lưu bài thi vào hệ thống
            var exam = new Exam
            {
                CourseId = dto.CourseId,
                ModuleId = dto.ModuleId,
                ExamTitle = quizData.ExamTitle,
                DurationMinutes = quizData.DurationMinutes,
                PassScore = 80,
                MaxAttempts = 3,
                StartDate = DateTime.Now,
                EndDate = DateTime.Now.AddYears(1),
                TargetDepartmentId = course.TargetDepartmentId
            };
            _db.Exams.Add(exam);
            await _db.SaveChangesAsync();

            foreach (var qDto in quizData.Questions)
            {
                var question = new QuestionBank
                {
                    QuestionText = qDto.QuestionText,
                    QuestionType = qDto.QuestionType,
                    Difficulty = "Medium"
                };
                _db.QuestionBanks.Add(question);
                await _db.SaveChangesAsync();

                if (qDto.QuestionType == "MultipleChoice")
                {
                    foreach (var optDto in qDto.Options)
                    {
                        var option = new QuestionOption
                        {
                            QuestionId = question.QuestionId,
                            OptionText = optDto.OptionText,
                            IsCorrect = optDto.IsCorrect
                        };
                        _db.QuestionOptions.Add(option);
                    }
                }
                else if (qDto.QuestionType == "FillInTheBlank")
                {
                    var option = new QuestionOption
                    {
                        QuestionId = question.QuestionId,
                        OptionText = qDto.Options.Count > 0 ? qDto.Options[0].OptionText : "",
                        IsCorrect = true
                    };
                    _db.QuestionOptions.Add(option);
                }

                var eq = new ExamQuestion
                {
                    ExamId = exam.ExamId,
                    QuestionId = question.QuestionId,
                    Points = qDto.Points
                };
                _db.ExamQuestions.Add(eq);
            }
            await _db.SaveChangesAsync();

            return Ok(new { success = true, examId = exam.ExamId, examTitle = exam.ExamTitle });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi khi tự động sinh quiz: " + ex.Message });
        }
    }

    [HttpPost("/api/it/generate-test-files")]
    public IActionResult GenerateTestFiles()
    {
        return Ok(new { success = true, message = "Các file mẫu đã được chuẩn bị sẵn trong thư mục wwwroot/sample-data/" });
    }
}
// DTOs
public class GenerateQuizFromCourseDto
{
    public int CourseId { get; set; }
    public int? ModuleId { get; set; }
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
    public string? QuestionType { get; set; }
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


public class PromptFileDto
{
    public string Base64Data { get; set; } = "";
    public string MimeType { get; set; } = "application/pdf";
}


