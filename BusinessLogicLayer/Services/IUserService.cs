using System.Threading.Tasks;
using KhoaHoc.Models;

namespace KhoaHoc.BusinessLogicLayer.Services;

public interface IUserService
{
    Task<User?> AuthenticateAsync(string username, string password);
    Task<User?> GetUserByIdAsync(int userId);
    Task<User?> GetUserWithRolesAndPermissionsAsync(int userId);
    Task<User?> GetActiveUserByUsernameAsync(string username);
}
