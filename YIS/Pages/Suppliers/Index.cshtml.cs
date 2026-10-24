using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Suppliers
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<SupplierMaster> Suppliers { get; set; } = default!;

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

            if (_context.SupplierMasters != null)
            {
                var query = _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId)
                    .Include(s => s.Account)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(s =>
                        s.SupplierName.ToLower().Contains(term) ||
                        (s.SupplierContact != null && s.SupplierContact.Contains(term)) ||
                        (s.SupplierEmail != null && s.SupplierEmail.ToLower().Contains(term)) ||
                        (s.SupplierNtnNo != null && s.SupplierNtnNo.Contains(term)) ||
                        (s.SupplierCnic != null && s.SupplierCnic.Contains(term))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                Suppliers = await query
                    .OrderBy(s => s.SupplierName)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.SupplierMasters == null)
            {
                return NotFound();
            }
            var supplier = await _context.SupplierMasters.FindAsync(id);

            if (supplier != null)
            {
                _context.SupplierMasters.Remove(supplier);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Supplier '{supplier.SupplierName}' deleted successfully.";
            }

            return RedirectToPage("./Index");
        }
    }
}
