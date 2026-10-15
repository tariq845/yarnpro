using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YIS.Data;
using YIS.Helpers;

namespace YIS.Pages
{
    public class StopImpersonationModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public StopImpersonationModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            return await SwitchBackToAdminAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            return await SwitchBackToAdminAsync();
        }

        private async Task<IActionResult> SwitchBackToAdminAsync()
        {
            if (!AuthHelper.IsImpersonating(HttpContext))
            {
                return RedirectToPage("/Dashboard/Index");
            }

            var originalAdminId = AuthHelper.GetOriginalAdminUserId(HttpContext);
            if (!originalAdminId.HasValue)
            {
                TempData["ErrorMessage"] = "Could not find original admin session. Redirected to login.";
                return RedirectToPage("/Login");
            }

            var admin = await _context.UserMasters.FindAsync(originalAdminId.Value);
            if (admin == null)
            {
                TempData["ErrorMessage"] = "Original admin account could not be found.";
                return RedirectToPage("/Login");
            }

            await AuthHelper.SetUserSessionAndSignInAsync(
                HttpContext,
                _context,
                admin,
                isImpersonating: false
            );

            TempData["StatusMessage"] = $"Welcome back, {admin.UmName ?? admin.UmUsername}! Switched back to Admin account successfully.";
            return RedirectToPage("/Configuration/Users/Index");
        }
    }
}
