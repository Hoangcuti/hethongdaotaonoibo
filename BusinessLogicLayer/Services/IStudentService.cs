using System.Collections.Generic;
using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.BusinessLogicLayer.Services;

public interface IStudentService
{
    Task<object> GetDashboardDataAsync(int userId);
    Task<IEnumerable<object>> GetCoursesForStudentAsync(int userId, int? departmentId, string? search, int? categoryId);
    Task<IEnumerable<object>> GetCertificatesAsync(int userId);
    Task<object> GetAchievementsAsync(int userId);
    Task<object?> GetCourseDetailsAsync(int courseId, int userId, int? departmentId);
    Task<bool> EnrollInCourseAsync(int userId, int courseId, int? departmentId);
}
