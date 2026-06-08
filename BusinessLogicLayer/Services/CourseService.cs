using System.Collections.Generic;
using System.Threading.Tasks;
using KhoaHoc.DataAccessLayer.Repositories;
using KhoaHoc.Models;

namespace KhoaHoc.BusinessLogicLayer.Services;

public class CourseService : ICourseService
{
    private readonly ICourseRepository _courseRepository;

    public CourseService(ICourseRepository courseRepository)
    {
        _courseRepository = courseRepository;
    }

    public async Task<Course?> GetCourseWithDetailsAsync(int courseId)
    {
        return await _courseRepository.GetCourseWithDetailsAsync(courseId);
    }

    public async Task<IEnumerable<Course>> GetAllCoursesAsync()
    {
        return await _courseRepository.GetAllAsync();
    }
}
