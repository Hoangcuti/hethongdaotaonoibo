using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.DataAccessLayer.Repositories;

public interface ICourseRepository : IRepository<Course>
{
    Task<Course?> GetCourseWithDetailsAsync(int courseId);
}
