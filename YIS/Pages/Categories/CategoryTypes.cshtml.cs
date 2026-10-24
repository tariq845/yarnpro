using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class CategoryTypesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CategoryTypesModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<CategoryType> CategoryTypes { get; set; } = default!;

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

            if (_context.CategoryTypes != null)
            {
                var query = _context.CategoryTypes
                    .Where(ct => ct.UcpId == activeUcpId)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(ct => ct.CategoryTypeName.ToLower().Contains(term));
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                CategoryTypes = await query
                    .OrderBy(ct => ct.CategoryTypeName)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(string id)
        {
            if (_context.CategoryTypes == null)
            {
                return NotFound();
            }
            var categoryType = await _context.CategoryTypes.FindAsync(id);

            if (categoryType != null)
            {
                _context.CategoryTypes.Remove(categoryType);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Category Type '{categoryType.CategoryTypeName}' deleted successfully.";
            }

            return RedirectToPage("./CategoryTypes");
        }
    }
}
