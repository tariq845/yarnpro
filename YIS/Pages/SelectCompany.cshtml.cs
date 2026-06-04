using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using Microsoft.AspNetCore.Authorization;

namespace YIS.Pages
{
    [AllowAnonymous]
    public class SelectCompanyModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SelectCompanyModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<UserCompanyProfile> CompanyProfiles { get; set; } = default!;

        [BindProperty]
        public long SelectedUcpId { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var userIdStr = User.FindFirst("UserId")?.Value;
            if (long.TryParse(userIdStr, out long userId))
            {
                // Load only companies assigned to this user
                // Using explicit join to bypass EF Core FK type definition mismatch
                CompanyProfiles = await (from uca in _context.UserCompanyAssignments
                                         join ucp in _context.UserCompanyProfiles on uca.UcpId equals ucp.UcpId
                                         where uca.UmId == userId
                                         orderby ucp.UcpCompanyName
                                         select ucp).ToListAsync();

                // FALLBACK: If user is admin (UM_ID = 1) and has no specific assignments, show all
                if (!CompanyProfiles.Any() && userId == 1)
                {
                    CompanyProfiles = await _context.UserCompanyProfiles
                        .OrderBy(p => p.UcpCompanyName)
                        .ToListAsync();
                }
            }
            else
            {
                // If not logged in, show all available company profiles
                CompanyProfiles = await _context.UserCompanyProfiles
                    .OrderBy(p => p.UcpCompanyName)
                    .ToListAsync();
            }

            if (!CompanyProfiles.Any())
            {
                TempData["ErrorMessage"] = "No company profiles assigned to your account. Please contact administrator.";
                return Page();
            }

            // Auto-redirect if only one company is available
            if (CompanyProfiles.Count == 1)
            {
                var singleCompany = CompanyProfiles[0];

                // Update UserMaster with the selected company
                if (long.TryParse(userIdStr, out userId))
                {
                    await _context.UserMasters
                        .Where(u => u.UmId == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.UcpId, singleCompany.UcpId));
                }

                // Set session variables
                HttpContext.Session.SetString("ActiveUcpId", singleCompany.UcpId.ToString());
                HttpContext.Session.SetString("ActiveCompanyName", singleCompany.UcpCompanyName ?? "Unknown");
                TempData["StatusMessage"] = $"You are now working with {singleCompany.UcpCompanyName}";

                return RedirectToPage("/Dashboard/Index");
            }

            // Pre-select the current user's profile if it exists
            // The userId is already parsed above, so no need to re-parse userIdStr
            if (long.TryParse(userIdStr, out userId)) // Re-using userId variable
            {
                var user = await _context.UserMasters.FindAsync(userId);
                if (user?.UcpId != null)
                {
                    SelectedUcpId = user.UcpId.Value;
                }
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var userIdStr = User.FindFirst("UserId")?.Value;
            if (long.TryParse(userIdStr, out long userId))
            {
                if (SelectedUcpId <= 0)
                {
                    ModelState.AddModelError(string.Empty, "Please select a company.");

                    // Reload filtered list
                    CompanyProfiles = await _context.UserCompanyAssignments
                        .Where(uca => uca.UmId == userId)
                        .Select(uca => uca.Company!)
                        .OrderBy(p => p.UcpCompanyName)
                        .ToListAsync();

                    if (!CompanyProfiles.Any() && userId == 1)
                    {
                        CompanyProfiles = await _context.UserCompanyProfiles.OrderBy(p => p.UcpCompanyName).ToListAsync();
                    }
                    return Page();
                }

                // Update UserMaster with the selected company
                await _context.UserMasters
                    .Where(u => u.UmId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.UcpId, SelectedUcpId));

                // Optional: Store in session for easy access
                HttpContext.Session.SetString("ActiveUcpId", SelectedUcpId.ToString());

                var profile = await _context.UserCompanyProfiles.FindAsync(SelectedUcpId);
                if (profile != null)
                {
                    HttpContext.Session.SetString("ActiveCompanyName", profile.UcpCompanyName ?? "Unknown");
                    TempData["StatusMessage"] = $"You selected {profile.UcpCompanyName}";
                }

                return RedirectToPage("/Dashboard/Index");
            }

            return RedirectToPage("/Login");
        }
    }
}
