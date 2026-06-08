using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.DataAccessLayer.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetUserWithRolesAndPermissionsAsync(int userId);
}
