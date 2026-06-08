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
    [HttpGet("/api/it/departments")]
    public async Task<IActionResult> GetDepartments()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var departments = await _db.Departments
            .Select(d => new {
                d.DepartmentId,
                d.DepartmentName,
                userCount = d.Users.Count(),
                managerId = d.ManagerId,
                managerName = _db.Users.Where(u => u.UserId == d.ManagerId).Select(u => u.FullName).FirstOrDefault()
            })
            .ToListAsync();
        return Json(departments);
    }
    [HttpGet("/api/it/departments/{id}")]
    public async Task<IActionResult> GetDepartmentDetails(int id)
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var department = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == id);
        if (department == null) return NotFound(new { error = "Khong tim thay phong ban." });

        var employees = await _db.Users
            .Include(u => u.JobTitle)
            .Include(u => u.Roles)
            .Where(u => u.DepartmentId == id)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                userId = u.UserId,
                fullName = u.FullName,
                username = u.Username,
                email = u.Email,
                employeeCode = u.EmployeeCode,
                status = u.Status,
                jobTitle = u.JobTitle != null ? u.JobTitle.TitleName : null,
                isDepartmentManager = department.ManagerId == u.UserId,
                roles = u.Roles.Select(r => new { r.RoleId, r.RoleName }).ToList()
            })
            .ToListAsync();

        return Json(new
        {
            departmentId = department.DepartmentId,
            departmentName = department.DepartmentName,
            managerId = department.ManagerId,
            employees
        });
    }
    [HttpPut("/api/it/departments/{id}/manager")]
    public async Task<IActionResult> AssignDepartmentManager(int id, [FromBody] AssignDepartmentManagerDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var department = await _db.Departments.FindAsync(id);
        if (department == null) return NotFound(new { error = "Khong tim thay phong ban." });

        var user = await _db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.UserId == dto.UserId);
        if (user == null) return NotFound(new { error = "Khong tim thay nhan vien." });

        user.DepartmentId = id;
        user.IsDeptAdmin = true;
        department.ManagerId = user.UserId;

        var managerRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName != null && r.RoleName.ToUpper() == "MANAGER");
        if (managerRole == null)
        {
            managerRole = new Role { RoleName = "Manager" };
            _db.Roles.Add(managerRole);
            await _db.SaveChangesAsync();
        }

        if (!user.Roles.Any(r => r.RoleId == managerRole.RoleId))
            user.Roles.Add(managerRole);

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    [HttpPost("/api/it/departments")]
    public async Task<IActionResult> CreateItDepartment([FromBody] CreateDepartmentDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.DepartmentName)) return BadRequest("Name required");
        if (await _db.Departments.AnyAsync(d => d.DepartmentName.ToLower() == dto.DepartmentName.Trim().ToLower()))
            return BadRequest(new { error = $"Ph?ng ban {dto.DepartmentName.Trim()} d? t?n t?i." });
        
        var dept = new Department { DepartmentName = dto.DepartmentName };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    [HttpPut("/api/it/departments/{id}")]
    public async Task<IActionResult> UpdateItDepartment(int id, [FromBody] CreateDepartmentDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();

        if (await _db.Departments.AnyAsync(d => d.DepartmentId != id && d.DepartmentName.ToLower() == dto.DepartmentName.Trim().ToLower()))
            return BadRequest(new { error = $"Ph?ng ban {dto.DepartmentName.Trim()} d? t?n t?i." });

        dept.DepartmentName = dto.DepartmentName;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    [HttpDelete("/api/it/departments/{id}")]
    public async Task<IActionResult> DeleteItDepartment(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();

        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
    [HttpGet("/api/it/jobtitles")]
    public async Task<IActionResult> GetJobTitles()
    {
        var auth = RequireITApi();
        if (auth != null) return auth;

        var titles = await _db.JobTitles
            .Select(j => new
            {
                jobTitleId = j.JobTitleId,
                titleName = j.TitleName,
                gradeLevel = j.GradeLevel,
                userCount = j.Users.Count()
            })
            .OrderBy(j => j.gradeLevel)
            .ThenBy(j => j.titleName)
            .ToListAsync();

        return Json(titles);
    }
    [HttpPost("/api/it/jobtitles")]
    public async Task<IActionResult> CreateJobTitle([FromBody] JobTitleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        if (string.IsNullOrWhiteSpace(dto.TitleName))
            return BadRequest(new { error = "Ten chuc danh khong duoc trong." });

        if (await _db.JobTitles.AnyAsync(j => j.TitleName == dto.TitleName))
            return BadRequest(new { error = "Chuc danh nay da ton tai." });

        var jobTitle = new JobTitle { TitleName = dto.TitleName, GradeLevel = dto.GradeLevel };
        _db.JobTitles.Add(jobTitle);
        await _db.SaveChangesAsync();

        var uid = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        _db.AuditLogs.Add(new AuditLog { UserId = uid, ActionType = "INSERT", TableName = "JobTitles", Description = "Tao chuc danh: " + dto.TitleName, Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, jobTitleId = jobTitle.JobTitleId });
    }
    [HttpPut("/api/it/jobtitles/{id}")]
    public async Task<IActionResult> UpdateJobTitle(int id, [FromBody] JobTitleDto dto)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var jt = await _db.JobTitles.FindAsync(id);
        if (jt == null) return NotFound(new { error = "Khong tim thay chuc danh." });

        if (!string.IsNullOrWhiteSpace(dto.TitleName)) jt.TitleName = dto.TitleName;
        jt.GradeLevel = dto.GradeLevel;
        await _db.SaveChangesAsync();

        var uid = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        _db.AuditLogs.Add(new AuditLog { UserId = uid, ActionType = "UPDATE", TableName = "JobTitles", Description = "Cap nhat chuc danh ID " + id, Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
    [HttpDelete("/api/it/jobtitles/{id}")]
    public async Task<IActionResult> DeleteJobTitle(int id)
    {
        var auth = RequireIT();
        if (auth != null) return Json(new { error = "Unauthorized" });

        var jt = await _db.JobTitles.Include(j => j.Users).FirstOrDefaultAsync(j => j.JobTitleId == id);
        if (jt == null) return NotFound(new { error = "Khong tim thay chuc danh." });

        if (jt.Users.Any())
            return BadRequest(new { error = "Khong the xoa! Con " + jt.Users.Count + " nhan vien dung chuc danh nay." });

        _db.JobTitles.Remove(jt);
        await _db.SaveChangesAsync();

        var uid = int.Parse(HttpContext.Session.GetString("UserID") ?? "1");
        _db.AuditLogs.Add(new AuditLog { UserId = uid, ActionType = "DELETE", TableName = "JobTitles", Description = "Xoa chuc danh: " + jt.TitleName + " (ID " + id + ")", Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
}
