using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KhoaHoc.Models;

namespace KhoaHoc.Controllers;

public class AuthController : Controller
{
    private readonly KhoaHoc.BusinessLogicLayer.Services.IUserService _userService;

    public AuthController(KhoaHoc.BusinessLogicLayer.Services.IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public IActionResult Login()
    {
        // Nếu đã có Session UserID -> Đẩy về trang chủ tương ứng luôn
        if (HttpContext.Session.GetString("UserID") != null)
        {
            return RedirectToDashboard();
        }
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        // 1. Kiểm tra đầu vào trống
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.";
            return View();
        }
        
        username = username.Trim();
        password = password.Trim();

        // 2. Xác thực qua Tầng Nghiệp vụ (BLL)
        var user = await _userService.AuthenticateAsync(username, password);

        if (user == null)
        {
            ViewBag.Error = "Tên đăng nhập không tồn tại, mật khẩu không chính xác hoặc tài khoản đã bị khóa.";
            return View();
        }

        // 4. THIẾT LẬP SESSION (Lưu thông tin tạm thời)
        HttpContext.Session.SetString("UserID", user.UserId.ToString());
        HttpContext.Session.SetString("FullName", user.FullName ?? user.Username ?? "Người dùng");
        HttpContext.Session.SetString("Username", user.Username ?? "");
        HttpContext.Session.SetString("DepartmentID", user.DepartmentId?.ToString() ?? "0");
        HttpContext.Session.SetString("DepartmentName", user.Department?.DepartmentName ?? "");
        HttpContext.Session.SetString("IsDeptAdmin", (user.IsDeptAdmin == true).ToString());
        
        string role = DetermineRole(user);
        HttpContext.Session.SetString("Role", role);

        // 5. THIẾT LẬP COOKIE AUTHENTICATION (Dùng cho [Authorize])
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username ?? ""),
            new Claim("FullName", user.FullName ?? ""),
            new Claim(ClaimTypes.Role, role), // Lưu role vào Claim chính thống
            new Claim("DepartmentID", user.DepartmentId?.ToString() ?? "0"),
            new Claim("DepartmentName", user.Department?.DepartmentName ?? ""),
            new Claim("IsDeptAdmin", (user.IsDeptAdmin == true).ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true, // Nhớ đăng nhập
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, 
            new ClaimsPrincipal(claimsIdentity), 
            authProperties);

        return RedirectToDashboard();
    }

    [HttpPost]
    public async Task<IActionResult> DemoLogin(string profile)
    {
        var normalized = (profile ?? string.Empty).Trim().ToLowerInvariant();

        string? targetUsername = normalized switch
        {
            "it" => "admin",
            "hr" => "tuyettthr0001",
            "manager" => "maypvkt0001",
            "employee" => "cuongnvcn0001",
            _ => null
        };

        if (targetUsername == null)
        {
            ViewBag.Error = "Không tìm thấy cấu hình tài khoản demo.";
            return View("Login");
        }

        var user = await _userService.GetActiveUserByUsernameAsync(targetUsername);

        if (user == null)
        {
            ViewBag.Error = $"Không tìm thấy tài khoản '{targetUsername}' trong CSDL. Vui lòng chạy Seeder.";
            return View("Login");
        }

        // Thiết lập Session giống hệt Login thật
        HttpContext.Session.SetString("UserID", user.UserId.ToString());
        HttpContext.Session.SetString("FullName", user.FullName ?? user.Username ?? "Người dùng");
        HttpContext.Session.SetString("Username", user.Username ?? "");
        HttpContext.Session.SetString("DepartmentID", user.DepartmentId?.ToString() ?? "0");
        HttpContext.Session.SetString("DepartmentName", user.Department?.DepartmentName ?? "");
        HttpContext.Session.SetString("IsDeptAdmin", (user.IsDeptAdmin == true).ToString());
        
        string role = DetermineRole(user);
        HttpContext.Session.SetString("Role", role);

        // Thiết lập Cookie cho [Authorize]
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username ?? ""),
            new Claim("FullName", user.FullName ?? ""),
            new Claim(ClaimTypes.Role, role),
            new Claim("DepartmentID", user.DepartmentId?.ToString() ?? "0"),
            new Claim("DepartmentName", user.Department?.DepartmentName ?? ""),
            new Claim("IsDeptAdmin", (user.IsDeptAdmin == true).ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        return RedirectToDashboard();
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    private string DetermineRole(User user)
    {
        // Ưu tiên quyền Admin cao nhất
        if (user.IsItadmin == true) return "IT";

        // Kiểm tra các Role từ bảng trung gian
        var roleNames = user.Roles.Select(r => r.RoleName?.ToUpper()).ToList();
        var managerRoles = new[] { "HR", "TRAINING MANAGER", "HR MANAGER", "MANAGER", "DEPT ADMIN", "TRAINER", "LECTURER", "GIẢNG VIÊN" };
        
        if (managerRoles.Any(mr => roleNames.Contains(mr)) || user.IsDeptAdmin == true)
            return "Manager";

        return "Student";
    }

    private IActionResult RedirectToDashboard()
    {
        var role = HttpContext.Session.GetString("Role");
        return role switch
        {
            "IT" => RedirectToAction("Index", "IT"),
            "Manager" => RedirectToAction("Index", "HR"),
            _ => RedirectToAction("Index", "Student")
        };
    }

    // Endpoint cho trang Login: lấy thông tin phòng ban theo username
    [HttpGet]
    public async Task<IActionResult> GetDepartmentInfo(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Json(new { success = false });

        var user = await _userService.GetActiveUserByUsernameAsync(username);

        if (user == null || user.Department == null)
            return Json(new { success = false });

        return Json(new
        {
            success = true,
            deptName = user.Department.DepartmentName,
            logoUrl = (string?)null  // Mở rộng sau nếu có logo
        });
    }
}
