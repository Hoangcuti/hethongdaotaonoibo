using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KhoaHoc.Infrastructure;
using KhoaHoc.Models;

namespace KhoaHoc.Controllers;

public partial class ITController : Controller
{
    [HttpGet("/api/it/users")]
    public async Task<IActionResult> GetUsers(string? search, string? status, int page = 1, int pageSize = 15)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var query = _db.Users
            .Include(u => u.Department)
            .Include(u => u.JobTitle)
            .Include(u => u.Roles)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(u => (u.FullName != null && u.FullName.Contains(search))
                                  || (u.Username != null && u.Username.Contains(search))
                                  || (u.Email != null && u.Email.Contains(search))
                                  || (u.EmployeeCode != null && u.EmployeeCode.Contains(search)));

        if (!string.IsNullOrEmpty(status))
            query = query.Where(u => u.Status == status);

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.UserId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                userId = u.UserId,
                employeeCode = u.EmployeeCode,
                fullName = u.FullName,
                username = u.Username,
                email = u.Email,
                department = u.Department != null ? u.Department.DepartmentName : "N/A",
                departmentId = u.DepartmentId,
                jobTitle = u.JobTitle != null ? u.JobTitle.TitleName : "N/A",
                isItadmin = u.IsItadmin,
                status = u.Status,
                lastLogin = u.LastLogin,
                roles = u.Roles.Select(r => new { r.RoleId, r.RoleName })
            })
            .ToListAsync();

        return Json(new { total, page, pageSize, users });
    }
    [HttpPost("/api/it/users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { error = "Username và Password là bắt buộc." });

        // Chuẩn hóa email và username sang chữ thường, bắt buộc đuôi @basau.net
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            dto.Email = dto.Email.Split('@')[0].Trim().ToLower() + "@basau.net";
        }
        if (!string.IsNullOrWhiteSpace(dto.Username))
        {
            dto.Username = dto.Username.Split('@')[0].Trim().ToLower();
        }
        else if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            dto.Username = dto.Email.Split('@')[0].Trim().ToLower();
        }

        // Kiểm tra username trùng
        if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest(new { error = "Username đã tồn tại." });

        var passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(dto.Password));

        var user = new User
        {
            Username = dto.Username,
            FullName = dto.FullName,
            Email = dto.Email,
            EmployeeCode = dto.EmployeeCode,
            DepartmentId = dto.DepartmentId,
            IsItadmin = dto.IsItAdmin,
            PasswordHash = passwordHash,
            Status = "Active"
        };

        try
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Ghi AuditLog
            var currentUserIdStr = HttpContext.Session.GetString("UserID");
            var currentUserId = string.IsNullOrEmpty(currentUserIdStr) ? 1 : int.Parse(currentUserIdStr);
            
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUserId,
                ActionType = "INSERT",
                TableName = "Users",
                Description = $"Tạo tài khoản mới: {dto.Username} (ID: {user.UserId})",
                Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

            return Ok(new { success = true, userId = user.UserId });
        }
        catch (Exception ex)
        {
            Console.WriteLine("======= CREATE USER ERROR =======");
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null) Console.WriteLine("INNER: " + ex.InnerException.Message);
            Console.WriteLine("=================================");
            return StatusCode(500, new { error = ex.InnerException != null ? ex.InnerException.Message : ex.Message });
        }
    }
    [HttpPut("/api/it/users/{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Chuẩn hóa email nếu có cập nhật
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            dto.Email = dto.Email.Split('@')[0].Trim().ToLower() + "@basau.net";
        }

        user.FullName = dto.FullName ?? user.FullName;
        user.Email = dto.Email ?? user.Email;
        user.Status = dto.Status ?? user.Status;
        user.DepartmentId = dto.DepartmentId ?? user.DepartmentId;
        user.IsItadmin = dto.IsItAdmin ?? user.IsItadmin;

        if (!string.IsNullOrEmpty(dto.NewPassword))
            user.PasswordHash = SHA256.HashData(Encoding.UTF8.GetBytes(dto.NewPassword));

        await _db.SaveChangesAsync();

        // Ghi AuditLog
        var currentUserId = int.Parse(HttpContext.Session.GetString("UserID")!);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUserId,
            ActionType = "UPDATE",
            TableName = "Users",
            Description = $"Cập nhật tài khoản UserID: {id}",
            Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
    [HttpDelete("/api/it/users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users
            .Include(u => u.Enrollments)
            .Include(u => u.TrainingAssignmentUsers)
            .Include(u => u.UserExams)
            .Include(u => u.UserLessonLogs)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user == null) return NotFound();

        using var transaction = await _db.Database.BeginTransactionAsync();
        try {
            // 1. Xóa các dữ liệu học tập & đánh giá (Sử dụng SQL trực tiếp để tối ưu hiệu suất)
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserAnswers WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE UserID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM QuizSessionStates WHERE UserExamID IN (SELECT UserExamID FROM UserExams WHERE UserID = {0})", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserExams WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Enrollments WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM TrainingAssignments WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserLessonLogs WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserRoles WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserPermissions WHERE UserID = {0}", id);

            // 2. Xóa các thành tích & dữ liệu phụ trợ
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM AttendanceLogs WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Certificates WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserBadges WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserSkills WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SurveyResults WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserPoints WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM NewsletterSubscriptions WHERE UserID = {0}", id);

            // 3. Xử lý các liên kết lịch sử (Chuyển sang NULL để bảo toàn tính toàn vẹn)
            await _db.Database.ExecuteSqlRawAsync("UPDATE Courses SET CreatedBy = NULL WHERE CreatedBy = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET UserID = NULL WHERE UserID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE TrainingAssignments SET AssignedBy = NULL WHERE AssignedBy = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE IT_Movement_Logs SET EmployeeID = NULL WHERE EmployeeID = {0}", id);
            await _db.Database.ExecuteSqlRawAsync("UPDATE IT_Movement_Logs SET ActionBy = NULL WHERE ActionBy = {0}", id);

            // 4. Xóa chính User
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Users WHERE UserID = {0}", id);

            // 5. Ghi AuditLog hoạt động xóa (của người thực hiện xóa)
            var currentUserIdStr = HttpContext.Session.GetString("UserID");
            var currentUserId = string.IsNullOrEmpty(currentUserIdStr) ? 0 : int.Parse(currentUserIdStr);
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUserId,
                ActionType = "DELETE",
                TableName = "Users",
                Description = $"Xóa vĩnh viễn tài khoản: {user.Username} (ID: {id})",
                Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
            return Ok(new { success = true });
        } catch (Exception ex) {
            await transaction.RollbackAsync();
            var inner = ex.InnerException != null ? $"\nChi tiết: {ex.InnerException.Message}" : "";
            return StatusCode(500, new { error = "Lỗi khi xóa tài khoản: " + ex.Message + inner });
        }
    }
    [HttpGet("/api/it/roles")]
    public async Task<IActionResult> GetRoles()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var roles = await _db.Roles
            .Select(r => new { r.RoleId, r.RoleName })
            .ToListAsync();
        return Json(roles);
    }
    [HttpGet("/api/it/permission-target-users")]
    public async Task<IActionResult> GetPermissionTargetUsers()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureCompatibilitySchemaAsync();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == "Active")
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName,
                username = u.Username
            })
            .ToListAsync();

        return Json(users);
    }
    [HttpGet("/api/it/my-permissions")]
    public async Task<IActionResult> GetMyPermissions()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureDefaultPermissionsAsync();

        var userIdStr = HttpContext.Session.GetString("UserID");
        if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
        var userId = int.Parse(userIdStr);
        
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return NotFound(new { error = "Không tìm thấy người dùng." });

        List<string> grantedPermissions;
        if (user.IsItadmin == true)
        {
            // IT Admin gets everything
            grantedPermissions = await _db.Permissions
                .AsNoTracking()
                .Where(p => p.PermissionKey != null)
                .Select(p => p.PermissionKey!)
                .Distinct()
                .ToListAsync();
        }
        else
        {
            grantedPermissions = user.Roles
                .SelectMany(r => r.Permissions)
                .Concat(user.UserPermissions.Select(up => up.Permission))
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.PermissionKey))
                .Select(p => p!.PermissionKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        return Json(new
        {
            userId = user.UserId,
            fullName = user.FullName ?? user.Username,
            roleNames = user.Roles.Select(r => r.RoleName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
            permissions = grantedPermissions,
            isItAdmin = user.IsItadmin == true
        });
    }
    [HttpPost("/api/it/users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> AssignRole(int userId, int roleId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserId == userId);
        var role = await _db.Roles.FindAsync(roleId);
        if (user == null || role == null) return NotFound();

        if (!user.Roles.Any(r => r.RoleId == roleId))
            user.Roles.Add(role);

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    [HttpDelete("/api/it/users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveRole(int userId, int roleId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound();

        var role = user.Roles.FirstOrDefault(r => r.RoleId == roleId);
        if (role != null)
        {
            user.Roles.Remove(role);
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }
    [HttpGet("/api/it/permissions")]
    public async Task<IActionResult> GetPermissions()
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureDefaultPermissionsAsync();

        var permissions = await _db.Permissions
            .Include(p => p.Roles)
            .Select(p => new
            {
                permissionId = p.PermissionId,
                permissionKey = p.PermissionKey,
                description = p.Description,
                roles = p.Roles.Select(r => new { r.RoleId, r.RoleName })
            })
            .ToListAsync();
        return Json(permissions);
    }
    [HttpGet("/api/it/roles/{roleId}/permissions")]
    public async Task<IActionResult> GetRolePermissionBoard(int roleId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureDefaultPermissionsAsync();

        var role = await _db.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId);
        if (role == null) return NotFound(new { error = "Không tìm thấy role." });

        var enabled = role.Permissions.Select(p => p.PermissionId).ToHashSet();
        var catalogByKey = GetPermissionCatalog().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.PermissionKey)
            .Select(p => new
            {
                permissionId = p.PermissionId,
                permissionKey = p.PermissionKey,
                description = p.Description,
                enabled = enabled.Contains(p.PermissionId),
                category = p.PermissionKey != null && catalogByKey.ContainsKey(p.PermissionKey) ? catalogByKey[p.PermissionKey].Category : "Khác",
                source = enabled.Contains(p.PermissionId) ? "role" : "none"
            })
            .ToListAsync();

        return Json(new { targetName = role.RoleName, permissions });
    }
    [HttpGet("/api/it/users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPermissionBoard(int userId)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        await EnsureDefaultPermissionsAsync();

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound(new { error = "Không tìm thấy người dùng." });

        var inherited = user.Roles.SelectMany(r => r.Permissions).Select(p => p.PermissionId).ToHashSet();
        var direct = user.UserPermissions.Select(p => p.PermissionId).ToHashSet();
        var catalogByKey = GetPermissionCatalog().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.PermissionKey)
            .Select(p => new
            {
                permissionId = p.PermissionId,
                permissionKey = p.PermissionKey,
                description = p.Description,
                enabled = inherited.Contains(p.PermissionId) || direct.Contains(p.PermissionId),
                inherited = inherited.Contains(p.PermissionId),
                direct = direct.Contains(p.PermissionId),
                category = p.PermissionKey != null && catalogByKey.ContainsKey(p.PermissionKey) ? catalogByKey[p.PermissionKey].Category : "Khác",
                source = direct.Contains(p.PermissionId) && inherited.Contains(p.PermissionId)
                    ? "role+user"
                    : direct.Contains(p.PermissionId)
                        ? "user"
                        : inherited.Contains(p.PermissionId)
                            ? "role"
                            : "none"
            })
            .ToListAsync();

        return Json(new
        {
            targetName = user.FullName ?? user.Username,
            roleNames = user.Roles.Select(r => r.RoleName).ToList(),
            permissions
        });
    }
    [HttpPost("/api/it/roles/{roleId}/permissions/{permissionId}/toggle")]
    public async Task<IActionResult> ToggleRolePermission(int roleId, int permissionId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureDefaultPermissionsAsync();

        var role = await _db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == roleId);
        var permission = await _db.Permissions.FindAsync(permissionId);
        if (role == null || permission == null) return NotFound();

        var existing = role.Permissions.FirstOrDefault(p => p.PermissionId == permissionId);
        var enabled = existing == null;
        if (enabled)
            role.Permissions.Add(permission);
        else
            role.Permissions.Remove(existing!);

        await _db.SaveChangesAsync();
        return Ok(new { success = true, enabled });
    }
    [HttpPost("/api/it/users/{userId}/permissions/{permissionId}/toggle")]
    public async Task<IActionResult> ToggleUserPermission(int userId, int permissionId)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        await EnsureDefaultPermissionsAsync();

        var user = await _db.Users
            .Include(u => u.Roles)
                .ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.UserId == userId);
        var permission = await _db.Permissions.FindAsync(permissionId);
        if (user == null || permission == null) return NotFound();

        var existing = await _db.UserPermissions.FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permissionId);
        var inherited = user.Roles.SelectMany(r => r.Permissions).Any(p => p.PermissionId == permissionId);
        if (inherited)
            return BadRequest(new { error = "Quy?n n?y dang du?c k? th?a t? role. H?y ch?nh ? ph?n quy?n role n?u mu?n t?t." });

        var enabled = existing == null;

        if (enabled)
            _db.UserPermissions.Add(new UserPermission { UserId = userId, PermissionId = permissionId, CreatedAt = DateTime.Now });
        else
            _db.UserPermissions.Remove(existing!);

        await _db.SaveChangesAsync();
        return Ok(new { success = true, enabled });
    }
    [HttpPut("/api/it/permissions/{id}")]
    public async Task<IActionResult> UpdatePermissionRoles(int id, [FromBody] UpdatePermissionRolesDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var permission = await _db.Permissions
            .Include(p => p.Roles)
            .FirstOrDefaultAsync(p => p.PermissionId == id);
        if (permission == null) return NotFound();

        var roleIds = (dto.RoleIds ?? new List<int>()).Distinct().ToList();
        var roles = await _db.Roles.Where(r => roleIds.Contains(r.RoleId)).ToListAsync();

        permission.Roles.Clear();
        foreach (var role in roles)
        {
            permission.Roles.Add(role);
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    [HttpGet("/api/it/users/active-stats")]
    public async Task<IActionResult> GetActiveUserStats()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var thirtyDaysAgo = DateTime.Now.AddDays(-30);
        var recentActive = await _db.Users
            .Where(u => u.LastLogin >= thirtyDaysAgo && u.Status == "Active")
            .CountAsync();
        var neverLoggedIn = await _db.Users.CountAsync(u => u.LastLogin == null && u.Status == "Active");

        return Json(new
        {
            recentlyActive = recentActive,
            neverLoggedIn,
            totalActive = await _db.Users.CountAsync(u => u.Status == "Active")
        });
    }
    [HttpGet("/api/it/export/users")]
    public async Task<IActionResult> ExportUsers(string? search, string? status)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var query = _db.Users.Include(u => u.Department).Include(u => u.JobTitle).Include(u => u.Roles).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(u => (u.FullName != null && u.FullName.Contains(search))
                || (u.Username != null && u.Username.Contains(search))
                || (u.Email != null && u.Email.Contains(search))
                || (u.EmployeeCode != null && u.EmployeeCode.Contains(search)));
        if (!string.IsNullOrEmpty(status)) query = query.Where(u => u.Status == status);

        var users = await query.OrderBy(u => u.Department != null ? u.Department.DepartmentName : "").ThenBy(u => u.FullName).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Nhan Vien");

        ws.Cell(1, 1).Value = "DANH SACH NHAN VIEN - " + DateTime.Now.ToString("dd/MM/yyyy");
        ws.Range(1, 1, 1, 10).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

        var hdrs = new[] { "STT", "Ma NV", "Ho va Ten", "Ten Dang Nhap", "Email", "Phong Ban", "Chuc Danh", "Roles", "Trang Thai", "Dang Nhap Cuoi" };
        for (int i = 0; i < hdrs.Length; i++)
        {
            ws.Cell(2, i + 1).Value = hdrs[i];
            ws.Cell(2, i + 1).Style.Font.Bold = true;
            ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
            ws.Cell(2, i + 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(2, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < users.Count; i++)
        {
            var u = users[i]; var row = i + 3;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = u.EmployeeCode ?? "";
            ws.Cell(row, 3).Value = u.FullName ?? "";
            ws.Cell(row, 4).Value = u.Username ?? "";
            ws.Cell(row, 5).Value = u.Email ?? "";
            ws.Cell(row, 6).Value = u.Department?.DepartmentName ?? "";
            ws.Cell(row, 7).Value = u.JobTitle?.TitleName ?? "";
            ws.Cell(row, 8).Value = string.Join(", ", u.Roles.Select(r => r.RoleName ?? ""));
            ws.Cell(row, 9).Value = u.Status ?? "";
            ws.Cell(row, 10).Value = u.LastLogin.HasValue ? u.LastLogin.Value.ToString("dd/MM/yyyy HH:mm") : "Chua dang nhap";
            if (i % 2 == 1) ws.Range(row, 1, row, hdrs.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f4ff");
            ws.Cell(row, 9).Style.Font.FontColor = u.Status == "Active" ? XLColor.FromHtml("#16a34a") : XLColor.FromHtml("#dc2626");
        }

        ws.Columns().AdjustToContents();
        if (users.Count > 0) {
            ws.Range(2, 1, users.Count + 2, hdrs.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(2, 1, users.Count + 2, hdrs.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "DanhSach_NhanVien_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
    }

    [HttpPost("/api/it/users/bulk-update-dept")]
    public async Task<IActionResult> BulkUpdateDept([FromBody] BulkUpdateDeptDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (dto.UserIds == null || dto.UserIds.Count == 0)
            return BadRequest(new { error = "Danh sách người dùng không được để trống." });

        var deptExists = await _db.Departments.AnyAsync(d => d.DepartmentId == dto.DepartmentId);
        if (!deptExists)
            return BadRequest(new { error = "Phòng ban đích không tồn tại." });

        var users = await _db.Users.Where(u => dto.UserIds.Contains(u.UserId)).ToListAsync();
        if (users.Count == 0)
            return BadRequest(new { error = "Không tìm thấy người dùng nào hợp lệ." });

        foreach (var user in users)
        {
            user.DepartmentId = dto.DepartmentId;
        }

        await _db.SaveChangesAsync();

        // Ghi AuditLog
        var currentUserIdStr = HttpContext.Session.GetString("UserID");
        var currentUserId = string.IsNullOrEmpty(currentUserIdStr) ? 1 : int.Parse(currentUserIdStr);
        
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = currentUserId,
            ActionType = "UPDATE",
            TableName = "Users",
            Description = $"Cập nhật phòng ban hàng loạt cho {users.Count} người dùng sang Phòng ban ID: {dto.DepartmentId}",
            Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
}

public class BulkUpdateDeptDto
{
    public List<int> UserIds { get; set; } = new();
    public int DepartmentId { get; set; }
}
