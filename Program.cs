using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using KhoaHoc.Infrastructure;
using KhoaHoc.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<CorporateLmsProContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorNumbersToAdd: null);
    }));

builder.Services.AddScoped<KhoaHoc.Services.IEmailService, KhoaHoc.Services.EmailService>();
builder.Services.AddHttpClient<KhoaHoc.Services.IAIService, KhoaHoc.Services.GeminiAIService>();
builder.Services.AddControllersWithViews();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024L * 1024L * 1024L; // 10 GB
});

var keysPath = Path.Combine(builder.Environment.WebRootPath ?? builder.Environment.ContentRootPath, "App_Data", "Keys");
try
{
    if (!Directory.Exists(keysPath))
    {
        Directory.CreateDirectory(keysPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not create keys directory at {keysPath}. Error: {ex.Message}");
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("CorporateLmsPro");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<CorporateLmsProContext>();
        context.Database.EnsureCreated();
        await context.Database.ExecuteSqlRawAsync(DatabaseCompatibility.SchemaPatchSql);
        await KhoaHoc.Infrastructure.DatabaseSeeder.SeedAsync(context, forceReset: false);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Database initialization failed. Check ConnectionStrings:DefaultConnection and make sure the SQL Server instance is reachable.");
        Console.Error.WriteLine(ex.Message);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.Response.Headers.ContentType.ToString().Contains("text/html"))
        {
            context.Response.Headers.ContentType = "text/html; charset=utf-8";
        }
        return Task.CompletedTask;
    });
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();

// Restore session from cookie claims if session was lost due to app recycle
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && string.IsNullOrEmpty(context.Session.GetString("UserID")))
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            var username = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var fullName = context.User.FindFirst("FullName")?.Value;
            var role = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var deptId = context.User.FindFirst("DepartmentID")?.Value;
            var deptName = context.User.FindFirst("DepartmentName")?.Value;
            var isDeptAdmin = context.User.FindFirst("IsDeptAdmin")?.Value;

            context.Session.SetString("UserID", userId);
            context.Session.SetString("Username", username ?? "");
            context.Session.SetString("FullName", fullName ?? "");
            context.Session.SetString("Role", role ?? "");
            context.Session.SetString("DepartmentID", deptId ?? "0");
            context.Session.SetString("DepartmentName", deptName ?? "");
            context.Session.SetString("IsDeptAdmin", isDeptAdmin ?? "False");
        }
    }
    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
