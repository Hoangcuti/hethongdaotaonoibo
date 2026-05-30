using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using KhoaHoc.Infrastructure;
using KhoaHoc.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<CorporateLmsProContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<KhoaHoc.Services.IEmailService, KhoaHoc.Services.EmailService>();
builder.Services.AddHttpClient<KhoaHoc.Services.IAIService, KhoaHoc.Services.GeminiAIService>();
builder.Services.AddControllersWithViews();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 1024L * 1024L * 1024L;
});

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
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
