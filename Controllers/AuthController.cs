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
    private readonly CorporateLmsProContext _db;

    public AuthController(CorporateLmsProContext db)
    {
        _db = db;
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

        // 2. Tìm User trong DB (bao gồm cả Roles và Department để check quyền sau này)
        var searchUsername = username;
        if (searchUsername.Contains("@"))
        {
            searchUsername = searchUsername.Split('@')[0];
        }

        var user = await _db.Users
            .Include(u => u.Roles)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => (u.Username == searchUsername || u.Email == username) && u.Status == "Active");

        if (user == null)
        {
            ViewBag.Error = "Tên đăng nhập không tồn tại hoặc tài khoản đã bị khóa.";
            return View();
        }

        // 3. KIỂM TRA MẬT KHẨU (PHẦN QUAN TRỌNG NHẤT)
        bool passwordValid = false;
        if (user.PasswordHash != null)
        {
            // Băm mật khẩu người dùng vừa nhập bằng SHA256
            byte[] inputBytes = Encoding.UTF8.GetBytes(password);
            byte[] hashedInput = SHA256.HashData(inputBytes);

            // Chuyển cả 2 sang chuỗi Hex (viết hoa) để so sánh cho chính xác tuyệt đối
            string storedHashHex = Convert.ToHexString(user.PasswordHash);
            string inputHashHex = Convert.ToHexString(hashedInput);

            if (storedHashHex.Equals(inputHashHex, StringComparison.OrdinalIgnoreCase))
            {
                passwordValid = true;
            }
            // Fallback: nếu Database lưu password dưới dạng plain text (ví dụ bị ai đó insert nhầm text vào VARBINARY)
            else if (Encoding.UTF8.GetString(user.PasswordHash) == password)
            {
                passwordValid = true;
            }
        }

        if (!passwordValid)
        {
            ViewBag.Error = "Mật khẩu không chính xác.";
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

        // 6. CẬP NHẬT THỜI GIAN ĐĂNG NHẬP CUỐI
        user.LastLogin = DateTime.Now;
        await _db.SaveChangesAsync();

        return RedirectToDashboard();
    }

    [HttpPost]
    public async Task<IActionResult> DemoLogin(string profile)
    {
        var normalized = (profile ?? string.Empty).Trim().ToLowerInvariant();

        var demoProfiles = new Dictionary<string, (string Username, string FullName, string Role, string DepartmentName, string DepartmentId, string Dashboard)>
        {
            ["it"] = ("demo.it", "System Admin Demo", "IT", "Van hanh he thong", "0", "IT"),
            ["hr"] = ("demo.hr", "HR Demo", "Manager", "Nhan su", "101", "HR"),
            ["manager"] = ("demo.manager", "Truong phong Demo", "Manager", "Phong ban demo", "102", "HR"),
            ["employee"] = ("demo.employee", "Nhan vien Demo", "Student", "Khoi nghiep vu", "104", "Student")
        };

        if (!demoProfiles.TryGetValue(normalized, out var demo))
        {
            ViewBag.Error = "Khong tim thay tai khoan demo.";
            return View("Login");
        }

        HttpContext.Session.SetString("UserID", $"demo-{normalized}");
        HttpContext.Session.SetString("FullName", demo.FullName);
        HttpContext.Session.SetString("Username", demo.Username);
        HttpContext.Session.SetString("DepartmentID", demo.DepartmentId);
        HttpContext.Session.SetString("DepartmentName", demo.DepartmentName);
        HttpContext.Session.SetString("IsDeptAdmin", (demo.Role == "Manager").ToString());
        HttpContext.Session.SetString("Role", demo.Role);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, $"demo-{normalized}"),
            new Claim(ClaimTypes.Name, demo.Username),
            new Claim("FullName", demo.FullName),
            new Claim("DepartmentName", demo.DepartmentName),
            new Claim("DepartmentID", demo.DepartmentId),
            new Claim("IsDeptAdmin", (demo.Role == "Manager").ToString()),
            new Claim(ClaimTypes.Role, demo.Role)
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

        return demo.Dashboard switch
        {
            "IT" => RedirectToAction("Index", "IT"),
            "HR" => RedirectToAction("Index", "HR"),
            _ => RedirectToAction("Index", "Student")
        };
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

        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Username == username && u.Status == "Active");

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
