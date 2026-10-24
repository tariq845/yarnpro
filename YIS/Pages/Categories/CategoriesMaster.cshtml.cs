using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class CategoriesMasterModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CategoriesMasterModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<CategoryMaster> CategoryMasters { get; set; } = default!;

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

            if (_context.CategoryMasters != null)
            {
                var query = _context.CategoryMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .Include(c => c.SubCategory)
                    .Include(c => c.CategoryType)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(c =>
                        c.CategoryName.ToLower().Contains(term) ||
                        (c.SubCategory != null && c.SubCategory.SCategoryName.ToLower().Contains(term)) ||
                        (c.CategoryType != null && c.CategoryType.CategoryTypeName.ToLower().Contains(term))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                CategoryMasters = await query
                    .OrderBy(c => c.CategoryName)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.CategoryMasters == null)
            {
                return NotFound();
            }
            var categoryMaster = await _context.CategoryMasters.FindAsync(id);

            if (categoryMaster != null)
            {
                _context.CategoryMasters.Remove(categoryMaster);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Category Master '{categoryMaster.CategoryName}' deleted successfully.";
            }

            return RedirectToPage("./CategoriesMaster");
        }
    }
}
