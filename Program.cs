using DLDS.Data;
using DLDS.Models;
using DLDS.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration / secrets ----------
// Connection string comes from appsettings.json (dev placeholder) or, preferably,
// an environment variable / user-secret so real credentials never live in source control:
//   export ConnectionStrings__DefaultConnection="Server=...;Database=DLDS;User Id=...;Password=...;TrustServerCertificate=True;"
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// ---------- Services ----------
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Reasonable defaults for a demo app; tighten for production use.
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddControllersWithViews();

// Business services
builder.Services.AddScoped<IRiskAssessmentService, RiskAssessmentService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IReportService, ReportService>();

// FileMonitoringService is both a hosted background service AND an injectable
// control surface (Start/StopWatchingAsync), so it is registered as a singleton
// and exposed under both interfaces pointing at the same instance.
builder.Services.AddSingleton<FileMonitoringService>();
builder.Services.AddSingleton<IFileMonitoringControl>(sp => sp.GetRequiredService<FileMonitoringService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<FileMonitoringService>());

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ---------- Middleware pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ---------- Database migration + seed on startup ----------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();

        await DbInitializer.SeedAsync(services);
    }
    catch (Exception ex)
    {
        // Don't crash the whole app if the DB isn't reachable yet in dev;
        // surface a clear log message instead so it's obvious during a viva.
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
    }
}

app.Run();
