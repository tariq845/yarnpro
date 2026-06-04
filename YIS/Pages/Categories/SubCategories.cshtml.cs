using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class SubCategoriesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SubCategoriesModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<SubCategory> SubCategories { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.SubCategories != null)
            {
                var query = _context.SubCategories
                    .Where(s => s.UcpId == activeUcpId)
                    .Include(s => s.MainCategory)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(s =>
                        s.SCategoryName.ToLower().Contains(term) ||
                        (s.MainCategory != null && s.MainCategory.MCategoryName.ToLower().Contains(term))
                    );
                }

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                SubCategories = await query
                    .OrderBy(s => s.SCategoryName)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.SubCategories == null)
            {
                return NotFound();
            }
            var subCategory = await _context.SubCategories.FindAsync(id);

            if (subCategory != null)
            {
                _context.SubCategories.Remove(subCategory);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Sub Category '{subCategory.SCategoryName}' deleted successfully.";
            }

            return RedirectToPage("./SubCategories");
        }
    }
}
