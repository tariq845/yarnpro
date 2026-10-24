using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Accounts
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<AccountMaster> Accounts { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 10;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.AccountMasters != null)
            {
                var query = _context.AccountMasters
                    .Where(a => a.UcpId == activeUcpId)
                    .Include(a => a.Category)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(a =>
                        a.AccountName.ToLower().Contains(term) ||
                        (a.Category != null && a.Category.CategoryName.ToLower().Contains(term))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                Accounts = await query
                    .OrderBy(a => a.AccountName)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.AccountMasters == null)
            {
                return NotFound();
            }
            var account = await _context.AccountMasters.FindAsync(id);

            if (account != null)
            {
                _context.AccountMasters.Remove(account);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Account '{account.AccountName}' deleted successfully.";
            }

            return RedirectToPage("./Index");
        }
    }
}
