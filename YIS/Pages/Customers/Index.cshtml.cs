using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Customers
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<CustomerMaster> Customers { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.CustomerMasters != null)
            {
                var query = _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .Include(c => c.Account)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(c =>
                        c.CustomerName.ToLower().Contains(term) ||
                        (c.CustomerContact != null && c.CustomerContact.Contains(term)) ||
                        (c.CustomerEmail != null && c.CustomerEmail.ToLower().Contains(term)) ||
                        (c.CustomerNtnNo != null && c.CustomerNtnNo.Contains(term)) ||
                        (c.CustomerCnic != null && c.CustomerCnic.Contains(term))
                    );
                }

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                Customers = await query
                    .OrderBy(c => c.CustomerName)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.CustomerMasters == null)
            {
                return NotFound();
            }
            var customer = await _context.CustomerMasters.FindAsync(id);

            if (customer != null)
            {
                _context.CustomerMasters.Remove(customer);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Customer '{customer.CustomerName}' deleted successfully.";
            }

            return RedirectToPage("./Index");
        }
    }
}
