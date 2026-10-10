using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Sellers
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public SellerMaster? Seller { get; set; }
        public IList<UserCompanyProfile> CompanyProfiles { get; set; } = default!;
        public long? CurrentUcpId { get; set; }

        public async Task OnGetAsync()
        {
            var userId = AuthHelper.GetUserId(User);
            var user = await _context.UserMasters.FindAsync(userId);
            CurrentUcpId = user?.UcpId;

            // Load the single seller with company profiles
            Seller = await _context.SellerMasters
                .Include(s => s.CompanyProfiles)
                .FirstOrDefaultAsync();

            if (Seller != null)
            {
                CompanyProfiles = Seller.CompanyProfiles
                    .Where(c => c.UcpIsActive) // Only show globally enabled companies
                    .OrderBy(c => c.UcpCompanyName)
                    .ToList();
            }
        }

        public async Task<IActionResult> OnPostToggleActiveAsync(long profileId)
        {
            var userId = AuthHelper.GetUserId(User);
            if (userId <= 0) return RedirectToPage("/Login");

            var profile = await _context.UserCompanyProfiles.FindAsync(profileId);
            if (profile == null)
            {
                return NotFound();
            }

            // Update user's active company choice
            await _context.UserMasters
                .Where(u => u.UmId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.UcpId, profileId));

            // Update session
            HttpContext.Session.SetString("ActiveUcpId", profileId.ToString());
            HttpContext.Session.SetString("ActiveCompanyName", profile.UcpCompanyName ?? "Unknown");

            TempData["StatusMessage"] = $"'{profile.UcpCompanyName}' is now your active company.";

            return RedirectToPage();
        }
    }
}
