using System.Collections.Generic;
using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.BusinessLogicLayer.Services;

public interface ICourseService
{
    Task<Course?> GetCourseWithDetailsAsync(int courseId);
    Task<IEnumerable<Course>> GetAllCoursesAsync();
}
