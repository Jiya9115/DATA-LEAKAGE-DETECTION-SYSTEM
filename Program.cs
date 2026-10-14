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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
var configuredProvider = builder.Configuration["DatabaseProvider"] 
    ?? builder.Configuration["DB_PROVIDER"] 
    ?? "";

// Automatically determine whether to use SQLite or SQL Server:
// 1. Explicitly requested via DatabaseProvider="Sqlite" or USE_SQLITE="true"
// 2. Connection string points to a SQLite database (contains ".db" or starts with "Data Source=")
// 3. Or running on Render / Cloud container / Production and no external SQL Server is configured (i.e. default localhost)
bool isRender = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER"));
bool isExplicitSqlite = configuredProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
    || builder.Configuration.GetValue<bool>("USE_SQLITE", false);

bool isSqliteConnectionString = connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
    || (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) 
        && !connectionString.Contains(",1433") 
        && !connectionString.Contains("Server="));

bool isDefaultLocalhost = string.IsNullOrWhiteSpace(connectionString)
    || connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase)
    || connectionString.Contains("127.0.0.1");

bool useSqlite = isExplicitSqlite 
    || isSqliteConnectionString 
    || (isRender && isDefaultLocalhost)
    || (builder.Environment.IsProduction() && isDefaultLocalhost);

if (useSqlite)
{
    var sqliteConnection = isSqliteConnectionString 
        ? connectionString 
        : (builder.Configuration.GetConnectionString("SqliteConnection") ?? "Data Source=dlds.db");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(sqliteConnection));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        }));
}

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
        if (db.Database.IsSqlite())
        {
            logger.LogInformation("Using SQLite database. Ensuring schema is created...");
            db.Database.EnsureCreated();
        }
        else
        {
            logger.LogInformation("Using SQL Server database. Applying migrations...");
            try
            {
                db.Database.Migrate();
            }
            catch (Exception migEx)
            {
                logger.LogWarning(migEx, "SQL Server migration failed; attempting EnsureCreated fallback...");
                db.Database.EnsureCreated();
            }
        }

        await DbInitializer.SeedAsync(services);
        logger.LogInformation("Database initialized and seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing or seeding the database.");
    }
}

app.Run();
