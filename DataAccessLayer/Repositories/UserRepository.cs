using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.DataAccessLayer.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(CorporateLmsProContext context) : base(context)
    {
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _dbSet
            .Include(u => u.Department)
            .Include(u => u.JobTitle)
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetUserWithRolesAndPermissionsAsync(int userId)
    {
        return await _dbSet
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }
}
