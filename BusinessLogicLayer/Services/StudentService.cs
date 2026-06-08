using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KhoaHoc.DataAccessLayer.Repositories;
using KhoaHoc.Models;

namespace KhoaHoc.BusinessLogicLayer.Services;

public class StudentService : IStudentService
{
    private static readonly string[] VisibleCourseStatuses = ["Published", "Active"];

    private readonly IRepository<Course> _courseRepository;
    private readonly IRepository<Enrollment> _enrollmentRepository;
    private readonly IRepository<Certificate> _certificateRepository;
    private readonly IRepository<UserPoint> _userPointRepository;
    private readonly IRepository<UserBadge> _userBadgeRepository;

    public StudentService(
        IRepository<Course> courseRepository,
        IRepository<Enrollment> enrollmentRepository,
        IRepository<Certificate> certificateRepository,
        IRepository<UserPoint> userPointRepository,
        IRepository<UserBadge> userBadgeRepository)
    {
        _courseRepository = courseRepository;
        _enrollmentRepository = enrollmentRepository;
        _certificateRepository = certificateRepository;
        _userPointRepository = userPointRepository;
        _userBadgeRepository = userBadgeRepository;
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

    public async Task<object> GetDashboardDataAsync(int userId)
    {
        var enrollments = await _enrollmentRepository.Query()
            .Include(e => e.Course)
            .Where(e => e.UserId == userId)
            .ToListAsync();

        var certificates = await _certificateRepository.Query()
            .Where(c => c.UserId == userId)
            .CountAsync();

        var recentCourses = enrollments
            .OrderByDescending(e => e.EnrollDate)
            .Take(4)
            .Select(e => new
            {
                courseId = e.CourseId,
                title = e.Course?.Title ?? "N/A",
                progress = e.ProgressPercent ?? 0,
                status = e.Status
            })
            .ToList();

        return new
        {
            totalEnrolled = enrollments.Count,
            inProgress = enrollments.Count(e => e.Status == "InProgress"),
            completed = enrollments.Count(e => e.Status == "Completed"),
            certificates = certificates,
            totalPoints = 0,
            badges = 0,
            recentCourses = recentCourses
        };
    }

    public async Task<IEnumerable<object>> GetCoursesForStudentAsync(int userId, int? departmentId, string? search, int? categoryId)
    {
        var query = ApplyStudentCourseScope(_courseRepository.Query()
            .Include(c => c.Category)
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .Include(c => c.Exams)
            .Include(c => c.TrainingAssignments.Where(ta => ta.UserId == userId)), userId, departmentId);

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

        return courses;
    }

    public async Task<IEnumerable<object>> GetCertificatesAsync(int userId)
    {
        var certs = await _certificateRepository.Query()
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

        return certs;
    }

    public async Task<object> GetAchievementsAsync(int userId)
    {
        var userPoint = await _userPointRepository.Query()
            .FirstOrDefaultAsync(up => up.UserId == userId);

        var badges = await _userBadgeRepository.Query()
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

        var leaderboard = await _userPointRepository.Query()
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

        return new
        {
            totalPoints = userPoint?.TotalPoints ?? 0,
            badges,
            leaderboard
        };
    }

    public async Task<object?> GetCourseDetailsAsync(int courseId, int userId, int? departmentId)
    {
        var course = await ApplyStudentCourseScope(_courseRepository.Query()
            .Include(c => c.Category)
            .Include(c => c.CourseModules)
                .ThenInclude(m => m.Lessons)
            .Include(c => c.Exams)
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .Include(c => c.TrainingAssignments.Where(ta => ta.UserId == userId)), userId, departmentId)
            .FirstOrDefaultAsync(c => c.CourseId == courseId);

        if (course == null) return null;

        var isEnrolled = await _enrollmentRepository.Query()
            .AnyAsync(e => e.CourseId == courseId && e.UserId == userId);

        return new
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
        };
    }

    public async Task<bool> EnrollInCourseAsync(int userId, int courseId, int? departmentId)
    {
        var existing = await _enrollmentRepository.Query()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        
        if (existing != null) return false;

        var course = await ApplyStudentCourseScope(_courseRepository.Query()
            .Include(c => c.Enrollments.Where(e => e.UserId == userId))
            .Include(c => c.TrainingAssignments.Where(ta => ta.UserId == userId)), userId, departmentId)
            .FirstOrDefaultAsync(c => c.CourseId == courseId);
        
        if (course == null) return false;

        var enrollment = new Enrollment
        {
            UserId = userId,
            CourseId = courseId,
            EnrollDate = DateTime.Now,
            ProgressPercent = 0,
            Status = "NotStarted"
        };

        await _enrollmentRepository.AddAsync(enrollment);
        await _enrollmentRepository.SaveChangesAsync();

        return true;
    }
}
