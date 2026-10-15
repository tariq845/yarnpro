using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using YIS.Data;

namespace YIS.Pages
{
    public class LoginModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public LoginModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _context.UserMasters
                .FirstOrDefaultAsync(u => u.UmUsername == Username);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            // Verify Password
            // Current DB stores password as byte[]? UmPassword.
            // BCrypt hash is stored as bytes.

            bool isPasswordValid = false;

            if (!string.IsNullOrEmpty(user.UmPassword))
            {
                // Verify directly against the stored string hash
                try
                {
                    isPasswordValid = BCrypt.Net.BCrypt.Verify(Password, user.UmPassword);
                }
                catch
                {
                    isPasswordValid = false;
                }
            }

            if (!isPasswordValid)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            // Update Last Login
            user.LastLogin = DateTime.Now;
            // Need to save changes safely. Using ExecuteUpdate is cleaner if we wasn't already loading the user, 
            // but since we loaded it, just saving is fine, but beware of binary fields loading/saving issues 
            // if we didn't use AsNoTracking (we didn't use AsNoTracking here, so it's tracked).

            // However, UserMasters binary fields might be heavy?
            // Safer to use ExecuteUpdate on the ID to update just LastLogin.

            await _context.UserMasters
                .Where(u => u.UmId == user.UmId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLogin, DateTime.Now));

            // Create Claims
            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.UmName ?? user.UmUsername ?? "User"),
                new System.Security.Claims.Claim("UserId", user.UmId.ToString()),
                new System.Security.Claims.Claim("RoleId", user.UmUrId?.ToString() ?? "0")
            };

            // Add Role Claim if it exists
            if (user.UmUrId.HasValue && user.UmUrId.Value > 0)
            {
                var role = await _context.UserRoles.FindAsync(user.UmUrId.Value);
                if (role != null)
                {
                    claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role.UrName ?? "User"));
                }
                else
                {
                    claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "User"));
                }
            }
            else if (user.UmUrId == -1 || user.UmUsername?.ToLower() == "admin" || user.UmName?.ToLower() == "admin" || user.UmId == 1 || user.UmId == 6)
            {
                claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "SuperAdmin"));
                claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"));
            }
            else
            {
                claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "User"));
            }

            var claimsIdentity = new System.Security.Claims.ClaimsIdentity(
                claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTime.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                new System.Security.Claims.ClaimsPrincipal(claimsIdentity),
                authProperties);

            // --- Cache User Permissions ---
            // Fetch assignments for this USER joined with MenuOptions to get URL
            var assignments = await _context.UserRoleAssignments
                .Where(ura => ura.UraUmId == user.UmId && (ura.UraDisableOn == null || ura.UraDisableOn > DateTime.Today))
                .Join(_context.MenuOptions,
                    ura => ura.UraMoId,
                    mo => mo.MoId,
                    (ura, mo) => new
                    {
                        mo.MoUrl,
                        ura.CanRead,
                        ura.CanCreate,
                        ura.CanUpdate,
                        ura.CanDelete
                    })
                .Where(a => !string.IsNullOrEmpty(a.MoUrl))
                .ToListAsync();

            if (assignments.Any())
            {
                // Create Dictionary: Key=MoUrl, Value=PermissionsObject
                var permissions = assignments
                    .ToDictionary(
                        a => a.MoUrl!,
                        a => new { a.CanRead, a.CanCreate, a.CanUpdate, a.CanDelete }
                    );

                string jsonPermissions = System.Text.Json.JsonSerializer.Serialize(permissions);
                HttpContext.Session.SetString("RolePermissions", jsonPermissions);
            }

            // --- Load User's Active Company ---
            if (user.UcpId.HasValue)
            {
                HttpContext.Session.SetString("ActiveUcpId", user.UcpId.Value.ToString());

                var profile = await _context.UserCompanyProfiles.FindAsync(user.UcpId.Value);
                if (profile != null)
                {
                    HttpContext.Session.SetString("ActiveCompanyName", profile.UcpCompanyName ?? "Unknown");
                }
            }

            // --- Check Company Count for Conditional Routing ---
            // Load companies assigned to this user
            var assignedCompanies = await (from uca in _context.UserCompanyAssignments
                                           join ucp in _context.UserCompanyProfiles on uca.UcpId equals ucp.UcpId
                                           where uca.UmId == user.UmId
                                           select ucp).ToListAsync();

            // FALLBACK: If user is admin (UM_ID = 1) and has no specific assignments, show all
            if (!assignedCompanies.Any() && user.UmId == 1)
            {
                assignedCompanies = await _context.UserCompanyProfiles.ToListAsync();
            }

            // Conditional routing based on company count
            if (assignedCompanies.Count <= 1)
            {
                // 0 or 1 company: redirect directly to dashboard
                if (assignedCompanies.Count == 1)
                {
                    // Auto-select the single company
                    var singleCompany = assignedCompanies[0];

                    // Update UserMaster with the selected company
                    await _context.UserMasters
                        .Where(u => u.UmId == user.UmId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.UcpId, singleCompany.UcpId));

                    // Set session variables
                    HttpContext.Session.SetString("ActiveUcpId", singleCompany.UcpId.ToString());
                    HttpContext.Session.SetString("ActiveCompanyName", singleCompany.UcpCompanyName ?? "Unknown");
                }

                return RedirectToPage("/Dashboard/Index");
            }
            else
            {
                // 2+ companies: show selection page
                return RedirectToPage("/SelectCompany");
            }
        }
    }
}
