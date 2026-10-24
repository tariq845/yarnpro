using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Configuration.Users
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<UserMaster> Users { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 10;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public async Task OnGetAsync()
        {
            // Set breadcrumb
            ViewData["BreadcrumbParent"] = "Configuration";
            ViewData["BreadcrumbParentUrl"] = "#";
            
            if (_context.UserMasters != null)
            {
                var query = _context.UserMasters.AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(u => 
                        u.UmUsername.ToLower().Contains(term) ||
                        u.UmName.ToLower().Contains(term) ||
                        (u.UmContact != null && u.UmContact.Contains(term))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                Users = await query
                    .AsNoTracking()
                    .OrderBy(u => u.UmUsername)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .Select(u => new UserMaster
                    {
                        UmId = u.UmId,
                        UmName = u.UmName,
                        UmUsername = u.UmUsername,
                        UmContact = u.UmContact,
                        UmActiveOn = u.UmActiveOn,
                        UmDeactiveOn = u.UmDeactiveOn,
                        CanCreate = u.CanCreate,
                        CanRead = u.CanRead,
                        CanUpdate = u.CanUpdate,
                        CanDelete = u.CanDelete,
                        CreationOn = u.CreationOn
                    })
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.UserMasters == null)
            {
                return NotFound();
            }
            var user = await _context.UserMasters.FindAsync(id);

            if (user != null)
            {
                _context.UserMasters.Remove(user);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"User '{user.UmUsername}' deleted successfully.";
            }

            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostImpersonateAsync(long targetUserId)
        {
            if (!AuthHelper.IsAdmin(User))
            {
                TempData["ErrorMessage"] = "Access Denied: Only administrators are authorized to use Proxy Login.";
                return RedirectToPage("./Index");
            }

            if (AuthHelper.IsImpersonating(HttpContext))
            {
                TempData["WarningMessage"] = "You are already impersonating a user. Please switch back to Admin first.";
                return RedirectToPage("./Index");
            }

            var currentAdminId = AuthHelper.GetUserId(User);
            if (currentAdminId == targetUserId)
            {
                TempData["InfoMessage"] = "You cannot impersonate yourself.";
                return RedirectToPage("./Index");
            }

            var targetUser = await _context.UserMasters.FindAsync(targetUserId);
            if (targetUser == null)
            {
                TempData["ErrorMessage"] = "Target user not found.";
                return RedirectToPage("./Index");
            }

            var currentAdminName = User.Identity?.Name ?? "Administrator";

            await AuthHelper.SetUserSessionAndSignInAsync(
                HttpContext,
                _context,
                targetUser,
                isImpersonating: true,
                originalAdminUserId: currentAdminId,
                originalAdminName: currentAdminName
            );

            TempData["StatusMessage"] = $"Proxy Login Active: You are now logged in as '{targetUser.UmName ?? targetUser.UmUsername}'.";
            return RedirectToPage("/Dashboard/Index");
        }
    }
}
