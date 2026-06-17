using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KhoaHoc.Models;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using System.Text.Json;
using KhoaHoc.Infrastructure;

namespace KhoaHoc.Controllers;

public class HRController : Controller
{
    private readonly CorporateLmsProContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly KhoaHoc.Services.IEmailService _emailService;
    private readonly KhoaHoc.Services.IAIService _aiService;

    public HRController(CorporateLmsProContext db, IWebHostEnvironment env, KhoaHoc.Services.IEmailService emailService, KhoaHoc.Services.IAIService aiService)
    {
        _db = db;
        _env = env;
        _emailService = emailService;
        _aiService = aiService;
    }

    private IActionResult? RequireManager()
    {
        var role = HttpContext.Session.GetString("Role");
        if (HttpContext.Session.GetString("UserID") == null)
            return RedirectToAction("Login", "Auth");
        if (role != "Manager" && role != "IT" && role != "Trainer" && role != "Lecturer")
            return RedirectToAction("Login", "Auth");
        return null;
    }

    private int GetCurrentUserId() =>
        int.Parse(HttpContext.Session.GetString("UserID") ?? "0");

    private int GetCurrentDeptId() =>
        int.Parse(HttpContext.Session.GetString("DepartmentID") ?? "0");

    private bool IsTrainingCenter() =>
        HttpContext.Session.GetString("DepartmentName") == "Trung tâm Đào tạo Nội bộ";

    private IActionResult? RequireDepartmentManagerApi()
    {
        var auth = RequireManager();
        if (auth != null)
            return Json(new { error = "Unauthorized" });

        var role = HttpContext.Session.GetString("Role");
        if ((role != "Manager" && role != "IT") || GetCurrentDeptId() <= 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Chỉ trưởng phòng mới có quyền thực hiện thao tác này." });

        return null;
    }

    private static string GetDeptPrefix(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "NV";
        var lower = name.ToLower().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in lower)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        var clean = sb.ToString().Replace("đ", "d");
        
        if (clean.Contains("giam doc")) return "GD";
        if (clean.Contains("nhan su")) return "HR";
        if (clean.Contains("ky thuat") || clean.Contains("san xuat")) return "KT";
        if (clean.Contains("kinh doanh") || clean.Contains("marketing")) return "KD";
        if (clean.Contains("tai chinh") || clean.Contains("ke toan")) return "TC";
        if (clean.Contains("cong nghe") || clean.Contains("thong tin") || clean.Contains("it")) return "IT";
        if (clean.Contains("dao tao") || clean.Contains("giang vien")) return "GV";
        return "NV";
    }

    // Dashboard chính HR
    public async Task<IActionResult> Index()
    {
        var auth = RequireManager();
        if (auth != null) return auth;
        return View();
    }

    // API: KPIs tổng quan HR
    [HttpGet("/api/hr/stats")]
    public async Task<IActionResult> Stats()
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        int deptId = GetCurrentDeptId();
        var query = _db.Users.Where(u => u.Status == "Active");
        if (deptId > 0) query = query.Where(u => u.DepartmentId == deptId);

        var totalEmployees = await query.CountAsync();
        
        var assignmentQuery = _db.TrainingAssignments.AsQueryable();
        if (deptId > 0) assignmentQuery = assignmentQuery.Where(ta => ta.User != null && ta.User.DepartmentId == deptId);
        var totalAssignments = await assignmentQuery.CountAsync();

        var enrollmentQuery = _db.Enrollments.AsQueryable();
        if (deptId > 0) enrollmentQuery = enrollmentQuery.Where(e => e.User != null && e.User.DepartmentId == deptId);
        var completedTrainings = await enrollmentQuery.CountAsync(e => e.Status == "Completed");

        var certQuery = _db.Certificates.AsQueryable();
        if (deptId > 0) certQuery = certQuery.Where(c => c.User != null && c.User.DepartmentId == deptId);
        var totalCertificates = await certQuery.CountAsync();

        var budgetData = await _db.DeptTrainingBudgets
            .Where(b => b.Year == DateTime.Now.Year && (deptId == 0 || b.DeptId == deptId))
            .SumAsync(b => (decimal?)b.TotalBudget) ?? 0;

        var spentData = await _db.DeptTrainingBudgets
            .Where(b => b.Year == DateTime.Now.Year && (deptId == 0 || b.DeptId == deptId))
            .SumAsync(b => (decimal?)b.SpentAmount) ?? 0;

        string deptName = "Hệ thống";
        string deptPrefix = "HR";
        if (deptId > 0)
        {
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == deptId);
            if (dept != null)
            {
                deptName = dept.DepartmentName;
                deptPrefix = GetDeptPrefix(deptName);
            }
        }

        return Json(new
        {
            totalEmployees,
            totalAssignments,
            completedTrainings,
            totalCertificates,
            totalBudget = budgetData,
            spentBudget = spentData,
            budgetUsagePercent = budgetData > 0 ? Math.Round(spentData / budgetData * 100, 1) : 0,
            deptName,
            deptPrefix
        });
    }

    // API: Danh sách phân công đào tạo
    [HttpGet("/api/hr/assignments")]
    public async Task<IActionResult> GetAssignments(string? search, string? priority, int page = 1, int pageSize = 15)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        int deptId = GetCurrentDeptId();
        var query = _db.TrainingAssignments
            .Include(ta => ta.User)
                .ThenInclude(u => u!.Department)
            .Include(ta => ta.Course)
            .Include(ta => ta.AssignedByNavigation)
            .AsQueryable();

        if (deptId > 0)
            query = query.Where(ta => ta.User != null && ta.User.DepartmentId == deptId);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(ta => (ta.User != null && ta.User.FullName!.Contains(search))
                                   || (ta.Course != null && ta.Course.Title!.Contains(search)));

        if (!string.IsNullOrEmpty(priority) && priority != "all")
            query = query.Where(ta => ta.Priority == priority);

        var total = await query.CountAsync();
        var assignments = await query
            .OrderByDescending(ta => ta.AssignedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ta => new
            {
                assignmentId = ta.AssignmentId,
                employeeName = ta.User != null ? ta.User.FullName : "N/A",
                department = ta.User != null && ta.User.Department != null ? ta.User.Department.DepartmentName : "N/A",
                courseName = ta.Course != null ? ta.Course.Title : "N/A",
                assignedBy = ta.AssignedByNavigation != null ? ta.AssignedByNavigation.FullName : "N/A",
                assignedDate = ta.AssignedDate,
                dueDate = ta.DueDate,
                priority = ta.Priority
            })
            .ToListAsync();

        return Json(new { total, page, assignments });
    }

    // API: Tạo phân công đào tạo mới
    [HttpPost("/api/hr/assignments")]
    public async Task<IActionResult> CreateAssignment([FromBody] CreateAssignmentDto dto)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        if (dto.UserId == GetCurrentUserId())
        {
            return BadRequest(new { error = "Trưởng phòng không được phép tự phân công khóa học cho chính mình." });
        }

        var assignment = new TrainingAssignment
        {
            UserId = dto.UserId,
            CourseId = dto.CourseId,
            AssignedBy = GetCurrentUserId(),
            AssignedDate = DateTime.Now,
            DueDate = dto.DueDate,
            Priority = dto.Priority ?? "Normal"
        };

        _db.TrainingAssignments.Add(assignment);

        // Tự động tạo Enrollment nếu chưa có
        var existingEnrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.UserId == dto.UserId && e.CourseId == dto.CourseId);

        if (existingEnrollment == null)
        {
            _db.Enrollments.Add(new Enrollment
            {
                UserId = dto.UserId,
                CourseId = dto.CourseId,
                EnrollDate = DateTime.Now,
                ProgressPercent = 0,
                Status = "NotStarted"
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, assignmentId = assignment.AssignmentId });
    }

    // API: Ngân sách theo phòng ban
    [HttpGet("/api/hr/budget")]
    public async Task<IActionResult> GetBudget(int? year)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        int targetYear = year ?? DateTime.Now.Year;
        int deptId = GetCurrentDeptId();

        var budgets = await _db.DeptTrainingBudgets
            .Include(b => b.Dept)
            .Where(b => b.Year == targetYear && (deptId == 0 || b.DeptId == deptId))
            .Select(b => new
            {
                budgetId = b.BudgetId,
                department = b.Dept != null ? b.Dept.DepartmentName : "N/A",
                year = b.Year,
                totalBudget = b.TotalBudget,
                spentAmount = b.SpentAmount,
                remaining = (b.TotalBudget ?? 0) - (b.SpentAmount ?? 0),
                usagePercent = b.TotalBudget > 0
                    ? Math.Round((double)(b.SpentAmount ?? 0) / (double)b.TotalBudget! * 100, 1)
                    : 0.0
            })
            .OrderByDescending(b => b.totalBudget)
            .ToListAsync();

        return Json(budgets);
    }

    // API: Báo cáo kỹ năng theo phòng ban
    [HttpGet("/api/hr/skills")]
    public async Task<IActionResult> SkillReport(int? departmentId)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var query = _db.UserSkills
            .Include(us => us.User)
                .ThenInclude(u => u.Department)
            .Include(us => us.Skill)
            .AsQueryable();

        if (departmentId.HasValue)
            query = query.Where(us => us.User.DepartmentId == departmentId);

        var skillData = await query
            .GroupBy(us => us.Skill.SkillName)
            .Select(g => new
            {
                skillName = g.Key,
                averageScore = Math.Round(g.Average(us => (double)(us.LevelScore ?? 0)), 1),
                employeeCount = g.Count()
            })
            .OrderByDescending(s => s.averageScore)
            .ToListAsync();

        // Danh sách departments để filter
        var departments = await _db.Departments
            .Select(d => new { d.DepartmentId, d.DepartmentName })
            .ToListAsync();

        return Json(new { skillData, departments });
    }

    // API: Tiến độ học tập theo phòng ban (cho biểu đồ)
    [HttpGet("/api/hr/training-progress")]
    public async Task<IActionResult> TrainingProgress()
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var data = await _db.Enrollments
            .Include(e => e.User)
                .ThenInclude(u => u!.Department)
            .Where(e => e.User != null && e.User.Department != null)
            .GroupBy(e => e.User!.Department!.DepartmentName)
            .Select(g => new
            {
                department = g.Key,
                total = g.Count(),
                completed = g.Count(e => e.Status == "Completed"),
                inProgress = g.Count(e => e.Status == "InProgress"),
                notStarted = g.Count(e => e.Status == "NotStarted"),
                completionRate = g.Count() > 0
                    ? Math.Round((double)g.Count(e => e.Status == "Completed") / g.Count() * 100, 1)
                    : 0.0
            })
            .ToListAsync();

        return Json(data);
    }

    // API: Danh sách nhân viên để phân công & quản lý
    [HttpGet("/api/hr/employees")]
    public async Task<IActionResult> GetEmployees(int? departmentId)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        int currentDeptId = GetCurrentDeptId();
        int currentUserId = GetCurrentUserId();
        var query = _db.Users
            .Include(u => u.Department)
            .Where(u => u.UserId != currentUserId)
            .AsQueryable();

        var isTrainingCenter = IsTrainingCenter();
        if (currentDeptId > 0 && !isTrainingCenter)
            query = query.Where(u => u.DepartmentId == currentDeptId);
        else if (departmentId.HasValue && departmentId.Value > 0)
            query = query.Where(u => u.DepartmentId == departmentId.Value);

        var employees = await query
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName,
                department = u.Department != null ? u.Department.DepartmentName : "N/A",
                employeeCode = u.EmployeeCode,
                email = u.Email,
                status = u.Status,
                assignedCount = _db.TrainingAssignments.Count(ta => ta.UserId == u.UserId),
                completedCount = _db.Enrollments.Count(e => e.UserId == u.UserId && e.Status == "Completed")
            })
            .OrderBy(u => u.fullName)
            .ToListAsync();

        return Json(employees);
    }

    // NEW: API Lấy chi tiết Hồ sơ năng lực (Profile) của 1 nhân viên
    [HttpGet("/api/hr/employees/{id}/profile")]
    public async Task<IActionResult> GetEmployeeProfile(int id)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var user = await _db.Users
            .Include(u => u.Department)
            .Include(u => u.JobTitle)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user == null) return NotFound("User not found");

        int myDeptId = GetCurrentDeptId();
        if (myDeptId > 0 && user.DepartmentId != myDeptId)
            return Forbid();

        var assignmentsList = await _db.TrainingAssignments
            .Include(ta => ta.Course)
            .Where(ta => ta.UserId == id)
            .ToListAsync();

        var enrollmentsList = await _db.Enrollments
            .Include(e => e.Course)
            .Where(e => e.UserId == id)
            .ToListAsync();

        var allCourses = new List<object>();
        var seenCourseIds = new HashSet<int>();

        foreach (var ta in assignmentsList)
        {
            if (ta.CourseId.HasValue)
            {
                var enroll = enrollmentsList.FirstOrDefault(e => e.CourseId == ta.CourseId.Value);
                allCourses.Add(new {
                    courseId = ta.CourseId.Value,
                    title = ta.Course?.Title ?? "Khóa học",
                    progress = enroll?.ProgressPercent ?? 0,
                    status = enroll?.Status ?? "NotStarted",
                    assignmentId = (int?)ta.AssignmentId,
                    dueDate = ta.DueDate
                });
                seenCourseIds.Add(ta.CourseId.Value);
            }
        }

        foreach (var e in enrollmentsList)
        {
            if (e.CourseId.HasValue && !seenCourseIds.Contains(e.CourseId.Value))
            {
                allCourses.Add(new {
                    courseId = e.CourseId.Value,
                    title = e.Course?.Title ?? "Khóa học",
                    progress = e.ProgressPercent ?? 0,
                    status = e.Status ?? "NotStarted",
                    assignmentId = (int?)null,
                    dueDate = (DateTime?)null
                });
            }
        }

        var skills = await _db.UserSkills
            .Include(s => s.Skill)
            .Where(s => s.UserId == id)
            .Select(s => new {
                skillName = s.Skill!.SkillName,
                score = s.LevelScore,
                lastEvaluated = s.LastAssessed
            })
            .ToListAsync();

        return Json(new {
            fullName = user.FullName,
            email = user.Email,
            employeeCode = user.EmployeeCode,
            departmentName = user.Department?.DepartmentName ?? "N/A",
            jobTitle = user.JobTitle?.TitleName ?? "N/A",
            status = user.Status,
            courses = allCourses,
            skills = skills
        });
    }

    // NEW: Hủy giao khóa học
    [HttpDelete("/api/hr/assignments/{id}")]
    public async Task<IActionResult> DeleteAssignment(int id)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var assignment = await _db.TrainingAssignments.FindAsync(id);
        if (assignment == null) return NotFound("Phân công không tồn tại.");

        var user = await _db.Users.FindAsync(assignment.UserId);
        int myDeptId = GetCurrentDeptId();
        if (myDeptId > 0 && user != null && user.DepartmentId != myDeptId)
        {
            return Forbid();
        }

        _db.TrainingAssignments.Remove(assignment);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // NEW: Gia hạn khóa học
    [HttpPost("/api/hr/assignments/{id}/extend")]
    public async Task<IActionResult> ExtendAssignment(int id, [FromBody] ExtendAssignmentDto dto)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var assignment = await _db.TrainingAssignments.FindAsync(id);
        if (assignment == null) return NotFound("Phân công không tồn tại.");

        var user = await _db.Users.FindAsync(assignment.UserId);
        int myDeptId = GetCurrentDeptId();
        if (myDeptId > 0 && user != null && user.DepartmentId != myDeptId)
        {
            return Forbid();
        }

        assignment.DueDate = dto.NewDueDate;
        await _db.SaveChangesAsync();

        // Gửi thông báo cho học viên biết họ đã được gia hạn
        var course = await _db.Courses.FindAsync(assignment.CourseId);
        var notifTitle = $"Bạn đã được gia hạn khóa học '{course?.Title}' đến ngày {(dto.NewDueDate.HasValue ? dto.NewDueDate.Value.ToString("dd/MM/yyyy HH:mm") : "Không giới hạn")}.";
        if (notifTitle.Length > 255)
        {
            notifTitle = notifTitle.Substring(0, 252) + "...";
        }
        var notification = new Notification
        {
            UserId = assignment.UserId,
            Title = notifTitle,
            IsRead = false
        };
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // NEW: Manager vô hiệu hóa nhân viên trong phòng
    [HttpPatch("/api/hr/employees/{id}/status")]
    public async Task<IActionResult> UpdateEmployeeStatus(int id, [FromBody] string status)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Security: Manager chỉ được sửa người trong phòng mình
        int myDeptId = GetCurrentDeptId();
        if (myDeptId > 0 && user.DepartmentId != myDeptId)
            return Forbid();

        user.Status = status == "Active" ? "Active" : "Inactive";
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // API: Danh sách departments
    [HttpGet("/api/hr/departments")]
    public async Task<IActionResult> GetDepartments()
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var depts = await _db.Departments
            .Select(d => new { d.DepartmentId, d.DepartmentName })
            .ToListAsync();
        return Json(depts);
    }

    [HttpGet("/api/hr/courses")]
    public async Task<IActionResult> GetCourses()
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        var currentDept = await _db.Departments.FindAsync(deptId);
        bool isTrainingCenter = currentDept?.DepartmentName == "Trung tâm Đào tạo Nội bộ";

        var query = _db.Courses.Where(c => c.Status != "Deleted");
        if (!isTrainingCenter)
        {
            query = query.Where(c => c.IsForAllDepartments == true 
                                  || c.TargetDepartmentId == null 
                                  || c.TargetDepartmentId == deptId 
                                  || c.OwnerDepartmentId == deptId);
        }

        var courses = await query
            .Select(c => new { 
                c.CourseId, 
                c.Title, 
                c.IsMandatory,
                c.Status,
                moduleCount = c.CourseModules.Count(),
                lessonCount = c.CourseModules.SelectMany(m => m.Lessons).Count(),
                examCount = c.Exams.Count(),
                isOwned = isTrainingCenter || c.OwnerDepartmentId == deptId,
                isGlobal = c.IsForAllDepartments == true
            })
            .ToListAsync();

        // Also include pending course requests from DocumentLibrary
        var pendingQuery = _db.DocumentLibraries.AsQueryable();
        if (!isTrainingCenter)
        {
            pendingQuery = pendingQuery.Where(d => d.SharedByDeptId == deptId);
        }
        var pendingCourses = await pendingQuery
            .Where(d => d.TargetType == "course" && d.ApprovalStatus == "Pending")
            .Select(d => new {
                CourseId = 0, // Not yet created
                Title = d.Title,
                IsMandatory = (bool?)false, 
                Status = "Pending",
                moduleCount = 0,
                lessonCount = 0,
                examCount = 0,
                isOwned = true,
                isGlobal = false
            })
            .ToListAsync();

        return Json(courses.Concat(pendingCourses));
    }

    // NEW: Manager tạo khóa học cho phòng mình
    [HttpPost("/api/hr/courses")]
    public async Task<IActionResult> CreateDeptCourse([FromBody] ItCreateCourseDto dto)
    {
        var auth = RequireManager();
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

        int deptId = GetCurrentDeptId();
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
            OwnerDepartmentId = deptId > 0 ? deptId : null,
            TargetDepartmentId = dto.TargetDepartmentIds != null && dto.TargetDepartmentIds.Any() ? dto.TargetDepartmentIds.First() : null,
            TargetDepartmentIds = dto.TargetDepartmentIds != null ? string.Join(",", dto.TargetDepartmentIds) : null,
            CreatedAt = DateTime.Now,
            CreatedBy = GetCurrentUserId()
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

        return Ok(new { success = true, id = course.CourseId, courseId = course.CourseId });
    }

    [HttpPut("/api/hr/courses/{id}")]
    public async Task<IActionResult> UpdateDeptCourse(int id, [FromBody] ItCreateCourseDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();

        int deptId = GetCurrentDeptId();
        if (deptId > 0 && !IsTrainingCenter() && course.OwnerDepartmentId != deptId) return Forbid();

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
        course.TargetDepartmentId = dto.TargetDepartmentIds != null && dto.TargetDepartmentIds.Any() ? dto.TargetDepartmentIds.First() : null;
        course.TargetDepartmentIds = dto.TargetDepartmentIds != null ? string.Join(",", dto.TargetDepartmentIds) : null;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/hr/courses/{id}")]
    public async Task<IActionResult> DeleteDeptCourse(int id)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();

        int deptId = GetCurrentDeptId();
        if (deptId > 0 && !IsTrainingCenter() && course.OwnerDepartmentId != deptId) return Forbid();

        course.Status = "Deleted";
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("/api/hr/courses/{id}/content")]
    public async Task<IActionResult> GetCourseContent(int id)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureCompatibilitySchemaAsync();

        var modules = await _db.CourseModules
            .Where(m => m.CourseId == id)
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
            .Where(e => e.CourseId == id)
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

        var course = await _db.Courses.FindAsync(id);
        var courseTitle = course?.Title ?? "";
        var courseStatus = course?.Status ?? "";

        return Json(new { courseId = id, title = courseTitle, status = courseStatus, modules, exams, documents });
    }

    [HttpPost("/api/hr/courses/{courseId}/modules")]
    public async Task<IActionResult> CreateCourseModule(int courseId, [FromBody] HrCreateModuleDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId);
        if (course == null) return NotFound();
        if (deptId > 0 && !IsTrainingCenter() && course.OwnerDepartmentId != deptId) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { error = "Tên chương là bắt buộc." });

        var module = new CourseModule
        {
            CourseId = courseId,
            Title = dto.Title.Trim(),
            SortOrder = dto.SortOrder ?? 0
        };
        _db.CourseModules.Add(module);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, moduleId = module.ModuleId });
    }



    [HttpPost("/api/hr/courses/{courseId}/publish")]
    public async Task<IActionResult> PublishCourse(int courseId, [FromBody] HrPublishCourseDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        var course = await _db.Courses
            .Include(c => c.CourseModules)
                .ThenInclude(m => m.Lessons)
            .FirstOrDefaultAsync(c => c.CourseId == courseId);
        if (course == null) return NotFound();
        if (deptId > 0 && !IsTrainingCenter() && course.OwnerDepartmentId != deptId) return Forbid();

        var totalLessons = course.CourseModules.SelectMany(m => m.Lessons).Count();
        if (dto.Publish == true && totalLessons == 0)
            return BadRequest(new { error = "Khóa học phải có ít nhất 1 bài học trước khi publish cho student." });

        course.Status = dto.Publish == true ? "Published" : "Draft";
        await _db.SaveChangesAsync();
        return Ok(new { success = true, status = course.Status });
    }

    [HttpGet("/api/hr/documents")]
    public async Task<IActionResult> GetDeptDocuments()
    {
        int deptId = GetCurrentDeptId();
        if (deptId <= 0) return Unauthorized();

        var docs = await (from d in _db.DocumentLibraries
                          where d.SharedByDeptId == deptId
                          orderby d.Id descending
                          join c in _db.Courses on d.CourseId equals c.CourseId into cj
                          from c in cj.DefaultIfEmpty()
                          join m in _db.CourseModules on d.ModuleId equals m.ModuleId into mj
                          from m in mj.DefaultIfEmpty()
                          join l in _db.Lessons on d.LessonId equals l.LessonId into lj
                          from l in lj.DefaultIfEmpty()
                          join e in _db.Exams on d.ExamId equals e.ExamId into ej
                          from e in ej.DefaultIfEmpty()
                          select new
                          {
                              id = d.Id,
                              title = d.Title,
                              filePath = d.FilePath,
                              approvalStatus = d.ApprovalStatus ?? "Pending",
                              rejectionReason = d.RejectionReason,
                              targetType = d.TargetType,
                              courseName = c != null ? c.Title : null,
                              moduleName = m != null ? m.Title : null,
                              lessonName = l != null ? l.Title : null,
                              examName = e != null ? e.ExamTitle : null
                          }).ToListAsync();

        return Json(docs);
    }

    [HttpPost("/api/hr/documents")]
    public async Task<IActionResult> UploadDocument([FromBody] UploadDocDto dto)
    {
        int deptId = GetCurrentDeptId();
        int userId = GetCurrentUserId();
        if (deptId <= 0 || userId <= 0) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { error = "Tiêu đề không được trống." });

        if (dto.TargetType != "course" && dto.CourseId <= 0) return BadRequest(new { error = "Bạn phải chọn một Khóa học." });
        
        // New flow: PendingData & TargetType are sufficient
        bool hasNewContent = !string.IsNullOrWhiteSpace(dto.TargetType) && !string.IsNullOrWhiteSpace(dto.PendingData);

        bool hasTarget = hasNewContent || dto.ModuleId.HasValue || dto.ExamId.HasValue || 
                          !string.IsNullOrWhiteSpace(dto.NewModuleName) || 
                          !string.IsNullOrWhiteSpace(dto.NewLessonName) || 
                          !string.IsNullOrWhiteSpace(dto.NewExamName);

        if (!hasTarget) 
            return BadRequest(new { error = "Bạn phải cung cấp thông tin để tạo nội dung mới." });

        var doc = new DocumentLibrary
        {
            Title = dto.Title,
            FilePath = dto.FilePath,
            CourseId = dto.CourseId,
            ModuleId = dto.ModuleId,
            LessonId = dto.LessonId,
            ExamId = dto.ExamId,
            NewModuleName = dto.NewModuleName,
            NewLessonName = dto.NewLessonName,
            NewExamName = dto.NewExamName,
            PendingData = dto.PendingData,
            TargetType = dto.TargetType,
            SharedByDeptId = deptId,
            ApprovalStatus = "Pending",
            CreatedBy = userId
        };

        try {
            _db.DocumentLibraries.Add(doc);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        } catch (Exception ex) {
            return StatusCode(500, new { error = "Lỗi lưu tài liệu: " + ex.Message, detail = ex.InnerException?.Message });
        }
    }

    [HttpPost("/api/hr/upload-temp")]
    public async Task<IActionResult> UploadTempFile(IFormFile? file)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (file == null || file.Length == 0) return BadRequest(new { error = "File không hợp lệ hoặc rỗng." });

        var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "temp");
        Directory.CreateDirectory(uploadsRoot);

        var safeFileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(uploadsRoot, safeFileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new { filePath = $"/uploads/temp/{safeFileName}" });
    }

    [HttpPost("/api/hr/generate-quiz-ai")]
    public async Task<IActionResult> GenerateQuizAI([FromBody] PromptDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var topic = dto.Prompt?.Trim() ?? "Bài tập tổng quát";
        var generatedData = await _aiService.GenerateQuizAsync(topic);

        return Ok(generatedData);
    }

    [HttpPost("/api/hr/generate-module-ai")]
    public async Task<IActionResult> GenerateModuleAI([FromBody] PromptDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });
        var result = await _aiService.GenerateModuleAsync(dto.Prompt ?? "Chương mới");
        return Ok(result);
    }

    [HttpPost("/api/hr/generate-lesson-ai")]
    public async Task<IActionResult> GenerateLessonAI([FromBody] PromptDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });
        var result = await _aiService.GenerateLessonAsync(dto.Prompt ?? "Bài học mới");
        return Ok(result);
    }

    [HttpPost("/api/hr/generate-quiz-from-file")]
    public async Task<IActionResult> GenerateQuizFromFileAI([FromBody] PromptFileDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrEmpty(dto.Base64Data)) return BadRequest("File data is required");
        
        var generatedData = await _aiService.GenerateQuizFromDocumentAsync(dto.Base64Data, dto.MimeType);
        return Ok(generatedData);
    }

    // NEW: Broadcast khóa học cho tất cả phòng ban
    [HttpPost("/api/hr/broadcast")]
    public async Task<IActionResult> BroadcastCourse([FromBody] int courseId)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var course = await _db.Courses.FindAsync(courseId);
        if (course == null) return NotFound();

        course.IsForAllDepartments = true;
        course.IsMandatory = true;
        
        await _db.SaveChangesAsync();

        // Gửi thông báo email tới tất cả phòng ban
        var depts = await _db.Departments.ToListAsync();
        await _emailService.NotifyAllDepartmentsAsync(course, depts);

        return Ok(new { success = true });
    }

    // NEW: Giao khóa học cho toàn bộ nhân viên trong phòng
    [HttpPost("/api/hr/assign-dept")]
    public async Task<IActionResult> AssignToDept([FromBody] AssignDeptDto dto)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        int userDeptId = GetCurrentDeptId();
        var currentDept = await _db.Departments.FindAsync(userDeptId);
        bool isTrainingCenter = currentDept?.DepartmentName == "Trung tâm Đào tạo Nội bộ";

        int targetDeptId = (isTrainingCenter && dto.DepartmentId > 0) ? dto.DepartmentId : userDeptId;
        int currentUserId = GetCurrentUserId();

        var users = await _db.Users
            .Where(u => u.DepartmentId == targetDeptId && u.Status == "Active" && u.UserId != currentUserId)
            .ToListAsync();

        foreach (var user in users)
        {
            var existing = await _db.TrainingAssignments
                .AnyAsync(ta => ta.UserId == user.UserId && ta.CourseId == dto.CourseId);
            
            if (!existing)
            {
                _db.TrainingAssignments.Add(new TrainingAssignment
                {
                    UserId = user.UserId,
                    CourseId = dto.CourseId,
                    AssignedBy = GetCurrentUserId(),
                    AssignedDate = DateTime.Now,
                    DueDate = dto.DueDate,
                    Priority = "High"
                });

                if (!await _db.Enrollments.AnyAsync(e => e.UserId == user.UserId && e.CourseId == dto.CourseId))
                {
                    _db.Enrollments.Add(new Enrollment { UserId = user.UserId, CourseId = dto.CourseId, EnrollDate = DateTime.Now, ProgressPercent = 0, Status = "NotStarted" });
                }
            }
        }

        // Cập nhật TargetDepartmentId và TargetDepartmentIds của khóa học để học viên mới sau này tự động được giao / xem thấy
        var course = await _db.Courses.FindAsync(dto.CourseId);
        if (course != null)
        {
            if (course.TargetDepartmentId == null || course.TargetDepartmentId == userDeptId)
            {
                course.TargetDepartmentId = targetDeptId;
            }

            var deptIds = new List<int>();
            if (!string.IsNullOrEmpty(course.TargetDepartmentIds))
            {
                var parts = course.TargetDepartmentIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (int.TryParse(part, out int parsedId))
                    {
                        deptIds.Add(parsedId);
                    }
                }
            }
            if (!deptIds.Contains(targetDeptId))
            {
                deptIds.Add(targetDeptId);
                course.TargetDepartmentIds = string.Join(",", deptIds);
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // NEW: Tạo nhân viên mới trong phòng ban
    [HttpPost("/api/hr/employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        if (deptId == 0) return BadRequest("Chỉ Trưởng phòng mới có thể tạo nhân sự.");

        GenerateEmailAndUsername(dto.FullName, dto.EmployeeCode, out string username, out string email);

        if (await _db.Users.AnyAsync(u => u.Email == email || u.Username == username))
            return BadRequest("Email hoặc Username đã tồn tại.");

        var user = new User
        {
            Username = username,
            PasswordHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("123456")),
            FullName = dto.FullName,
            Email = email,
            EmployeeCode = dto.EmployeeCode,
            DepartmentId = deptId,
            IsItadmin = false,
            Status = "Active"
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, userId = user.UserId });
    }

    // NEW: Gọi AI tạo nội dung khóa học (Trả về JSON preview cho frontend)
    [HttpPost("/api/hr/ai-generate-course")]
    public async Task<IActionResult> GenerateCourseAI([FromBody] PromptDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var topic = dto.Prompt?.Trim() ?? "Kỹ năng mới";
        var generatedData = await _aiService.GenerateCourseContentAsync(topic);

        return Ok(generatedData);
    }

    // NEW: Nút bấm "Tạo và Lưu tự động", AI làm hết mọi việc tạo DB (Phương án tốt nhất)
    [HttpPost("/api/hr/ai-create-full-course")]
    public async Task<IActionResult> CreateFullCourseAI([FromBody] PromptDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        if (deptId == 0) return BadRequest("Chỉ Trưởng phòng mới có quyền này.");

        var topic = dto.Prompt?.Trim() ?? "Giải quyết vấn đề";
        var generatedData = await _aiService.GenerateCourseContentAsync(topic);

        // Lưu vào cơ sở dữ liệu
        var course = new Course
        {
            Title = generatedData.Title,
            Description = generatedData.Description,
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
            foreach (var lessonTitle in mod.LessonTitles)
            {
                var lesson = new Lesson
                {
                    ModuleId = module.ModuleId,
                    Title = lessonTitle,
                    ContentType = "Document",
                    ContentBody = $"Nội dung bài học {lessonTitle} sẽ được HR bổ sung sau.",
                    SortOrder = lessonOrder++
                };
                _db.Lessons.Add(lesson);
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true, courseId = course.CourseId, title = course.Title, modulesCreated = generatedData.Modules.Count });
    }

    [HttpPost("/api/hr/ai-create-course-from-word")]
    public async Task<IActionResult> CreateCourseFromWord([FromBody] ImportCourseFileDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        if (deptId == 0) return BadRequest("Chỉ Trưởng phòng mới có quyền này.");

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

            // Gọi AI phân tích và sinh cấu trúc khóa học bao gồm bài học
            var generatedData = await _aiService.GenerateCourseFromWordTextAsync(wordText);

            // Lưu khóa học vào DB
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
                        Console.WriteLine($"Lỗi tự động sinh quiz cho chương {mod.Title}: {ex.Message}");
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

    // ============================================================
    // NEW API: QUẢN LÝ TIẾN ĐỘ & ĐIỂM DANH (GIẢNG VIÊN / HR)
    // ============================================================

    [HttpGet("/api/hr/staff-progress")]
    public async Task<IActionResult> GetStaffProgress()
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        int deptId = GetCurrentDeptId();

        var users = await _db.Users
            .Where(u => u.Status == "Active" && (deptId == 0 || u.DepartmentId == deptId))
            .ToListAsync();

        var resultList = new List<object>();

        foreach (var u in users)
        {
            // Lấy các phân công khóa học (TrainingAssignments) của nhân viên này
            var userAssignments = await _db.TrainingAssignments
                .Where(ta => ta.UserId == u.UserId)
                .ToListAsync();

            // Get enrolled courses for this user
            var enrolledCourseIds = await _db.Enrollments
                .Where(e => e.UserId == u.UserId)
                .Select(e => e.CourseId)
                .ToListAsync();

            // Find all exams in these enrolled courses
            var exams = await _db.Exams
                .Where(e => e.CourseId != null && enrolledCourseIds.Contains(e.CourseId.Value))
                .ToListAsync();

            var incompleteQuizzes = new List<object>();
            foreach (var exam in exams)
            {
                var attempts = await _db.UserExams
                    .Where(ue => ue.UserId == u.UserId && ue.ExamId == exam.ExamId)
                    .ToListAsync();

                var finishedAttemptsCount = attempts.Count(ue => ue.IsFinish == true);
                var bestScore = attempts.Any(ue => ue.IsFinish == true) 
                    ? attempts.Where(ue => ue.IsFinish == true).Max(ue => ue.Score ?? 0) 
                    : 0m;
                var hasUnfinished = attempts.Any(ue => ue.IsFinish != true);

                var requiredScore = exam.PassScore ?? 50m;
                
                // An exam is incomplete if the student hasn't passed it with the required score
                if (bestScore < requiredScore)
                {
                    var ta = userAssignments.FirstOrDefault(a => a.CourseId == exam.CourseId);
                    var isOverdue = ta != null && ta.DueDate.HasValue && ta.DueDate.Value < DateTime.Now;

                    incompleteQuizzes.Add(new
                    {
                        examId = exam.ExamId,
                        title = exam.ExamTitle ?? "N/A",
                        courseId = exam.CourseId,
                        attemptsCount = finishedAttemptsCount,
                        maxAttempts = exam.MaxAttempts,
                        bestScore = bestScore,
                        hasUnfinished = hasUnfinished,
                        passScore = requiredScore,
                        isOverdue = isOverdue,
                        dueDate = ta?.DueDate,
                        assignmentId = ta?.AssignmentId
                    });
                }
            }

            var enrollments = await _db.Enrollments
                .Include(e => e.Course)
                .Where(e => e.UserId == u.UserId && e.Status != "Completed")
                .ToListAsync();

            var missingLessons = new List<object>();
            foreach (var e in enrollments)
            {
                var ta = userAssignments.FirstOrDefault(a => a.CourseId == e.CourseId);
                var isOverdue = ta != null && ta.DueDate.HasValue && ta.DueDate.Value < DateTime.Now;

                missingLessons.Add(new
                {
                    courseId = e.CourseId,
                    title = e.Course?.Title ?? "N/A",
                    isOverdue = isOverdue,
                    dueDate = ta?.DueDate,
                    assignmentId = ta?.AssignmentId
                });
            }

            resultList.Add(new
            {
                userId = u.UserId,
                fullName = u.FullName,
                incompleteQuizzes,
                missingLessons
            });
        }

        return Json(resultList);
    }

    [HttpPost("/api/hr/notify-staff")]
    public async Task<IActionResult> NotifyStaff([FromBody] NotifyStaffDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.FindAsync(dto.UserId);
        if (user == null) return NotFound();

        var notification = new Notification
        {
            UserId = dto.UserId,
            Title = "Nhắc nhở học tập: " + dto.Message,
            IsRead = false
        };
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("/api/hr/notifications")]
    public async Task<IActionResult> GetHrNotifications()
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var userId = GetCurrentUserId();
        var notifications = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.Id)
            .Take(50)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                isRead = n.IsRead ?? false
            })
            .ToListAsync();

        return Json(notifications);
    }

    [HttpPost("/api/hr/notifications/{id}/read")]
    public async Task<IActionResult> MarkHrNotificationRead(int id)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var userId = GetCurrentUserId();
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notif == null) return NotFound();

        notif.IsRead = true;
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/notifications/read-all")]
    public async Task<IActionResult> MarkAllHrNotificationsRead()
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var userId = GetCurrentUserId();
        var unread = await _db.Notifications.Where(n => n.UserId == userId && n.IsRead != true).ToListAsync();
        foreach (var n in unread)
        {
            n.IsRead = true;
        }
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("/api/hr/schedules")]
    public async Task<IActionResult> GetHrSchedules()
    {
        var auth = RequireManager();
        if (auth != null) return Json(new List<object>());

        int deptId = GetCurrentDeptId();

        var schedules = await _db.OfflineTrainingEvents
            .AsNoTracking()
            .Include(e => e.Course)
            .Where(e => deptId <= 0 || e.DepartmentId == null || e.DepartmentId == deptId)
            .OrderByDescending(e => e.StartTime)
            .Select(e => new
            {
                eventId = e.EventId,
                title = e.Title ?? (e.Course != null ? e.Course.Title : "Lịch học"),
                courseTitle = e.Course != null ? e.Course.Title : "N/A",
                location = e.Location,
                startTime = e.StartTime,
                endTime = e.EndTime,
                status = e.Status ?? (e.EndTime < DateTime.Now ? "Đã kết thúc" : "Sắp diễn ra")
            })
            .ToListAsync();

        return Json(schedules);
    }

    [HttpGet("/api/hr/schedules/{id}/attendance")]
    public async Task<IActionResult> GetScheduleAttendance(int id)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        if (deptId <= 0) return Unauthorized();

        var schedule = await _db.OfflineTrainingEvents.FindAsync(id);
        if (schedule == null) return NotFound();

        // Lấy tất cả user trong phòng ban
        var users = await _db.Users
            .Where(u => u.DepartmentId == deptId && u.Status == "Active")
            .ToListAsync();

        var logs = await _db.AttendanceLogs
            .Where(a => a.EventId == id)
            .ToListAsync();

        var resultLogs = users.Select(u => {
            var log = logs.FirstOrDefault(l => l.UserId == u.UserId);
            return new {
                userId = u.UserId,
                fullName = u.FullName ?? u.Username,
                status = log?.AttendanceStatus ?? "Absent",
                checkInTime = log?.CheckInTime,
                cancelReason = log?.CancelReason
            };
        }).ToList();

        return Json(new { schedule, logs = resultLogs });
    }

    [HttpPost("/api/hr/attendance/cancel")]
    public async Task<IActionResult> CancelAttendance([FromBody] CancelAttendanceDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var log = await _db.AttendanceLogs.FirstOrDefaultAsync(a => a.EventId == dto.EventId && a.UserId == dto.UserId);
        if (log == null) return NotFound();

        log.AttendanceStatus = "Cancelled";
        log.Status = false;
        log.CancelReason = dto.Reason;
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
    [HttpDelete("/api/hr/documents/{id}")]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        int deptId = GetCurrentDeptId();
        if (deptId <= 0) return Unauthorized();

        var doc = await _db.DocumentLibraries.FirstOrDefaultAsync(d => d.Id == id && d.SharedByDeptId == deptId);
        if (doc == null) return NotFound("Không tìm thấy tài liệu hoặc không có quyền xóa.");

        _db.DocumentLibraries.Remove(doc);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/reset-attempts")]
    public async Task<IActionResult> ResetAttempts([FromBody] ResetAttemptsDto dto)
    {
        var auth = RequireDepartmentManagerApi();
        if (auth != null) return auth;

        var exam = await _db.Exams.FindAsync(dto.ExamId);
        if (exam == null) return NotFound(new { error = "Không tìm thấy bài thi" });

        // Delete user answers
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM UserAnswers WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE UserID = {0} AND ExamID = {1})", 
            dto.UserId, dto.ExamId);

        // Delete quiz session states
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM QuizSessionStates WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE UserID = {0} AND ExamID = {1})", 
            dto.UserId, dto.ExamId);

        // Delete user exams
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM UserExams WHERE UserID = {0} AND ExamID = {1}", 
            dto.UserId, dto.ExamId);

        // Recalculate progress
        if (exam.CourseId.HasValue)
        {
            await RecalculateProgressInternalAsync(dto.UserId, exam.CourseId.Value);
        }

        return Json(new { success = true, message = "Đã reset lượt làm bài thi thành công." });
    }

    private async Task<int> RecalculateProgressInternalAsync(int userId, int courseId)
    {
        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment == null) return 0;

        var totalLessons = await _db.Lessons
            .Where(l => _db.CourseModules.Any(m => m.ModuleId == l.ModuleId && m.CourseId == courseId))
            .CountAsync();
            
        var totalExams = await _db.Exams
            .Where(e => e.CourseId == courseId)
            .CountAsync();
            
        var totalItems = totalLessons + totalExams;

        var completedLessons = await _db.UserLessonLogs
            .Where(l => l.UserId == userId && l.Status == "Completed" && 
                        _db.Lessons.Any(ls => ls.LessonId == l.LessonId && 
                                             _db.CourseModules.Any(m => m.ModuleId == ls.ModuleId && m.CourseId == courseId)))
            .CountAsync();
            
        var completedExams = await _db.UserExams
            .Where(ue => ue.UserId == userId && ue.IsFinish == true &&
                        _db.Exams.Any(e => e.ExamId == ue.ExamId && e.CourseId == courseId && 
                                          ue.Score >= (e.PassScore ?? 50)))
            .Select(ue => ue.ExamId)
            .Distinct()
            .CountAsync();

        var completedItems = completedLessons + completedExams;
        var progressPercent = totalItems > 0 ? (int)Math.Round((double)completedItems / totalItems * 100) : 0;
        
        enrollment.ProgressPercent = progressPercent;
        enrollment.Status = progressPercent switch
        {
            >= 100 => "Completed",
            > 0 => "InProgress",
            _ => "NotStarted"
        };

        await _db.SaveChangesAsync();
        return progressPercent;
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

    [HttpGet("/api/hr/questions-pool")]
    public async Task<IActionResult> GetQuestionsPool()
    {
        var auth = RequireManager();
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

    [HttpPost("/api/hr/questions-pool")]
    public async Task<IActionResult> CreateQuestionInPool([FromBody] QuestionPoolItemDto dto)
    {
        var auth = RequireManager();
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

    [HttpPut("/api/hr/questions-pool/{id}")]
    public async Task<IActionResult> UpdateQuestionInPool(int id, [FromBody] QuestionPoolItemDto dto)
    {
        var auth = RequireManager();
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

                    // Remove old options
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

    [HttpDelete("/api/hr/questions-pool/{id}")]
    public async Task<IActionResult> DeleteQuestionInPool(int id)
    {
        var auth = RequireManager();
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
                    // Remove options
                    var options = await _db.QuestionOptions.Where(o => o.QuestionId == id).ToListAsync();
                    _db.QuestionOptions.RemoveRange(options);

                    // Remove links to exams
                    var links = await _db.ExamQuestions.Where(eq => eq.QuestionId == id).ToListAsync();
                    _db.ExamQuestions.RemoveRange(links);

                    // Remove student answers
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

    [HttpGet("/api/hr/exams-with-stats")]
    public async Task<IActionResult> GetExamsWithStats()
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var userDeptId = GetCurrentDeptId();
        var isTrainingCenter = IsTrainingCenter();

        var exams = await _db.Exams
            .Include(e => e.Course)
            .Include(e => e.ExamQuestions)
            .Include(e => e.UserExams)
                .ThenInclude(ue => ue.User)
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
                passedCount = e.UserExams.Count(ue => ue.IsFinish == true && (ue.Score ?? 0) >= (e.PassScore ?? 50m) && (isTrainingCenter || (ue.User != null && ue.User.DepartmentId == userDeptId))),
                failedCount = e.UserExams.Count(ue => ue.IsFinish == true && (ue.Score ?? 0) < (e.PassScore ?? 50m) && (isTrainingCenter || (ue.User != null && ue.User.DepartmentId == userDeptId)))
            })
            .ToListAsync();

        return Json(exams);
    }

    [HttpGet("/api/hr/categories")]
    public async Task<IActionResult> GetCategories()
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var cats = await _db.Categories
            .Select(c => new
            {
                categoryId = c.CategoryId,
                categoryName = c.CategoryName
            })
            .ToListAsync();
        return Json(cats);
    }

    [HttpGet("/api/hr/exams/{examId}/questions")]
    public async Task<IActionResult> GetExamQuestions(int examId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var questions = await _db.ExamQuestions
            .Include(eq => eq.Question)
                .ThenInclude(q => q.QuestionOptions)
            .Where(eq => eq.ExamId == examId)
            .Select(eq => new {
                eq.QuestionId, 
                eq.Points,
                questionText = eq.Question.QuestionText,
                questionType = eq.Question.QuestionType ?? "MultipleChoice",
                Options = eq.Question.QuestionOptions.Select(o => new { o.OptionId, o.OptionText, o.IsCorrect }).ToList()
            }).ToListAsync();

        return Json(questions);
    }

    [HttpPost("/api/hr/exams/{examId}/save-structure")]
    public async Task<IActionResult> SaveExamStructure(int examId, [FromBody] List<int> questionIds)
    {
        var auth = RequireManager();
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
                            Points = 10 // Default points per question
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

    [HttpGet("/api/hr/exams/{examId}/participants")]
    public async Task<IActionResult> GetExamParticipants(int examId)
    {
        var auth = RequireManager();
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

        int userDeptId = GetCurrentDeptId();
        bool isTrainingCenter = IsTrainingCenter();
        if (!isTrainingCenter)
        {
            enrollments = enrollments.Where(e => e.User != null && e.User.DepartmentId == userDeptId).ToList();
            userExams = userExams.Where(ue => ue.User != null && ue.User.DepartmentId == userDeptId).ToList();
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

    [HttpPost("/api/hr/courses/{courseId}/exams")]
    public async Task<IActionResult> CreateExam(int courseId, [FromBody] ItCreateExamDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        if (string.IsNullOrWhiteSpace(dto.ExamTitle))
            return BadRequest(new { error = "Tên quiz là bắt buộc." });

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    int? effectiveCourseId = courseId > 0 ? courseId : null;
                    
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
                        Level = dto.Level, 
                        MaxAttempts = dto.MaxAttempts, 
                        StartDate = dto.StartDate, 
                        EndDate = dto.EndDate, 
                        TargetDepartmentId = dto.TargetDepartmentId 
                    };
                    
                    _db.Exams.Add(exam);
                    await _db.SaveChangesAsync();

                    if (dto.AiQuestions != null && dto.AiQuestions.Any())
                    {
                        foreach (var qDto in dto.AiQuestions)
                        {
                            if (string.IsNullOrWhiteSpace(qDto.QuestionText)) continue;

                            var q = new QuestionBank { 
                                QuestionText = qDto.QuestionText.Trim(), 
                                QuestionType = qDto.QuestionType ?? "MultipleChoice",
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

                    // Send notification to target employees
                    var targetUserIds = new List<int>();
                    if (exam.TargetDepartmentId.HasValue && exam.TargetDepartmentId.Value > 0)
                    {
                        targetUserIds = await _db.Users
                            .Where(u => u.DepartmentId == exam.TargetDepartmentId.Value && u.Status == "Active")
                            .Select(u => u.UserId)
                            .ToListAsync();
                    }
                    else if (exam.CourseId.HasValue && exam.CourseId.Value > 0)
                    {
                        targetUserIds = await _db.Enrollments
                            .Where(e => e.CourseId == exam.CourseId.Value && e.User != null && e.User.Status == "Active")
                            .Select(e => e.UserId.Value)
                            .ToListAsync();
                    }
                    else
                    {
                        int userDeptId = GetCurrentDeptId();
                        bool isTrainingCenter = IsTrainingCenter();
                        if (!isTrainingCenter && userDeptId > 0)
                        {
                            targetUserIds = await _db.Users
                                .Where(u => u.DepartmentId == userDeptId && u.Status == "Active")
                                .Select(u => u.UserId)
                                .ToListAsync();
                        }
                        else
                        {
                            targetUserIds = await _db.Users
                                .Where(u => u.Status == "Active")
                                .Select(u => u.UserId)
                                .ToListAsync();
                        }
                    }

                    string notifTitle = $"Bạn có lịch thi mới: '{exam.ExamTitle}'" + 
                                        (exam.StartDate.HasValue ? $" bắt đầu từ {exam.StartDate.Value.ToString("dd/MM/yyyy HH:mm")}" : "") +
                                        (exam.EndDate.HasValue ? $" đến {exam.EndDate.Value.ToString("dd/MM/yyyy HH:mm")}." : ".");
                    if (notifTitle.Length > 255) notifTitle = notifTitle.Substring(0, 252) + "...";

                    foreach (var uid in targetUserIds)
                    {
                        _db.Notifications.Add(new Notification { UserId = uid, Title = notifTitle, IsRead = false });
                    }
                    await _db.SaveChangesAsync();

                    await transaction.CommitAsync();
                    return Ok(new { success = true, examId = exam.ExamId });
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
            return StatusCode(500, new { error = "Đã xảy ra lỗi hệ thống: " + ex.Message });
        }
    }

    // ============================================================
    // API: KHO TÀI LIỆU & BỘ XÂY DỰNG KÉO THẢ ĐỒNG BỘ TỪ IT
    // ============================================================
    private async Task EnsureCompatibilitySchemaAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(DatabaseCompatibility.SchemaPatchSql);
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

    [HttpGet("/api/hr/content-library")]
    public async Task<IActionResult> GetContentLibrary()
    {
        var auth = RequireManager();
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

    [HttpPut("/api/hr/modules/{moduleId}")]
    public async Task<IActionResult> UpdateModule(int moduleId, [FromBody] ItCreateModuleDto dto)
    {
        var auth = RequireManager();
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

    [HttpDelete("/api/hr/modules/{moduleId}")]
    public async Task<IActionResult> DeleteModule(int moduleId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var mod = await _db.CourseModules
            .Include(m => m.Lessons)
            .FirstOrDefaultAsync(m => m.ModuleId == moduleId);
        if (mod == null) return NotFound();

        foreach (var lesson in mod.Lessons)
        {
            lesson.ModuleId = null;
        }

        _db.CourseModules.Remove(mod);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/modules/{moduleId}/lessons")]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> CreateLesson(int moduleId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();
        var dto = await ReadLessonRequestAsync();

        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Tên bài học là bắt buộc." });
        if (await _db.Lessons.AnyAsync(l => l.ModuleId == moduleId && l.Title != null && l.Title.ToLower() == dto.Title.Trim().ToLower()))
            return BadRequest(new { error = $"Bài học '{dto.Title.Trim()}' đã tồn tại trong chương này." });

        if (IsVideoRequest(dto) && (dto.VideoFile == null || dto.VideoFile.Length == 0) && string.IsNullOrWhiteSpace(dto.VideoUrl))
            return BadRequest(new { error = "Bài video cần chọn file video hoặc nhập link video." });
        if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody))
            return BadRequest(new { error = "Bài văn bản cần có nội dung." });

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

    [HttpPut("/api/hr/lessons/{lessonId}")]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024L * 1024L)]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> UpdateLesson(int lessonId)
    {
        try
        {
            var auth = RequireManager();
            if (auth != null) return auth;

            await EnsureCompatibilitySchemaAsync();
            var dto = await ReadLessonRequestAsync();

            var lesson = await _db.Lessons.FindAsync(lessonId);
            if (lesson == null) return NotFound();
            if (!string.IsNullOrWhiteSpace(dto.Title) &&
                await _db.Lessons.AnyAsync(l => l.LessonId != lessonId && l.ModuleId == lesson.ModuleId && l.Title != null && l.Title.ToLower() == dto.Title.Trim().ToLower()))
                return BadRequest(new { error = $"Bài học {dto.Title.Trim()} đã tồn tại trong chương này." });

            bool isClearingVideo = dto.HasVideoUrlField && string.IsNullOrWhiteSpace(dto.VideoUrl) && (dto.VideoFile == null || dto.VideoFile.Length == 0);

            if (IsVideoRequest(dto) && !isClearingVideo && string.IsNullOrWhiteSpace(lesson.VideoUrl) && (dto.VideoFile == null || dto.VideoFile.Length == 0) && string.IsNullOrWhiteSpace(dto.VideoUrl))
                return BadRequest(new { error = "Bài video cần chọn file video hoặc nhập link video." });
            if (IsTextRequest(dto) && string.IsNullOrWhiteSpace(dto.ContentBody) && string.IsNullOrWhiteSpace(lesson.ContentBody))
                return BadRequest(new { error = "Bài văn bản cần có nội dung." });

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
            return StatusCode(500, new { error = "Lỗi cập nhật bài học: " + ex.Message });
        }
    }

    [HttpDelete("/api/hr/lessons/{id}")]
    public async Task<IActionResult> DeleteLesson(int id)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        try {
            var lesson = await _db.Lessons
                .Include(l => l.LessonAttachments)
                .FirstOrDefaultAsync(l => l.LessonId == id);
            
            if (lesson != null)
            {
                var logs = await _db.UserLessonLogs.Where(l => l.LessonId == id).ToListAsync();
                if (logs.Any()) _db.UserLessonLogs.RemoveRange(logs);

                if (lesson.LessonAttachments != null && lesson.LessonAttachments.Any())
                {
                    _db.LessonAttachments.RemoveRange(lesson.LessonAttachments);
                }

                _db.Lessons.Remove(lesson);
                await _db.SaveChangesAsync();
            }
            return Ok(new { success = true });
        } catch (Exception ex) {
            return StatusCode(500, new { error = "Lỗi khi xóa dữ liệu liên quan: " + ex.Message });
        }
    }

    [HttpPut("/api/hr/exams/{examId}")]
    public async Task<IActionResult> UpdateExam(int examId, [FromBody] ItCreateExamDto dto)
    {
        var auth = RequireManager();
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

    [HttpDelete("/api/hr/exams/{examId}")]
    public async Task<IActionResult> DeleteExam(int examId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound();

        _db.Exams.Remove(exam);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/exams/{examId}/link-to-course/{courseId}")]
    public async Task<IActionResult> LinkExamToCourse(int examId, int courseId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound("Không tìm thấy bài thi");

        if (exam.CourseId == courseId && courseId > 0)
        {
            return Ok(new { success = true, info = "Quiz này đã có sẵn trong khóa học này." });
        }

        exam.CourseId = courseId > 0 ? courseId : null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, title = exam.ExamTitle });
    }

    [HttpPost("/api/hr/modules/{moduleId}/link-to-course/{courseId}")]
    public async Task<IActionResult> LinkModuleToCourse(int moduleId, int courseId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var mod = await _db.CourseModules.FindAsync(moduleId);
        if (mod == null) return NotFound("Không tìm thấy chương");

        mod.CourseId = courseId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/lessons/{lessonId}/link-to-module/{moduleId}")]
    public async Task<IActionResult> LinkLessonToModule(int lessonId, int moduleId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var lesson = await _db.Lessons.FindAsync(lessonId);
        if (lesson == null) return NotFound("Không tìm thấy bài giảng");

        lesson.ModuleId = moduleId;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/lessons/{id}/unlink")]
    [HttpPost("/api/hr/lessons/{id}/unlink-from-module")]
    public async Task<IActionResult> UnlinkLesson(int id)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var lesson = await _db.Lessons.FindAsync(id);
        if (lesson == null) return NotFound();
        lesson.ModuleId = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/modules/{id}/unlink")]
    [HttpPost("/api/hr/modules/{id}/unlink-from-course/{courseId}")]
    public async Task<IActionResult> UnlinkModule(int id)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var mod = await _db.CourseModules.FindAsync(id);
        if (mod == null) return NotFound();
        mod.CourseId = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/exams/{examId}/unlink-from-course/{courseId}")]
    public async Task<IActionResult> UnlinkExamFromCourse(int examId, int courseId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var exam = await _db.Exams.FindAsync(examId);
        if (exam == null) return NotFound(new { error = "Không tìm thấy bài thi." });

        exam.CourseId = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/lessons/{lessonId}/attachments/upload")]
    public async Task<IActionResult> UploadLessonAttachment(int lessonId, IFormFile? file)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

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

    [HttpPost("/api/hr/lessons/{lessonId}/attachments/link")]
    public async Task<IActionResult> CreateLessonAttachmentLink(int lessonId, [FromBody] LessonAttachmentLinkDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

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

    [HttpDelete("/api/hr/attachments/{id}")]
    public async Task<IActionResult> DeleteLessonAttachment(int id)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

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

    [HttpPost("/api/hr/exams/{examId}/questions/batch")]
    public async Task<IActionResult> SaveExamQuestionsBatch(int examId, [FromBody] List<ItCreateQuestionDto>? questions)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        if (questions == null || questions.Count == 0)
            return BadRequest(new { error = "Danh sách câu hỏi trống." });

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
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
                            QuestionType = dto.QuestionType ?? "MultipleChoice",
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
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Lỗi lưu bộ câu hỏi: " + ex.Message });
        }
    }

    [HttpDelete("/api/hr/exams/{examId}/questions/{questionId}")]
    public async Task<IActionResult> DeleteExamQuestion(int examId, int questionId)
    {
        var auth = RequireManager();
        if (auth != null) return auth;

        var eq = await _db.ExamQuestions.FirstOrDefaultAsync(x => x.ExamId == examId && x.QuestionId == questionId);
        if (eq != null) {
            _db.ExamQuestions.Remove(eq);
            await _db.SaveChangesAsync();
        }
        return Ok(new { success = true });
    }

    [HttpPost("/api/hr/exams/generate")]
    public async Task<IActionResult> GenerateExam([FromBody] PromptDto dto)
    {
        var auth = RequireManager();
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

    [HttpPost("/api/hr/exams/generate-from-file")]
    public async Task<IActionResult> GenerateExamFromFile([FromBody] PromptFileDto dto)
    {
        var auth = RequireManager();
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

    [HttpPost("/api/hr/exams/generate-from-lesson")]
    public async Task<IActionResult> GenerateExamFromLesson([FromBody] GenerateFromLessonDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return auth;
        if (dto.LessonId <= 0) return BadRequest("LessonId required");

        try
        {
            var lesson = await _db.Lessons.FindAsync(dto.LessonId);
            if (lesson == null) return NotFound("Lesson not found");

            var quizData = await _aiService.GenerateQuizFromLessonsAsync(lesson.Title ?? "Bài học", lesson.ContentBody ?? "");
            return Ok(quizData);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}

public class GenerateFromLessonDto
{
    public int LessonId { get; set; }
}

// DTOs
public class ResetAttemptsDto
{
    public int UserId { get; set; }
    public int ExamId { get; set; }
}

public class NotifyStaffDto
{
    public int UserId { get; set; }
    public string Message { get; set; } = "";
}

public class CancelAttendanceDto
{
    public int EventId { get; set; }
    public int UserId { get; set; }
    public string Reason { get; set; } = "";
}

public class AssignDeptDto
{
    public int CourseId { get; set; }
    public int DepartmentId { get; set; }
    public DateTime? DueDate { get; set; }
}

public class CreateCourseDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool IsMandatory { get; set; }
}

public class CreateAssignmentDto
{
    public int UserId { get; set; }
    public int CourseId { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Priority { get; set; }
}

public class CreateEmployeeDto
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
}

public class PromptDto
{
    public string Prompt { get; set; } = "";
}

public class UploadDocDto
{
    public string Title { get; set; } = "";
    public string? FilePath { get; set; }
    public int CourseId { get; set; }
    public int? ModuleId { get; set; }
    public int? LessonId { get; set; }
    public int? ExamId { get; set; }
    public string? NewModuleName { get; set; }
    public string? NewLessonName { get; set; }
    public string? NewExamName { get; set; }
    public string? PendingData { get; set; }
    public string? TargetType { get; set; }
}

public class AssignDeptCourseDto
{
    public int CourseId { get; set; }
    public string Priority { get; set; } = "Normal";
    public DateTime? DueDate { get; set; }
}

public class UpdateHrCourseDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool? IsMandatory { get; set; }
    public string? Status { get; set; }
}

public class HrCreateModuleDto
{
    public string Title { get; set; } = "";
    public int? Level { get; set; }
    public int? SortOrder { get; set; }
}

public class HrCreateLessonDto
{
    public string Title { get; set; } = "";
    public string? ContentType { get; set; }
    public string? ContentBody { get; set; }
    public string? VideoUrl { get; set; }
    public int? Level { get; set; }
    public int? SortOrder { get; set; }
}

public class HrPublishCourseDto
{
    public bool Publish { get; set; }
}

public class ExtendAssignmentDto
{
    public DateTime? NewDueDate { get; set; }
}

public class QuestionPoolItemDto
{
    public string QuestionText { get; set; } = "";
    public string QuestionType { get; set; } = "MultipleChoice";
    public string Difficulty { get; set; } = "Medium";
    public List<QuestionPoolOptionDto>? Options { get; set; }
}

public class QuestionPoolOptionDto
{
    public string OptionText { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public class ImportCourseFileDto
{
    public string Base64Data { get; set; } = "";
}

