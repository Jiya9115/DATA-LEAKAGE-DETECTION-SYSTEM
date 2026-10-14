using DLDS.Data;
using DLDS.Models;
using DLDS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DLDS.Controllers
{
    /// <summary>Login/Logout using ASP.NET Core Identity. Passwords are always hashed by Identity — never handled or stored in plain text here.</summary>
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext db,
            ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    var user = await _userManager.FindByEmailAsync(model.Email);
                    if (user != null)
                    {
                        user.LastLoginAt = DateTime.UtcNow;
                        await _userManager.UpdateAsync(user);
                    }

                    _db.AuditLogs.Add(new AuditLog
                    {
                        Action = "Login",
                        Description = $"{model.Email} signed in.",
                        Username = model.Email,
                        IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                    });
                    await _db.SaveChangesAsync();

                    if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                        return Redirect(model.ReturnUrl);

                    return RedirectToAction("Index", "Dashboard");
                }

                if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "This account has been temporarily locked due to multiple failed sign-in attempts.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid email or password.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection error during login for {Email}", model.Email);
                ModelState.AddModelError(string.Empty, $"Database connection failed: {ex.Message}. Check database configuration.");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var email = User.Identity?.Name ?? "unknown";
            await _signInManager.SignOutAsync();

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "Logout",
                Description = $"{email} signed out.",
                Username = email,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            });
            await _db.SaveChangesAsync();

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                var existingUser = await _userManager.FindByEmailAsync(model.Email);
                if (existingUser != null)
                {
                    ModelState.AddModelError("Email", "An account with this email address already exists.");
                    return View(model);
                }

                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    DisplayName = model.DisplayName.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded)
                {
                    foreach (var error in createResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    return View(model);
                }

                // Assign default role (User)
                await _userManager.AddToRoleAsync(user, Roles.User);

                // Audit log
                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "Register",
                    Description = $"New user registered: {model.Email} ({model.DisplayName}).",
                    Username = model.Email,
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                });
                await _db.SaveChangesAsync();

                // Automatically sign in the newly registered user
                await _signInManager.SignInAsync(user, isPersistent: false);

                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for {Email}", model.Email);
                ModelState.AddModelError(string.Empty, $"Registration failed: {ex.Message}. Check database configuration.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult AccessDenied() => View();
    }
}
