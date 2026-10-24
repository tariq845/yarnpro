using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Items
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ItemMaster> Items { get; set; } = default!;

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

            if (_context.ItemMasters != null)
            {
                var query = _context.ItemMasters
                    .Where(i => i.UcpId == activeUcpId)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(i =>
                        (i.ItemName != null && i.ItemName.ToLower().Contains(term)) ||
                        (i.ItemStCode != null && i.ItemStCode.ToString().Contains(term)) ||
                        (i.ItemHsCode != null && i.ItemHsCode.Contains(term))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                Items = await query
                    .OrderBy(i => i.ItemName)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.ItemMasters == null)
            {
                return NotFound();
            }
            var item = await _context.ItemMasters.FindAsync(id);

            if (item != null)
            {
                _context.ItemMasters.Remove(item);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Item '{item.ItemName}' deleted successfully.";
            }

            return RedirectToPage("./Index");
        }
    }
}
