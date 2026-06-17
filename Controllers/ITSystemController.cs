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
    [HttpGet("/api/it/stats")]
    public async Task<IActionResult> Stats()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var totalUsers = await _db.Users.CountAsync();
        var totalDepartments = await _db.Departments.CountAsync();
        var totalCourses = await _db.Courses.CountAsync();
        var totalExams = await _db.Exams.CountAsync();

        // 1. Tỷ lệ hoàn thành khóa học chung (Course Completion Distribution)
        var totalEnrollments = await _db.Enrollments.CountAsync();
        var completedEnrollments = await _db.Enrollments.CountAsync(e => e.ProgressPercent == 100 || e.Status == "Completed");
        var inProgressEnrollments = await _db.Enrollments.CountAsync(e => e.ProgressPercent > 0 && e.ProgressPercent < 100 && e.Status != "Completed");
        var notStartedEnrollments = totalEnrollments - completedEnrollments - inProgressEnrollments;

        var studyDist = new Dictionary<string, int>
        {
            { "Hoàn thành", completedEnrollments },
            { "Đang học", inProgressEnrollments },
            { "Chưa học", notStartedEnrollments >= 0 ? notStartedEnrollments : 0 }
        };

        // 2. Bảng xếp hạng học tập theo phòng ban (Department Leaderboard)
        var dbDepts = await _db.Departments.AsNoTracking().ToListAsync();
        var dbUsers = await _db.Users
            .AsNoTracking()
            .Select(u => new
            {
                u.UserId,
                u.DepartmentId,
                Enrollments = u.Enrollments.Select(e => new { e.ProgressPercent, e.Status }).ToList()
            })
            .ToListAsync();

        var departments = dbDepts.Select(d =>
        {
            var deptUsers = dbUsers.Where(u => u.DepartmentId == d.DepartmentId).ToList();
            var deptEnrollments = deptUsers.SelectMany(u => u.Enrollments).ToList();
            var enrollmentCount = deptEnrollments.Count;
            var completedCount = deptEnrollments.Count(e => e.ProgressPercent == 100 || e.Status == "Completed");
            var avgProgress = enrollmentCount > 0 ? deptEnrollments.Average(e => e.ProgressPercent ?? 0) : 0.0;

            return new
            {
                d.DepartmentId,
                DepartmentName = d.DepartmentName ?? "Phòng ban ẩn",
                UserCount = deptUsers.Count,
                EnrollmentCount = enrollmentCount,
                CompletedCount = completedCount,
                AvgProgress = avgProgress
            };
        })
        .OrderByDescending(x => x.AvgProgress)
        .ToList();

        return Json(new
        {
            totalUsers,
            totalDepartments,
            totalCourses,
            totalExams,
            studyDist,
            departments
        });
    }
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
        if (backup == null || string.IsNullOrEmpty(backup.FileName))
            return NotFound(new { error = "Không tìm thấy bản backup hoặc tên file trống" });

        var downloadFileName = backup.FileName;
        if (downloadFileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            downloadFileName = Path.ChangeExtension(downloadFileName, ".sql");

        var backupDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "backups");
        var filePath = Path.Combine(backupDir, backup.FileName);

        // Nếu file đã tồn tại thì trả về ngay
        if (System.IO.File.Exists(filePath))
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(bytes, "text/plain", downloadFileName);
        }

        // File chưa tồn tại: tạo mới (thường xảy ra với các bản backup cũ)
        try
        {
            if (!Directory.Exists(backupDir))
                Directory.CreateDirectory(backupDir);

            var sqlContent = await GenerateSqlDumpAsync();
            await System.IO.File.WriteAllTextAsync(filePath, sqlContent, Encoding.UTF8);

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(bytes, "text/plain", downloadFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating backup file on demand: " + backup.FileName);
            return StatusCode(500, new { error = "Không thể tạo file backup: " + ex.Message });
        }
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
