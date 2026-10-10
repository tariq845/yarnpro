using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

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

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                Users = await query
                    .AsNoTracking()
                    .OrderBy(u => u.UmUsername)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
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
    }
}
