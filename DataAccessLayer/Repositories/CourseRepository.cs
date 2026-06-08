using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.DataAccessLayer.Repositories;

public class CourseRepository : Repository<Course>, ICourseRepository
{
    public CourseRepository(CorporateLmsProContext context) : base(context)
    {
    }

    public async Task<Course?> GetCourseWithDetailsAsync(int courseId)
    {
        return await _dbSet
            .Include(c => c.CourseModules)
                .ThenInclude(m => m.Lessons)
            .FirstOrDefaultAsync(c => c.CourseId == courseId);
    }
}
