using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KhoaHoc.Models;

namespace KhoaHoc.Controllers;

public class HRController : Controller
{
    private readonly CorporateLmsProContext _db;

    private readonly KhoaHoc.Services.IEmailService _emailService;
    private readonly KhoaHoc.Services.IAIService _aiService;

    public HRController(CorporateLmsProContext db, KhoaHoc.Services.IEmailService emailService, KhoaHoc.Services.IAIService aiService)
    {
        _db = db;
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

        if (currentDeptId > 0)
            query = query.Where(u => u.DepartmentId == currentDeptId);
        else if (departmentId.HasValue)
            query = query.Where(u => u.DepartmentId == departmentId);

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

        var courses = await _db.Enrollments
            .Include(e => e.Course)
            .Where(e => e.UserId == id)
            .Select(e => new {
                title = e.Course!.Title,
                progress = e.ProgressPercent,
                status = e.Status,
                enrollDate = e.EnrollDate
            })
            .ToListAsync();

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
            courses,
            skills
        });
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
    public async Task<IActionResult> CreateDeptCourse([FromBody] CreateCourseDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        if (deptId == 0) return BadRequest("Chỉ Trưởng phòng mới có quyền này.");

        var course = new Course
        {
            Title = dto.Title,
            Description = dto.Description,
            OwnerDepartmentId = deptId,
            TargetDepartmentId = deptId, // Mặc định đích là chính phòng mình
            IsMandatory = dto.IsMandatory,
            Status = "Published",
            CreatedAt = DateTime.Now,
            CreatedBy = GetCurrentUserId()
        };

        _db.Courses.Add(course);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, courseId = course.CourseId });
    }

    [HttpPut("/api/hr/courses/{id}")]
    public async Task<IActionResult> UpdateDeptCourse(int id, [FromBody] UpdateHrCourseDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.CourseId == id);
        if (course == null) return NotFound();
        if (deptId > 0 && !IsTrainingCenter() && course.OwnerDepartmentId != deptId) return Forbid();

        if (!string.IsNullOrWhiteSpace(dto.Title)) course.Title = dto.Title.Trim();
        course.Description = dto.Description ?? course.Description;
        if (dto.IsMandatory.HasValue) course.IsMandatory = dto.IsMandatory.Value;
        if (!string.IsNullOrWhiteSpace(dto.Status)) course.Status = dto.Status;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("/api/hr/courses/{id}/content")]
    public async Task<IActionResult> GetCourseContent(int id)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        int deptId = GetCurrentDeptId();
        var course = await _db.Courses
            .Include(c => c.CourseModules)
                .ThenInclude(m => m.Lessons)
            .Include(c => c.Exams)
            .FirstOrDefaultAsync(c => c.CourseId == id);
        if (course == null) return NotFound();
        // Allow HR to load content of any course for linking documents


        return Json(new
        {
            courseId = course.CourseId,
            title = course.Title,
            status = course.Status,
            modules = course.CourseModules
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    moduleId = m.ModuleId,
                    title = m.Title,
                    sortOrder = m.SortOrder,
                    lessons = m.Lessons.OrderBy(l => l.SortOrder).Select(l => new
                    {
                        lessonId = l.LessonId,
                        title = l.Title,
                        contentType = l.ContentType,
                        contentBody = l.ContentBody,
                        videoUrl = l.VideoUrl,
                        sortOrder = l.SortOrder
                    })
                }),
            exams = course.Exams.Select(e => new
            {
                examId = e.ExamId,
                examTitle = e.ExamTitle,
                durationMinutes = e.DurationMinutes,
                passScore = e.PassScore
            })
        });
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

    [HttpPost("/api/hr/modules/{moduleId}/lessons")]
    public async Task<IActionResult> CreateCourseLesson(int moduleId, [FromBody] HrCreateLessonDto dto)
    {
        var auth = RequireManager();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var module = await _db.CourseModules.Include(m => m.Course).FirstOrDefaultAsync(m => m.ModuleId == moduleId);
        if (module?.Course == null) return NotFound();
        int deptId = GetCurrentDeptId();
        if (deptId > 0 && !IsTrainingCenter() && module.Course.OwnerDepartmentId != deptId) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { error = "Tên bài học là bắt buộc." });

        var lesson = new Lesson
        {
            ModuleId = moduleId,
            Title = dto.Title.Trim(),
            ContentType = string.IsNullOrWhiteSpace(dto.ContentType) ? "Document" : dto.ContentType,
            ContentBody = dto.ContentBody,
            VideoUrl = dto.VideoUrl,
            SortOrder = dto.SortOrder ?? 0
        };
        _db.Lessons.Add(lesson);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, lessonId = lesson.LessonId });
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
                    incompleteQuizzes.Add(new
                    {
                        examId = exam.ExamId,
                        title = exam.ExamTitle ?? "N/A",
                        courseId = exam.CourseId,
                        attemptsCount = finishedAttemptsCount,
                        maxAttempts = exam.MaxAttempts,
                        bestScore = bestScore,
                        hasUnfinished = hasUnfinished,
                        passScore = requiredScore
                    });
                }
            }

            var missingLessons = await _db.Enrollments
                .Where(e => e.UserId == u.UserId && e.Status != "Completed")
                .Select(e => new { courseId = e.CourseId, title = e.Course != null ? e.Course.Title : "N/A" })
                .ToListAsync();

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
