using System.Text.Json;
using KhoaHoc.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;

namespace KhoaHoc.Controllers;

public class StudentController : Controller
{
    private static readonly string[] VisibleCourseStatuses = ["Published", "Active"];

    private readonly CorporateLmsProContext _db;
    private readonly KhoaHoc.Services.IAIService _aiService;
    private readonly ILogger<StudentController> _logger;

    public StudentController(CorporateLmsProContext db, KhoaHoc.Services.IAIService aiService, ILogger<StudentController> logger)
    {
        _db = db;
        _aiService = aiService;
        _logger = logger;
    }

    private int? GetCurrentUserId()
    {
        var idStr = HttpContext.Session.GetString("UserID");
        return int.TryParse(idStr, out var id) ? id : null;
    }

    private int? GetCurrentDepartmentId()
    {
        var deptIdStr = HttpContext.Session.GetString("DepartmentID");
        return int.TryParse(deptIdStr, out var deptId) ? deptId : null;
    }

    private IQueryable<Course> ApplyStudentCourseScope(IQueryable<Course> query, int userId, int? departmentId)
    {
        return query.Where(c =>
            c.Status != null &&
            VisibleCourseStatuses.Contains(c.Status) &&
            (
                c.Enrollments.Any(e => e.UserId == userId) ||
                c.TrainingAssignments.Any(ta => ta.UserId == userId) ||
                c.IsForAllDepartments == true ||
                (departmentId.HasValue && departmentId > 0 && (
                    c.TargetDepartmentId == departmentId ||
                    (c.TargetDepartmentIds != null && EF.Functions.Like("," + c.TargetDepartmentIds + ",", "%," + departmentId.Value + ",%"))
                ))
            )
        );
    }

    private IActionResult RequireAuth()
    {
        if (HttpContext.Session.GetString("UserID") == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        return null!;
    }

    public async Task<IActionResult> Index()
    {
        var auth = RequireAuth();
        if (auth != null) return auth;
        return View();
    }

    [HttpGet("/api/student/dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        try {
            Console.WriteLine("DEBUG: Entering Dashboard API");
            var userId = GetCurrentUserId();
            if (userId == null) {
                Console.WriteLine("DEBUG: UserId is null");
                return Unauthorized();
            }

            var enrollments = await _db.Enrollments
                .Include(e => e.Course)
                .Where(e => e.UserId == userId)
                .ToListAsync();

            var certificates = await _db.Certificates
                .Where(c => c.UserId == userId)
                .CountAsync();

            Console.WriteLine($"DEBUG: Found {enrollments.Count} enrollments");

            return Json(new
            {
                totalEnrolled = enrollments.Count,
                inProgress = enrollments.Count(e => e.Status == "InProgress"),
                completed = enrollments.Count(e => e.Status == "Completed"),
                certificates = certificates,
                totalPoints = 0, // Simplified for debug
                badges = 0,
                recentCourses = enrollments
                    .OrderByDescending(e => e.EnrollDate)
                    .Take(4)
                    .Select(e => new
                    {
                        courseId = e.CourseId,
                        title = e.Course?.Title ?? "N/A",
                        progress = e.ProgressPercent ?? 0,
                        status = e.Status
                    })
            });
        } catch (Exception ex) {
            Console.WriteLine($"DEBUG: Dashboard Error: {ex.Message}\n{ex.StackTrace}");
            return StatusCode(500, new { error = "Lỗi Dashboard: " + ex.Message });
        }
    }

    [HttpGet("/api/student/courses")]
    public async Task<IActionResult> Courses(string? search, int? categoryId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var departmentId = GetCurrentDepartmentId();

        var query = ApplyStudentCourseScope(_db.Courses
            .Include(c => c.Category)
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .Include(c => c.Exams)
            .Include(c => c.TrainingAssignments.Where(ta => ta.UserId == userId)), userId.Value, departmentId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Title != null && c.Title.Contains(search));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(c => c.CategoryId == categoryId);
        }

        var courses = await query.Select(c => new
        {
            courseId = c.CourseId,
            title = c.Title,
            description = c.Description,
            category = c.Category != null ? c.Category.CategoryName : "Chua phan loai",
            isMandatory = c.IsMandatory,
            thumbnail = c.Thumbnail,
            status = c.Status,
            enrolled = c.Enrollments.Any(e => e.UserId == userId),
            progress = c.Enrollments.Where(e => e.UserId == userId)
                .Select(e => e.ProgressPercent)
                .FirstOrDefault() ?? 0,
            quizCount = c.Exams.Count
        }).ToListAsync();

        return Json(courses);
    }

    [HttpGet("/api/student/certificates")]
    public async Task<IActionResult> Certificates()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var certs = await _db.Certificates
            .Include(c => c.Course)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IssueDate)
            .Select(c => new
            {
                certId = c.CertId,
                certCode = c.CertCode,
                courseName = c.Course != null ? c.Course.Title : "N/A",
                issueDate = c.IssueDate
            })
            .ToListAsync();

        return Json(certs);
    }

    [HttpGet("/api/student/achievements")]
    public async Task<IActionResult> Achievements()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var userPoint = await _db.UserPoints
            .FirstOrDefaultAsync(up => up.UserId == userId);

        var badges = await _db.UserBadges
            .Include(ub => ub.Badge)
            .Where(ub => ub.UserId == userId)
            .OrderByDescending(ub => ub.EarnedDate)
            .Select(ub => new
            {
                badgeName = ub.Badge.BadgeName,
                description = ub.Badge.RequirementDescription,
                iconUrl = ub.Badge.ImageUrl,
                earnedDate = ub.EarnedDate
            })
            .ToListAsync();

        var leaderboard = await _db.UserPoints
            .Include(up => up.User)
            .OrderByDescending(up => up.TotalPoints)
            .Take(10)
            .Select(up => new
            {
                userId = up.UserId,
                fullName = up.User.FullName,
                totalPoints = up.TotalPoints
            })
            .ToListAsync();

        return Json(new
        {
            totalPoints = userPoint?.TotalPoints ?? 0,
            badges,
            leaderboard
        });
    }


    [HttpGet("/api/student/courses/{id}")]
    public async Task<IActionResult> CourseDetails(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var departmentId = GetCurrentDepartmentId();

        var course = await ApplyStudentCourseScope(_db.Courses
            .Include(c => c.Category)
            .Include(c => c.CourseModules)
                .ThenInclude(m => m.Lessons)
            .Include(c => c.Exams)
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .Include(c => c.TrainingAssignments.Where(ta => ta.UserId == userId)), userId.Value, departmentId)
            .FirstOrDefaultAsync(c => c.CourseId == id);

        if (course == null) return NotFound();

        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.CourseId == id && e.UserId == userId);

        return Json(new
        {
            courseId = course.CourseId,
            title = course.Title,
            description = course.Description,
            category = course.Category?.CategoryName ?? "Chung",
            isMandatory = course.IsMandatory,
            thumbnail = course.Thumbnail,
            enrolled = isEnrolled,
            totalModules = course.CourseModules.Count,
            totalLessons = course.CourseModules.SelectMany(m => m.Lessons).Count(),
            totalQuizzes = course.Exams.Count
        });
    }

    [HttpPost("/api/student/enroll")]
    public async Task<IActionResult> Enroll([FromBody] int courseId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        var departmentId = GetCurrentDepartmentId();

        var existing = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (existing != null) return BadRequest(new { error = "Ban da dang ky khoa hoc nay roi." });

        var course = await ApplyStudentCourseScope(_db.Courses
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .Include(c => c.TrainingAssignments.Where(ta => ta.UserId == userId)), userId.Value, departmentId)
            .FirstOrDefaultAsync(c => c.CourseId == courseId);
        if (course == null) return NotFound();

        var enrollment = new Enrollment
        {
            UserId = userId.Value,
            CourseId = courseId,
            EnrollDate = DateTime.Now,
            ProgressPercent = 0,
            Status = "NotStarted"
        };
        _db.Enrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("/Student/Learn/{courseId}")]
    public async Task<IActionResult> Learn(int courseId)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var userId = GetCurrentUserId();
        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment == null) return RedirectToAction("Index", "Student");

        var course = await _db.Courses.FindAsync(courseId);
        if (course == null) return NotFound();

        ViewBag.CourseId = courseId;
        ViewBag.CourseTitle = course.Title;
        // Tự động tính toán lại tiến độ để đảm bảo luôn chính xác khi mở trang
        ViewBag.Progress = await RecalculateEnrollmentProgressAsync(userId ?? 0, courseId);

        return View();
    }

    [HttpGet("/Student/Quiz/{courseId}/{examId}")]
    public async Task<IActionResult> Quiz(int courseId, int examId)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var userId = GetCurrentUserId();
        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (!isEnrolled) return RedirectToAction("Index", "Student");

        var exam = await _db.Exams
            .Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.ExamId == examId && e.CourseId == courseId);
        if (exam == null) return NotFound();

        // Check attempts limit
        var attemptsCount = await _db.UserExams.CountAsync(ue => ue.UserId == userId && ue.ExamId == examId && ue.IsFinish == true);
        var activeSession = await _db.UserExams.AnyAsync(ue => ue.UserId == userId && ue.ExamId == examId && ue.IsFinish != true);
        if (exam.MaxAttempts.HasValue && attemptsCount >= exam.MaxAttempts.Value && !activeSession)
        {
            return RedirectToAction("Learn", "Student", new { courseId });
        }

        // Check sequential chapter locks
        if (exam.ModuleId.HasValue)
        {
            var modulesList = await _db.CourseModules
                .Where(m => m.CourseId == courseId)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
            
            var currentMod = modulesList.FirstOrDefault(m => m.ModuleId == exam.ModuleId.Value);
            if (currentMod != null)
            {
                var courseExams = await _db.Exams
                    .Where(e => e.CourseId == courseId)
                    .ToListAsync();

                var courseExamIds = courseExams.Select(ce => ce.ExamId).ToList();

                var userExams = await _db.UserExams
                    .Where(ue => ue.UserId == userId && ue.IsFinish == true && ue.ExamId.HasValue && courseExamIds.Contains(ue.ExamId.Value))
                    .ToListAsync();

                bool isLocked = false;
                var currentIndex = modulesList.IndexOf(currentMod);
                for (int j = 0; j < currentIndex; j++)
                {
                    var prevMod = modulesList[j];
                    var prevModExam = courseExams.FirstOrDefault(e => e.ModuleId == prevMod.ModuleId);
                    if (prevModExam != null)
                    {
                        var bestScore = userExams
                            .Where(ue => ue.ExamId == prevModExam.ExamId)
                            .Select(ue => ue.Score ?? 0)
                            .DefaultIfEmpty(0m)
                            .Max();
                        
                        var passScore = prevModExam.PassScore ?? 50m;
                        if (bestScore < passScore)
                        {
                            isLocked = true;
                            break;
                        }
                    }
                }

                if (isLocked)
                {
                    return RedirectToAction("Learn", "Student", new { courseId });
                }
            }
        }

        ViewBag.CourseId = courseId;
        ViewBag.CourseTitle = exam.Course?.Title;
        ViewBag.ExamId = examId;
        ViewBag.ExamTitle = exam.ExamTitle;

        return View();
    }

    [HttpGet("/Student/QuizResult/{userExamId}")]
    public async Task<IActionResult> QuizResult(int userExamId)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var userId = GetCurrentUserId();
        var userExam = await _db.UserExams
            .Include(ue => ue.Exam)
            .FirstOrDefaultAsync(ue => ue.UserExamId == userExamId && ue.UserId == userId);
        if (userExam == null) return RedirectToAction("Index", "Student");

        ViewBag.UserExamId = userExamId;
        ViewBag.ExamTitle = userExam.Exam?.ExamTitle;

        return View();
    }

    [HttpGet("/api/student/curriculum/{courseId}")]
    public async Task<IActionResult> Curriculum(int courseId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment == null) return Forbid();

        // Luôn tính toán lại tiến độ để đồng bộ tuyệt đối giữa Dashboard và Learn
        await RecalculateEnrollmentProgressAsync(userId.Value, courseId);
        await _db.SaveChangesAsync();

        // Kiểm tra và cấp chứng chỉ nếu đủ điều kiện
        await CheckAndIssueCertificateAsync(userId.Value, courseId);

        var departmentId = GetCurrentDepartmentId();

        var completedLessonIds = await _db.UserLessonLogs
            .Where(l => l.UserId == userId && l.Status == "Completed")
            .Select(l => l.LessonId)
            .ToListAsync();

        var modulesList = await _db.CourseModules
            .Include(m => m.Lessons)
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        var courseExams = await _db.Exams
            .Where(e => e.CourseId == courseId)
            .ToListAsync();

        var courseExamIds = courseExams.Select(ce => ce.ExamId).ToList();

        var userExams = await _db.UserExams
            .Where(ue => ue.UserId == userId && ue.IsFinish == true && ue.ExamId.HasValue && courseExamIds.Contains(ue.ExamId.Value))
            .ToListAsync();

        // Calculate locks
        var moduleLocks = new Dictionary<int, (bool IsLocked, string Reason)>();
        for (int i = 0; i < modulesList.Count; i++)
        {
            var currentMod = modulesList[i];
            bool isLocked = false;
            string lockReason = "";

            // Check previous modules
            for (int j = 0; j < i; j++)
            {
                var prevMod = modulesList[j];
                var prevModExam = courseExams.FirstOrDefault(e => e.ModuleId == prevMod.ModuleId);
                if (prevModExam != null)
                {
                    var bestScore = userExams
                        .Where(ue => ue.ExamId == prevModExam.ExamId)
                        .Select(ue => ue.Score ?? 0)
                        .DefaultIfEmpty(0m)
                        .Max();

                    var passScore = prevModExam.PassScore ?? 50m;
                    if (bestScore < passScore)
                    {
                        isLocked = true;
                        lockReason = $"Cần hoàn thành bài thi '{prevModExam.ExamTitle}' của chương trước đạt {passScore}%";
                        break;
                    }
                }
            }
            moduleLocks[currentMod.ModuleId] = (isLocked, lockReason);
        }

        var modules = modulesList.Select(m => {
            var lockInfo = moduleLocks.TryGetValue(m.ModuleId, out var val) ? val : (false, "");
            return new
            {
                moduleId = m.ModuleId,
                title = m.Title,
                isLocked = lockInfo.Item1,
                lockReason = lockInfo.Item2,
                lessons = m.Lessons.OrderBy(l => l.SortOrder).Select(l => new
                {
                    lessonId = l.LessonId,
                    title = l.Title,
                    videoUrl = l.VideoUrl,
                    contentBody = l.ContentBody,
                    contentType = l.ContentType,
                    isCompleted = completedLessonIds.Contains((int)l.LessonId),
                    isLocked = lockInfo.Item1,
                    lockReason = lockInfo.Item2
                }).ToList()
            };
        }).ToList();

        var exams = await _db.Exams
            .Where(e => e.CourseId == courseId)
            .OrderBy(e => e.ExamId)
            .Select(e => new
            {
                examId = e.ExamId,
                moduleId = e.ModuleId,
                title = e.ExamTitle,
                durationMinutes = e.DurationMinutes ?? 30,
                passScore = e.PassScore ?? 50,
                questionCount = e.ExamQuestions.Count,
                maxAttempts = e.MaxAttempts,
                endDate = e.EndDate,
                attemptsCount = e.UserExams.Count(ue => ue.UserId == userId && ue.IsFinish == true),
                lastAttempt = e.UserExams
                    .Where(ue => ue.UserId == userId)
                    .OrderByDescending(ue => ue.StartTime)
                    .Select(ue => new
                    {
                        userExamId = ue.UserExamId,
                        score = ue.Score ?? 0,
                        isFinish = ue.IsFinish ?? false,
                        startTime = ue.StartTime,
                        endTime = ue.EndTime
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Cấu trúc lại để thêm isLocked và lockReason
        var mappedExams = exams.Select(e => {
            var dbExam = courseExams.FirstOrDefault(ce => ce.ExamId == e.examId);
            var isModuleLocked = false;
            var moduleLockReason = "";
            if (dbExam?.ModuleId != null && moduleLocks.TryGetValue(dbExam.ModuleId.Value, out var mLock))
            {
                isModuleLocked = mLock.IsLocked;
                moduleLockReason = mLock.Reason;
            }

            var isLocked = isModuleLocked || (e.maxAttempts != null && e.attemptsCount >= e.maxAttempts) || (e.endDate != null && DateTime.Now > e.endDate);
            var lockReason = "";
            if (isModuleLocked) lockReason = moduleLockReason;
            else if (e.endDate != null && DateTime.Now > e.endDate) lockReason = "Đã hết hạn làm bài";
            else if (e.maxAttempts != null && e.attemptsCount >= e.maxAttempts) lockReason = "Đã hết số lần làm bài tối đa";

            return new {
                e.examId,
                e.moduleId,
                e.title,
                e.durationMinutes,
                e.passScore,
                e.questionCount,
                e.maxAttempts,
                e.endDate,
                e.attemptsCount,
                e.lastAttempt,
                isLocked,
                lockReason
            };
        }).ToList();

        // Tính điểm trung bình Quiz chính xác (tính cả những bài chưa làm là 0đ)
        var allExams = await _db.Exams
            .Where(e => e.CourseId == courseId)
            .Select(e => new { e.ExamId })
            .ToListAsync();

        var bestScores = new List<decimal>();
        foreach (var ex in allExams)
        {
            var bestScore = await _db.UserExams
                .Where(ue => ue.UserId == userId && ue.ExamId == ex.ExamId && ue.IsFinish == true)
                .OrderByDescending(ue => ue.Score)
                .Select(ue => ue.Score ?? 0)
                .FirstOrDefaultAsync();
            bestScores.Add(bestScore);
        }

        var evaluation = BuildCourseEvaluation(enrollment.ProgressPercent ?? 0, bestScores);

        return Json(new
        {
            debug = new { courseId, userId, modulesCount = modules.Count, examsCount = mappedExams.Count },
            modules,
            exams = mappedExams,
            progressPercent = enrollment.ProgressPercent ?? 0,
            evaluation = new
            {
                progressWeight = 40,
                quizWeight = 60,
                lessonCompletionPercent = evaluation.ProgressPercent,
                quizAverage = evaluation.QuizAverage,
                weightedScore = evaluation.WeightedScore,
                ranking = evaluation.Ranking,
                isPassed = evaluation.IsPassed,
                note = evaluation.Note
            }
        });
    }

    [HttpGet("/api/student/modules/{moduleId}/summary-ai")]
    public async Task<IActionResult> GetModuleSummaryAI(int moduleId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var module = await _db.CourseModules
            .Include(m => m.Lessons)
            .FirstOrDefaultAsync(m => m.ModuleId == moduleId);

        if (module == null) return NotFound();

        // Kiểm tra xem user có đang enroll khóa học chứa module này không
        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == module.CourseId);
        if (!isEnrolled) return Forbid();

        var lessonsText = string.Join("\n\n", module.Lessons.OrderBy(l => l.SortOrder).Select(l => 
            $"--- Bài: {l.Title} ---\n{l.ContentBody}"));

        var summary = await _aiService.SummarizeModuleAsync(module.Title ?? "Chương học", lessonsText);

        return Ok(new { summary });
    }

    [HttpPost("/api/student/complete-lesson")]
    public async Task<IActionResult> CompleteLesson([FromBody] int lessonId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var lesson = await _db.Lessons.Include(l => l.Module).FirstOrDefaultAsync(l => l.LessonId == lessonId);
        if (lesson?.Module?.CourseId == null) return NotFound();

        var log = await _db.UserLessonLogs.FirstOrDefaultAsync(l => l.LessonId == lessonId && l.UserId == userId);
        if (log == null)
        {
            log = new UserLessonLog { UserId = userId, LessonId = lessonId, Status = "Completed", DurationSpent = 0 };
            _db.UserLessonLogs.Add(log);
        }
        else
        {
            log.Status = "Completed";
        }

        var progressPercent = await RecalculateEnrollmentProgressAsync(userId.Value, lesson.Module.CourseId.Value);
        await _db.SaveChangesAsync();

        // Kiểm tra và cấp chứng chỉ ngay khi hoàn thành bài học cuối cùng
        await CheckAndIssueCertificateAsync(userId.Value, lesson.Module.CourseId.Value);

        // Lấy thêm thông tin Quiz để tính toán lại Xếp loại ngay lập tức
        var quizScores = await _db.UserExams
            .Where(ue => ue.UserId == userId && ue.IsFinish == true && 
                        _db.Exams.Any(e => e.ExamId == ue.ExamId && e.CourseId == lesson.Module.CourseId))
            .Select(ue => ue.Score ?? 0)
            .ToListAsync();
        
        var eval = BuildCourseEvaluation(progressPercent, quizScores.Select(s => (decimal)s));

        return Ok(new { 
            success = true, 
            progressPercent,
            evaluation = new {
                progressWeight = 40,
                quizWeight = 60,
                lessonCompletionPercent = eval.ProgressPercent,
                quizAverage = eval.QuizAverage,
                weightedScore = eval.WeightedScore,
                ranking = eval.Ranking,
                isPassed = eval.IsPassed,
                note = eval.Note
            }
        });
    }

    [HttpGet("/api/student/quizzes/{examId}")]
    public async Task<IActionResult> GetQuizSession(int examId)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var exam = await _db.Exams
                .Include(e => e.Course)
                .Include(e => e.ExamQuestions)
                    .ThenInclude(eq => eq.Question)
                        .ThenInclude(q => q.QuestionOptions)
                .FirstOrDefaultAsync(e => e.ExamId == examId);
            
            if (exam == null) return NotFound(new { error = "Khong tim thay bai thi." });
            if (exam.CourseId == null) return BadRequest(new { error = "Bai thi khong thuoc khoa hoc nao." });

            var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == exam.CourseId);
            if (!isEnrolled) return Forbid();

            // Check max attempts
            var attemptsCount = await _db.UserExams.CountAsync(ue => ue.UserId == userId && ue.ExamId == examId && ue.IsFinish == true);
            var activeSession = await _db.UserExams.AnyAsync(ue => ue.UserId == userId && ue.ExamId == examId && ue.IsFinish != true);
            if (exam.MaxAttempts.HasValue && attemptsCount >= exam.MaxAttempts.Value && !activeSession)
            {
                return BadRequest(new { error = "Bạn đã hết số lần làm bài kiểm tra này." });
            }

            // Check if the exam's module is locked
            if (exam.ModuleId.HasValue)
            {
                var courseId = exam.CourseId.Value;
                var modulesList = await _db.CourseModules
                    .Where(m => m.CourseId == courseId)
                    .OrderBy(m => m.SortOrder)
                    .ToListAsync();
                
                var currentMod = modulesList.FirstOrDefault(m => m.ModuleId == exam.ModuleId.Value);
                if (currentMod != null)
                {
                    var courseExams = await _db.Exams
                        .Where(e => e.CourseId == courseId)
                        .ToListAsync();

                    var courseExamIds = courseExams.Select(ce => ce.ExamId).ToList();

                    var userExams = await _db.UserExams
                        .Where(ue => ue.UserId == userId && ue.IsFinish == true && ue.ExamId.HasValue && courseExamIds.Contains(ue.ExamId.Value))
                        .ToListAsync();

                    bool isLocked = false;
                    string lockReason = "";
                    
                    var currentIndex = modulesList.IndexOf(currentMod);
                    for (int j = 0; j < currentIndex; j++)
                    {
                        var prevMod = modulesList[j];
                        var prevModExam = courseExams.FirstOrDefault(e => e.ModuleId == prevMod.ModuleId);
                        if (prevModExam != null)
                        {
                            var bestScore = userExams
                                .Where(ue => ue.ExamId == prevModExam.ExamId)
                                .Select(ue => ue.Score ?? 0)
                                .DefaultIfEmpty(0m)
                                .Max();
                            
                            var passScore = prevModExam.PassScore ?? 50m;
                            if (bestScore < passScore)
                            {
                                isLocked = true;
                                lockReason = $"Cần hoàn thành bài thi '{prevModExam.ExamTitle}' của chương trước đạt {passScore}%";
                                break;
                            }
                        }
                    }

                    if (isLocked)
                    {
                        return BadRequest(new { error = lockReason });
                    }
                }
            }

            var session = await GetOrCreateQuizSessionAsync(userId.Value, exam);
            var answers = await _db.UserAnswers.Where(a => a.UserExamId == session.UserExam.UserExamId).ToListAsync();
            var answerMap = answers.ToDictionary(a => a.QuestionId, a => a.OptionId);

            var questions = exam.ExamQuestions
                .OrderBy(eq => eq.QuestionId)
                .Select((eq, index) => new
                {
                    index,
                    questionId = eq.QuestionId,
                    questionText = eq.Question?.QuestionText ?? "Cau hoi khong co noi dung",
                    points = eq.Points ?? 0,
                    selectedOptionId = answerMap.TryGetValue(eq.QuestionId, out var selectedOptionId) ? selectedOptionId : null,
                    options = (eq.Question?.QuestionOptions ?? new List<QuestionOption>())
                        .OrderBy(o => o.OptionId)
                        .Select(o => new
                        {
                            optionId = o.OptionId,
                            optionText = o.OptionText
                        }).ToList()
                }).ToList();

            return Json(new
            {
                userExamId = session.UserExam.UserExamId,
                courseId = exam.CourseId,
                courseTitle = exam.Course?.Title ?? "Khoa hoc",
                examId = exam.ExamId,
                examTitle = exam.ExamTitle ?? "Bai kiem tra",
                durationMinutes = exam.DurationMinutes ?? 30,
                passScore = exam.PassScore ?? 50,
                currentQuestionIndex = session.State.CurrentQuestionIndex,
                remainingSeconds = session.State.RemainingSeconds,
                answeredCount = session.State.AnsweredCount,
                savedAt = session.State.LastSavedAt,
                questions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetQuizSession: " + ex.Message);
            return StatusCode(500, new { error = "Loi may chu: " + ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [HttpPost("/api/student/save-quiz-state")]
    public async Task<IActionResult> SaveQuizState([FromBody] SaveQuizStateDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var userExam = await _db.UserExams
            .Include(ue => ue.Exam)
            .FirstOrDefaultAsync(ue => ue.UserExamId == dto.UserExamId && ue.UserId == userId);
        if (userExam?.ExamId == null) return NotFound();
        if (userExam.IsFinish == true) return BadRequest(new { error = "Bai kiem tra nay da duoc nop." });

        var normalizedAnswers = NormalizeAnswers(dto.Answers);
        await SyncUserAnswersAsync(userExam.UserExamId, normalizedAnswers);

        var session = await _db.QuizSessionStates.FirstOrDefaultAsync(s => s.UserExamId == userExam.UserExamId);
        if (session == null)
        {
            session = new QuizSessionState { UserExamId = userExam.UserExamId };
            _db.QuizSessionStates.Add(session);
        }

        session.CurrentQuestionIndex = Math.Max(dto.CurrentQuestionIndex, 0);
        session.RemainingSeconds = Math.Max(dto.RemainingSeconds, 0);
        session.AnsweredCount = normalizedAnswers.Count;
        session.SavedAnswersJson = JsonSerializer.Serialize(normalizedAnswers);
        session.LastActivityAt = DateTime.Now;
        session.LastSavedAt = DateTime.Now;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            savedAt = session.LastSavedAt,
            answeredCount = session.AnsweredCount
        });
    }

    [HttpPost("/api/student/submit-quiz")]
    public async Task<IActionResult> SubmitQuiz([FromBody] SubmitQuizDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var userExam = await _db.UserExams
            .Include(ue => ue.Exam)
                .ThenInclude(e => e!.ExamQuestions)
                    .ThenInclude(eq => eq.Question)
                        .ThenInclude(q => q.QuestionOptions)
            .FirstOrDefaultAsync(ue => ue.UserExamId == dto.UserExamId && ue.UserId == userId);
        if (userExam?.Exam == null) return NotFound();

        var normalizedAnswers = NormalizeAnswers(dto.Answers);
        await SyncUserAnswersAsync(userExam.UserExamId, normalizedAnswers);

        var userAnswers = await _db.UserAnswers.Where(a => a.UserExamId == userExam.UserExamId).ToListAsync();
        var userAnswerMap = userAnswers.ToDictionary(a => a.QuestionId, a => a);

        decimal totalPoints = 0;
        decimal earnedPoints = 0;
        int correctCount = 0;
        foreach (var examQuestion in userExam.Exam.ExamQuestions)
        {
            var questionPoints = examQuestion.Points ?? 0;
            totalPoints += questionPoints;
            if (userAnswerMap.TryGetValue(examQuestion.QuestionId, out var answer) && answer.IsCorrect == true)
            {
                earnedPoints += questionPoints;
                correctCount++;
            }
        }

        var score = totalPoints > 0 
            ? Math.Round(earnedPoints / totalPoints * 100, 2) 
            : (userExam.Exam.ExamQuestions.Count > 0 ? Math.Round((decimal)correctCount / userExam.Exam.ExamQuestions.Count * 100, 2) : 0m);
        
        // Ensure we don't exceed 100% just in case
        if (score > 100) score = 100;
        
        userExam.Score = score;
        userExam.IsFinish = true;
        userExam.EndTime = DateTime.Now;

        var state = await _db.QuizSessionStates.FirstOrDefaultAsync(s => s.UserExamId == userExam.UserExamId);
        if (state == null)
        {
            state = new QuizSessionState { UserExamId = userExam.UserExamId };
            _db.QuizSessionStates.Add(state);
        }

        state.CurrentQuestionIndex = Math.Max(dto.CurrentQuestionIndex, 0);
        state.RemainingSeconds = Math.Max(dto.RemainingSeconds, 0);
        state.AnsweredCount = normalizedAnswers.Count;
        state.SavedAnswersJson = JsonSerializer.Serialize(normalizedAnswers);
        state.LastActivityAt = DateTime.Now;
        state.LastSavedAt = DateTime.Now;
        state.SubmittedAt = DateTime.Now;

        await _db.SaveChangesAsync();

        // Cập nhật lại tiến độ tổng quát (bao gồm cả bài thi vừa xong)
        if (userExam.Exam?.CourseId != null)
        {
            await RecalculateEnrollmentProgressAsync(userId.Value, userExam.Exam.CourseId.Value);
            await _db.SaveChangesAsync();

            // Kiểm tra cấp chứng chỉ sau khi nộp bài thi
            await CheckAndIssueCertificateAsync(userId.Value, userExam.Exam.CourseId.Value);
        }

        return Ok(new { success = true, userExamId = userExam.UserExamId, score });
    }

    [HttpGet("/api/student/quiz-results/{userExamId}")]
    public async Task<IActionResult> QuizResultData(int userExamId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var userExam = await _db.UserExams
            .Include(ue => ue.Exam)
                .ThenInclude(e => e!.Course)
            .Include(ue => ue.Exam)
                .ThenInclude(e => e!.ExamQuestions)
                    .ThenInclude(eq => eq.Question)
                        .ThenInclude(q => q.QuestionOptions)
            .FirstOrDefaultAsync(ue => ue.UserExamId == userExamId && ue.UserId == userId);
        if (userExam?.Exam?.CourseId == null) return NotFound();

        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == userExam.Exam.CourseId);
        if (enrollment == null) return Forbid();

        var answers = await _db.UserAnswers.Where(a => a.UserExamId == userExamId).ToListAsync();
        var answerMap = answers.ToDictionary(a => a.QuestionId, a => a);

        decimal totalPoints = 0;
        decimal correctPoints = 0;
        var details = new List<object>();
        var correctCount = 0;
        var incorrectCount = 0;
        var unansweredCount = 0;

        foreach (var examQuestion in userExam.Exam.ExamQuestions.OrderBy(eq => eq.QuestionId))
        {
            totalPoints += examQuestion.Points ?? 0;
            answerMap.TryGetValue(examQuestion.QuestionId, out var answer);
            var selectedOptionId = answer?.OptionId;
            var correctOption = examQuestion.Question.QuestionOptions.FirstOrDefault(o => o.IsCorrect == true);
            var isCorrect = answer?.IsCorrect == true;
            var isAnswered = selectedOptionId.HasValue;

            if (isCorrect)
            {
                correctCount++;
                correctPoints += examQuestion.Points ?? 0;
            }
            else if (isAnswered)
            {
                incorrectCount++;
            }
            else
            {
                unansweredCount++;
            }

            details.Add(new
            {
                questionId = examQuestion.QuestionId,
                questionText = examQuestion.Question.QuestionText,
                points = examQuestion.Points ?? 0,
                isCorrect,
                isAnswered,
                selectedOptionId,
                correctOptionId = correctOption?.OptionId,
                correctOptionText = correctOption?.OptionText,
                options = examQuestion.Question.QuestionOptions.OrderBy(o => o.OptionId).Select(o => new
                {
                    optionId = o.OptionId,
                    optionText = o.OptionText,
                    isCorrect = o.IsCorrect,
                    isSelected = selectedOptionId == o.OptionId
                })
            });
        }

        var finishedScores = await _db.UserExams
            .Include(ue => ue.Exam)
            .Where(ue => ue.UserId == userId && ue.Exam != null && ue.Exam.CourseId == userExam.Exam.CourseId && ue.IsFinish == true)
            .Select(ue => ue.Score ?? 0)
            .ToListAsync();

        var evaluation = BuildCourseEvaluation(enrollment.ProgressPercent ?? 0, finishedScores);
        
        // Ensure consistent pass check: compare percentage to percentage threshold
        // By default we assume PassScore is the percentage threshold (e.g. 50%)
        var currentScore = userExam.Score ?? 0;
        var threshold = userExam.Exam.PassScore ?? 50;
        var passedExam = currentScore >= threshold;

        return Json(new
        {
            userExamId = userExam.UserExamId,
            courseId = userExam.Exam.CourseId,
            courseTitle = userExam.Exam.Course?.Title ?? "Khoa hoc",
            examId = userExam.ExamId,
            examTitle = userExam.Exam.ExamTitle ?? "Bai kiem tra",
            score = currentScore,
            isPassed = passedExam,
            passScore = threshold,
            startTime = userExam.StartTime,
            endTime = userExam.EndTime,
            questions = details,
            totalQuestions = userExam.Exam.ExamQuestions.Count,
            correctCount,
            incorrectCount,
            unansweredCount,
            totalPoints,
            correctPoints,
            evaluation = new
            {
                progressWeight = 40,
                quizWeight = 60,
                lessonCompletionPercent = evaluation.ProgressPercent,
                quizAverage = evaluation.QuizAverage,
                weightedScore = evaluation.WeightedScore,
                ranking = evaluation.Ranking,
                isPassed = evaluation.IsPassed,
                note = evaluation.Note
            },
            details
        });
    }

    [HttpPost("/api/student/ask-ai")]
    public async Task<IActionResult> AskAI([FromBody] StudentQuestionDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == dto.CourseId);
        if (!isEnrolled) return Forbid();

        var course = await _db.Courses.FindAsync(dto.CourseId);
        var lesson = await _db.Lessons.FindAsync(dto.LessonId);

        var courseTitle = course?.Title ?? "Khoa hoc";
        var lessonContext = lesson?.Title ?? "Kien thuc chung";
        if (lesson != null && !string.IsNullOrEmpty(lesson.ContentBody))
        {
            lessonContext += " - Noi dung tom tat: " + lesson.ContentBody;
        }

        if (lessonContext.Length > 2000) lessonContext = lessonContext[..2000];

        var answer = await _aiService.AnswerStudentQuestionAsync(courseTitle, lessonContext, dto.Question);
        return Ok(new { answer });
    }

    // ============================================================
    // API: �?I M?T KH?U
    // ============================================================
    [HttpPost("/api/student/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var user = await _db.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        // Ki?m tra m?t kh?u cu
        var oldHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dto.OldPassword));
        if (!Convert.ToHexString(user.PasswordHash ?? []).Equals(Convert.ToHexString(oldHash), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Mat khau cu khong chinh xac." });

        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            return BadRequest(new { error = "Mat khau moi phai co it nhat 6 ky tu." });

        user.PasswordHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dto.NewPassword));
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: C?P NH?T H? SO C� NH�N
    // ============================================================
    [HttpGet("/api/student/profile")]
    public async Task<IActionResult> GetProfile()
    {
        try {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var user = await _db.Users
                .Include(u => u.Department)
                .Include(u => u.JobTitle)
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            if (user == null) return NotFound();

            return Json(new
            {
                userId = user.UserId,
                username = user.Username,
                fullName = user.FullName,
                email = user.Email,
                department = user.Department?.DepartmentName ?? "N/A",
                jobTitle = user.JobTitle?.TitleName ?? "N/A",
                employeeCode = user.EmployeeCode,
                status = user.Status
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Profile Error");
            return StatusCode(500, new { error = "Lỗi Profile: " + ex.Message });
        }
    }

    [HttpPut("/api/student/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var user = await _db.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.FullName)) user.FullName = dto.FullName;
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            user.Email = dto.Email.Split('@')[0].Trim().ToLower() + "@basau.net";
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: L? TR�NH H?C (LEARNING PATHS)
    // ============================================================
    [HttpGet("/api/student/learning-paths")]
    public async Task<IActionResult> GetLearningPaths()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var paths = await _db.LearningPaths
            .Include(p => p.PathCourses)
                .ThenInclude(pc => pc.Course)
            .Include(p => p.UserPathProgresses.Where(upp => upp.UserId == userId.Value))
            .ToListAsync();

        var result = paths.Select(p =>
        {
            var progress = p.UserPathProgresses.FirstOrDefault();
            var courses = p.PathCourses.OrderBy(pc => pc.StepOrder).Select(pc => new
            {
                courseId = pc.CourseId,
                title = pc.Course?.Title ?? "N/A",
                stepOrder = pc.StepOrder
            });
            return new
            {
                pathId = p.PathId,
                pathName = p.PathName,
                description = p.Description,
                totalCourses = p.PathCourses.Count,
                status = progress?.Status ?? "NotStarted",
                percentComplete = progress?.PercentComplete ?? 0,
                courses
            };
        });

        return Json(result);
    }

    [HttpPost("/api/student/learning-paths/{pathId}/start")]
    public async Task<IActionResult> StartLearningPath(int pathId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var path = await _db.LearningPaths.FindAsync(pathId);
        if (path == null) return NotFound();

        var existing = await _db.UserPathProgresses.FindAsync(userId.Value, pathId);
        if (existing == null)
        {
            _db.UserPathProgresses.Add(new UserPathProgress
            {
                UserId = userId.Value,
                PathId = pathId,
                Status = "InProgress",
                PercentComplete = 0
            });
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    // ============================================================
    // API: TH�NG B�O (NOTIFICATIONS)
    // ============================================================
    [HttpGet("/api/student/notifications")]
    public async Task<IActionResult> GetNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var notifications = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.Id)
            .Take(30)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                isRead = n.IsRead ?? false
            })
            .ToListAsync();

        var unreadCount = notifications.Count(n => !n.isRead);

        return Json(new { notifications, unreadCount });
    }

    [HttpPost("/api/student/notifications/{id}/read")]
    public async Task<IActionResult> MarkNotificationRead(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notif == null) return NotFound();

        notif.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/student/notifications/read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var unread = await _db.Notifications.Where(n => n.UserId == userId && n.IsRead != true).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: T�I LI?U ��NH K�M B�I H?C (LESSON ATTACHMENTS)
    // ============================================================
    [HttpGet("/api/student/lessons/{lessonId}/attachments")]
    public async Task<IActionResult> GetLessonAttachments(int lessonId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        // Ki?m tra h?c vi�n c� dang k� kh�a h?c ch?a b�i h?c n�y kh�ng
        var lesson = await _db.Lessons.Include(l => l.Module).FirstOrDefaultAsync(l => l.LessonId == lessonId);
        if (lesson?.Module?.CourseId == null) return NotFound();

        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == lesson.Module.CourseId);
        if (!isEnrolled) return Forbid();

        var attachments = await _db.LessonAttachments
            .Where(a => a.LessonId == lessonId)
            .Select(a => new
            {
                attachmentId = a.AttachmentId,
                fileName = a.FileName,
                filePath = a.FilePath
            })
            .ToListAsync();

        return Json(attachments);
    }

    // ============================================================
    // API: B�NH LU?N B�I H?C (LESSON COMMENTS)
    // ============================================================
    [HttpGet("/api/student/lessons/{lessonId}/comments")]
    public async Task<IActionResult> GetLessonComments(int lessonId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var comments = await _db.LessonComments
            .Include(c => c.UserId != null ? _db.Users.FirstOrDefault(u => u.UserId == c.UserId) : null)
            .Where(c => c.LessonId == lessonId)
            .OrderByDescending(c => c.Id)
            .Take(50)
            .ToListAsync();

        // L?y t�n user ri�ng
        var userIds = comments.Select(c => c.UserId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName ?? u.Username ?? "?n danh");

        var result = comments.Select(c => new
        {
            id = c.Id,
            content = c.Content,
            userName = c.UserId.HasValue && users.ContainsKey(c.UserId.Value) ? users[c.UserId.Value] : "?n danh",
            isOwn = c.UserId == userId.Value
        });

        return Json(result);
    }

    [HttpPost("/api/student/lessons/{lessonId}/comments")]
    public async Task<IActionResult> AddLessonComment(int lessonId, [FromBody] AddCommentDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var lesson = await _db.Lessons.Include(l => l.Module).FirstOrDefaultAsync(l => l.LessonId == lessonId);
        if (lesson?.Module?.CourseId == null) return NotFound();

        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == lesson.Module.CourseId);
        if (!isEnrolled) return Forbid();

        if (string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { error = "Noi dung binh luan khong duoc trong." });

        var comment = new LessonComment
        {
            LessonId = lessonId,
            UserId = userId.Value,
            Content = dto.Content.Trim()
        };
        _db.LessonComments.Add(comment);
        await _db.SaveChangesAsync();

        var user = await _db.Users.FindAsync(userId.Value);
        return Ok(new { success = true, id = comment.Id, userName = user?.FullName ?? "Bạn" });
    }

    // ============================================================
    // API: SỰ KIỆN ĐÀO TẠO OFFLINE
    // ============================================================
    [HttpGet("/api/student/offline-events")]
    public async Task<IActionResult> GetOfflineEvents()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var events = await _db.OfflineTrainingEvents
            .Include(e => e.Course)
            .Include(e => e.AttendanceLogs)
            .OrderBy(e => e.StartTime)
            .Select(e => new
            {
                eventId = e.EventId,
                title = e.Title,
                courseTitle = e.Course != null ? e.Course.Title : "N/A",
                location = e.Location,
                instructor = e.Instructor,
                shift = e.Shift,
                startTime = e.StartTime,
                endTime = e.EndTime,
                attendanceStartTime = e.AttendanceStartTime,
                attendanceEndTime = e.AttendanceEndTime,
                departmentId = e.DepartmentId,
                isRegistered = e.AttendanceLogs.Any(a => a.UserId == userId.Value),
                attendanceStatus = e.AttendanceLogs.Where(a => a.UserId == userId.Value).Select(a => a.AttendanceStatus).FirstOrDefault()
            })
            .ToListAsync();

        return Json(events);
    }

    [HttpPost("/api/student/checkin/{eventId}")]
    public async Task<IActionResult> Checkin(int eventId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var ev = await _db.OfflineTrainingEvents.FindAsync(eventId);
        if (ev == null) return NotFound();

        var now = DateTime.Now;
        if (ev.AttendanceStartTime.HasValue && now < ev.AttendanceStartTime.Value)
            return BadRequest(new { error = "Chưa đến giờ điểm danh." });
        if (ev.AttendanceEndTime.HasValue && now > ev.AttendanceEndTime.Value)
            return BadRequest(new { error = "Đã hết giờ điểm danh." });

        var log = await _db.AttendanceLogs.FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == userId.Value);
        if (log == null)
        {
            log = new AttendanceLog { 
                EventId = eventId, 
                UserId = userId.Value, 
                Status = true, 
                AttendanceStatus = "Present",
                CheckInTime = DateTime.Now
            };
            _db.AttendanceLogs.Add(log);
        }
        else
        {
            if (log.AttendanceStatus == "Present") return BadRequest(new { error = "Bạn đã điểm danh rồi." });
            log.Status = true;
            log.AttendanceStatus = "Present";
            log.CancelReason = null;
            log.CheckInTime = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/student/offline-events/{eventId}/register")]
    public async Task<IActionResult> RegisterOfflineEvent(int eventId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var ev = await _db.OfflineTrainingEvents
            .Include(e => e.AttendanceLogs)
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        if (ev == null) return NotFound();

        if (ev.AttendanceLogs.Any(a => a.UserId == userId.Value))
            return BadRequest(new { error = "Bạn đã đăng ký sự kiện này rồi." });

        var userDeptId = HttpContext.Session.GetString("DepartmentID");
        if (ev.DepartmentId.HasValue && userDeptId != null && ev.DepartmentId.Value.ToString() != userDeptId)
            return BadRequest(new { error = "Sự kiện này chỉ dành cho phòng ban khác." });

        _db.AttendanceLogs.Add(new AttendanceLog
        {
            EventId = eventId,
            UserId = userId.Value,
            Status = true,
            AttendanceStatus = "Registered"
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: KH?O S�T (SURVEYS)
    // ============================================================
    // NEW: API Lấy lịch làm bài kiểm tra
    [HttpGet("/api/student/upcoming-quizzes")]
    public async Task<IActionResult> GetUpcomingQuizzes()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var enrolledCourseIds = await _db.Enrollments
            .Where(e => e.UserId == userId.Value && e.Status != "Completed")
            .Select(e => e.CourseId)
            .ToListAsync();

        var exams = await _db.Exams
            .Include(e => e.Course)
            .Where(e => enrolledCourseIds.Contains(e.CourseId) && e.EndDate > DateTime.Now)
            .Select(e => new
            {
                examId = e.ExamId,
                title = e.ExamTitle,
                courseTitle = e.Course != null ? e.Course.Title : "N/A",
                startTime = e.StartDate,
                endTime = e.EndDate,
                passScore = e.PassScore,
                duration = e.DurationMinutes
            })
            .ToListAsync();

        return Json(exams);
    }

    [HttpGet("/api/student/surveys")]
    public async Task<IActionResult> GetSurveys()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var surveys = await _db.Surveys
            .Include(s => s.SurveyResults)
            .OrderByDescending(s => s.SurveyId)
            .Select(s => new
            {
                surveyId = s.SurveyId,
                title = s.Title,
                description = s.Description,
                expiredDate = s.ExpiredDate,
                isCompleted = s.SurveyResults.Any(r => r.UserId == userId.Value),
                isExpired = s.ExpiredDate != null && s.ExpiredDate < DateTime.Now
            })
            .ToListAsync();

        return Json(surveys);
    }

    [HttpPost("/api/student/surveys/{surveyId}/submit")]
    public async Task<IActionResult> SubmitSurvey(int surveyId, [FromBody] SurveySubmitDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var survey = await _db.Surveys.FindAsync(surveyId);
        if (survey == null) return NotFound();

        if (survey.ExpiredDate.HasValue && survey.ExpiredDate < DateTime.Now)
            return BadRequest(new { error = "Khao sat da het han." });

        var existing = await _db.SurveyResults.FirstOrDefaultAsync(r => r.SurveyId == surveyId && r.UserId == userId);
        if (existing != null)
            return BadRequest(new { error = "Ban da hoan thanh khao sat nay roi." });

        _db.SurveyResults.Add(new SurveyResult
        {
            SurveyId = surveyId,
            UserId = userId.Value,
            AnswerData = dto.AnswerData,
            SubmittedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: PH?N H?I KH�A H?C (COURSE FEEDBACK / RATING)
    // ============================================================
    [HttpGet("/api/student/courses/{courseId}/feedback")]
    public async Task<IActionResult> GetCourseFeedback(int courseId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var myFeedback = await _db.CourseFeedbacks
            .FirstOrDefaultAsync(f => f.CourseId == courseId && f.UserId == userId);

        var avgRating = await _db.CourseFeedbacks
            .Where(f => f.CourseId == courseId && f.Rating != null)
            .AverageAsync(f => (double?)f.Rating) ?? 0;

        var totalReviews = await _db.CourseFeedbacks.CountAsync(f => f.CourseId == courseId);

        var recentReviews = await _db.CourseFeedbacks
            .Include(f => f.User)
            .Where(f => f.CourseId == courseId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(10)
            .Select(f => new
            {
                rating = f.Rating,
                comment = f.Comment,
                userName = f.User != null ? f.User.FullName : "?n danh",
                createdAt = f.CreatedAt
            })
            .ToListAsync();

        return Json(new
        {
            myRating = myFeedback?.Rating,
            myComment = myFeedback?.Comment,
            hasReviewed = myFeedback != null,
            avgRating = Math.Round(avgRating, 1),
            totalReviews,
            recentReviews
        });
    }

    [HttpPost("/api/student/courses/{courseId}/feedback")]
    public async Task<IActionResult> SubmitCourseFeedback(int courseId, [FromBody] CourseFeedbackDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (!isEnrolled) return Forbid();

        if (dto.Rating < 1 || dto.Rating > 5)
            return BadRequest(new { error = "Danh gia phai tu 1 den 5 sao." });

        var existing = await _db.CourseFeedbacks.FirstOrDefaultAsync(f => f.CourseId == courseId && f.UserId == userId);
        if (existing != null)
        {
            existing.Rating = dto.Rating;
            existing.Comment = dto.Comment;
            existing.CreatedAt = DateTime.Now;
        }
        else
        {
            _db.CourseFeedbacks.Add(new CourseFeedback
            {
                CourseId = courseId,
                UserId = userId.Value,
                Rating = dto.Rating,
                Comment = dto.Comment,
                CreatedAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ============================================================
    // API: L?CH S? THI (ALL EXAM ATTEMPTS)
    // ============================================================
    [HttpGet("/api/student/exam-history")]
    public async Task<IActionResult> ExamHistory()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var history = await _db.UserExams
            .Include(ue => ue.Exam)
                .ThenInclude(e => e!.Course)
            .Where(ue => ue.UserId == userId)
            .OrderByDescending(ue => ue.StartTime)
            .Select(ue => new
            {
                userExamId = ue.UserExamId,
                examTitle = ue.Exam != null ? ue.Exam.ExamTitle : "N/A",
                courseTitle = ue.Exam != null && ue.Exam.Course != null ? ue.Exam.Course.Title : "N/A",
                courseId = ue.Exam != null ? ue.Exam.CourseId : null,
                score = ue.Score ?? 0,
                passScore = ue.Exam != null ? (ue.Exam.PassScore ?? 50) : 50,
                isPassed = (ue.Score ?? 0) >= (ue.Exam != null ? (ue.Exam.PassScore ?? 50) : 50),
                isFinish = ue.IsFinish ?? false,
                startTime = ue.StartTime,
                endTime = ue.EndTime
            })
            .ToListAsync();

        return Json(history);
    }

    private async Task<int> RecalculateEnrollmentProgressAsync(int userId, int courseId)
    {
        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment == null) return 0;

        // Tổng số mục cần hoàn thành = Bài học + Bài thi
        var totalLessons = await _db.Lessons
            .Where(l => _db.CourseModules.Any(m => m.ModuleId == l.ModuleId && m.CourseId == courseId))
            .CountAsync();
            
        var totalExams = await _db.Exams
            .Where(e => e.CourseId == courseId)
            .CountAsync();
            
        var totalItems = totalLessons + totalExams;

        // Đã hoàn thành (Bài học + Bài thi đã nộp)
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

        return progressPercent;
    }

    private async Task<(UserExam UserExam, QuizSessionState State)> GetOrCreateQuizSessionAsync(int userId, Exam exam)
    {
        var userExam = await _db.UserExams
            .OrderByDescending(ue => ue.StartTime)
            .FirstOrDefaultAsync(ue => ue.UserId == userId && ue.ExamId == exam.ExamId && ue.IsFinish != true);

        if (userExam == null)
        {
            userExam = new UserExam
            {
                UserId = userId,
                ExamId = exam.ExamId,
                Score = 0,
                IsFinish = false,
                StartTime = DateTime.Now
            };
            _db.UserExams.Add(userExam);
            await _db.SaveChangesAsync();
        }

        var state = await _db.QuizSessionStates.FirstOrDefaultAsync(s => s.UserExamId == userExam.UserExamId);
        if (state == null)
        {
            state = new QuizSessionState
            {
                UserExamId = userExam.UserExamId,
                CurrentQuestionIndex = 0,
                RemainingSeconds = Math.Max((exam.DurationMinutes ?? 30) * 60, 0),
                AnsweredCount = 0,
                SavedAnswersJson = "{}",
                LastActivityAt = DateTime.Now,
                LastSavedAt = DateTime.Now
            };
            _db.QuizSessionStates.Add(state);
            await _db.SaveChangesAsync();
        }

        return (userExam, state);
    }

    private async Task SyncUserAnswersAsync(int userExamId, Dictionary<int, int?> answers)
    {
        var existingAnswers = await _db.UserAnswers.Where(a => a.UserExamId == userExamId).ToListAsync();

        var selectedOptionIds = answers.Values
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

        var correctLookup = await _db.QuestionOptions
            .Where(o => selectedOptionIds.Contains(o.OptionId))
            .ToDictionaryAsync(o => o.OptionId, o => o.IsCorrect == true);

        foreach (var existing in existingAnswers)
        {
            if (!answers.TryGetValue(existing.QuestionId, out var optionId) || !optionId.HasValue)
            {
                _db.UserAnswers.Remove(existing);
                continue;
            }

            existing.OptionId = optionId.Value;
            existing.IsCorrect = correctLookup.TryGetValue(optionId.Value, out var isCorrect) && isCorrect;
        }

        var existingQuestionIds = existingAnswers.Select(a => a.QuestionId).ToHashSet();
        foreach (var answer in answers)
        {
            if (!answer.Value.HasValue || existingQuestionIds.Contains(answer.Key)) continue;

            _db.UserAnswers.Add(new UserAnswer
            {
                UserExamId = userExamId,
                QuestionId = answer.Key,
                OptionId = answer.Value.Value,
                IsCorrect = correctLookup.TryGetValue(answer.Value.Value, out var isCorrect) && isCorrect
            });
        }
    }

    private static Dictionary<int, int?> NormalizeAnswers(Dictionary<int, int?>? rawAnswers)
    {
        if (rawAnswers == null) return [];
        return rawAnswers
            .Where(kvp => kvp.Key > 0 && kvp.Value.GetValueOrDefault() >= 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static CourseEvaluationResult BuildCourseEvaluation(int progressPercent, IEnumerable<decimal> quizScores)
    {
        var scoreList = quizScores.ToList();
        var quizAverage = scoreList.Count > 0 ? Math.Round(scoreList.Average(), 2) : 0;
        // Tiến độ đóng góp 50%, Điểm thi đóng góp 50%
        var weightedScore = Math.Round(progressPercent * 0.5m + quizAverage * 0.5m, 2);

        string ranking;
        if (weightedScore >= 90) ranking = "Xuất sắc";
        else if (weightedScore >= 80) ranking = "Giỏi";
        else if (weightedScore >= 70) ranking = "Khá";
        else if (weightedScore >= 50) ranking = "Trung bình";
        else ranking = "Không đạt";

        var isPassed = weightedScore >= 50;
        var note = isPassed 
            ? $"Chúc mừng! Bạn đã hoàn thành khóa học với xếp loại: {ranking}."
            : "Bạn cần cố gắng hơn để đạt mức điểm hoàn thành khóa học (50%).";

        return new CourseEvaluationResult(progressPercent, quizAverage, weightedScore, ranking, isPassed, note);
    }

    private async Task CheckAndIssueCertificateAsync(int userId, int courseId)
    {
        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment == null || enrollment.ProgressPercent < 100) return;

        // Tính toán điểm trung bình quiz chính xác
        var allExams = await _db.Exams.Where(e => e.CourseId == courseId).Select(e => e.ExamId).ToListAsync();
        var quizScores = new List<decimal>();
        foreach (var exId in allExams)
        {
            var best = await _db.UserExams
                .Where(ue => ue.UserId == userId && ue.ExamId == exId && ue.IsFinish == true)
                .OrderByDescending(ue => ue.Score)
                .Select(ue => ue.Score ?? 0)
                .FirstOrDefaultAsync();
            quizScores.Add(best);
        }
        
        var eval = BuildCourseEvaluation(enrollment.ProgressPercent ?? 0, quizScores.Select(s => (decimal)s));
        
        // Điều kiện cấp chứng chỉ: Tiến độ 100% và Điểm tổng kết đạt (>= 50)
        if (eval.IsPassed)
        {
            var hasCert = await _db.Certificates.AnyAsync(c => c.UserId == userId && c.CourseId == courseId);
            if (!hasCert)
            {
                // 1. Tạo chứng chỉ
                var cert = new Certificate
                {
                    UserId = userId,
                    CourseId = courseId,
                    IssueDate = DateTime.Now,
                    CertCode = $"CERT-{courseId}-{userId}-{DateTime.Now.Ticks.ToString().Substring(10)}"
                };
                _db.Certificates.Add(cert);

                // 2. Cộng điểm thưởng (ví dụ 100 điểm)
                var userPoint = await _db.UserPoints.FirstOrDefaultAsync(up => up.UserId == userId);
                if (userPoint == null)
                {
                    userPoint = new UserPoint { UserId = userId, TotalPoints = 100, LastUpdated = DateTime.Now };
                    _db.UserPoints.Add(userPoint);
                }
                else
                {
                    userPoint.TotalPoints = (userPoint.TotalPoints ?? 0) + 100;
                    userPoint.LastUpdated = DateTime.Now;
                }

                // 3. Gửi thông báo (Sử dụng các trường hiện có trong DB của bạn)
                _db.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Title = $"Chúc mừng! Bạn đã hoàn thành khóa học và được cấp chứng chỉ mới.",
                    IsRead = false
                });

                await _db.SaveChangesAsync();
            }
        }
    }
}

public class StudentQuestionDto
{
    public int CourseId { get; set; }
    public int LessonId { get; set; }
    public string Question { get; set; } = "";
}

public class SaveQuizStateDto
{
    public int UserExamId { get; set; }
    public int CurrentQuestionIndex { get; set; }
    public int RemainingSeconds { get; set; }
    public Dictionary<int, int?>? Answers { get; set; }
}

public class SubmitQuizDto
{
    public int UserExamId { get; set; }
    public int CurrentQuestionIndex { get; set; }
    public int RemainingSeconds { get; set; }
    public Dictionary<int, int?>? Answers { get; set; }
}

public record CourseEvaluationResult(
    int ProgressPercent,
    decimal QuizAverage,
    decimal WeightedScore,
    string Ranking,
    bool IsPassed,
    string Note);

public class ChangePasswordDto
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
}

public class AddCommentDto
{
    public string Content { get; set; } = "";
}

public class SurveySubmitDto
{
    public string? AnswerData { get; set; }
}

public class CourseFeedbackDto
{
    public int Rating { get; set; }
    public string? Comment { get; set; }
}
