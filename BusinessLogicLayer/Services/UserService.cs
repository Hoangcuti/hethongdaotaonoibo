using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KhoaHoc.DataAccessLayer.Repositories;
using KhoaHoc.Models;

namespace KhoaHoc.BusinessLogicLayer.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var searchUsername = username.Trim();
        if (searchUsername.Contains("@"))
        {
            searchUsername = searchUsername.Split('@')[0];
        }

        var user = await _userRepository.GetByUsernameAsync(searchUsername);

        if (user == null || user.Status != "Active")
        {
            return null;
        }

        bool passwordValid = false;
        if (user.PasswordHash != null)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(password.Trim());
            byte[] hashedInput = SHA256.HashData(inputBytes);

            string storedHashHex = Convert.ToHexString(user.PasswordHash);
            string inputHashHex = Convert.ToHexString(hashedInput);

            if (storedHashHex.Equals(inputHashHex, StringComparison.OrdinalIgnoreCase))
            {
                passwordValid = true;
            }
            else if (Encoding.UTF8.GetString(user.PasswordHash) == password.Trim())
            {
                passwordValid = true;
            }
        }

        if (!passwordValid)
        {
            return null;
        }

        // Update Last Login
        user.LastLogin = DateTime.Now;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        return user;
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _userRepository.GetByIdAsync(userId);
    }

    public async Task<User?> GetUserWithRolesAndPermissionsAsync(int userId)
    {
        return await _userRepository.GetUserWithRolesAndPermissionsAsync(userId);
    }

    public async Task<User?> GetActiveUserByUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        var searchUsername = username.Trim();
        if (searchUsername.Contains("@"))
        {
            searchUsername = searchUsername.Split('@')[0];
        }
        var user = await _userRepository.GetByUsernameAsync(searchUsername);
        if (user != null && user.Status == "Active")
        {
            return user;
        }
        return null;
    }
}
