using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using WDSound.Web.Data;

var builder = WebApplication.CreateBuilder(args);
var sqlite = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Database:UseSqlite");
if (sqlite) SQLitePCL.Batteries_V2.Init();
if (builder.Environment.IsDevelopment())
{
    Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
}
var connection = sqlite ? new SqliteConnectionStringBuilder
{
    DataSource = builder.Configuration["Database:SqlitePath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "wdsound.db")
}.ToString()
    : builder.Configuration.GetConnectionString("Catalog") ?? throw new InvalidOperationException("Configure ConnectionStrings:Catalog for SQL Server.");
builder.Services.AddDbContext<Store>(options =>
{
    if (sqlite) options.UseSqlite(connection);
    else options.UseSqlServer(connection);
});
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<Store>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.AccessDeniedPath = "/admin/denied";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
if (builder.Configuration["DataProtection:KeysPath"] is { } keysPath)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("WDSound");
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    foreach (var (name, limit) in new[] { ("contact", 5), ("login", 12) })
    {
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) }));
    }
});
builder.Services.AddSingleton<LocalMedia>();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await using (var scope = app.Services.CreateAsyncScope())
{
    await Initialize.Run(scope.ServiceProvider, app.Configuration, app.Environment.IsDevelopment(), sqlite);
}
await app.RunAsync();
