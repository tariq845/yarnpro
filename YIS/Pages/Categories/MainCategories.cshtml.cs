using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class MainCategoriesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public MainCategoriesModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<MainCategory> MainCategories { get; set; } = default!;

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.MainCategories != null)
            {
                var query = _context.MainCategories
                    .Where(mc => mc.UcpId == activeUcpId)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(mc => mc.MCategoryName.ToLower().Contains(term));
                }

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                MainCategories = await query
                    .OrderBy(mc => mc.MCategoryName)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
                    .ToListAsync();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.MainCategories == null)
            {
                return NotFound();
            }
            var mainCategory = await _context.MainCategories.FindAsync(id);

            if (mainCategory != null)
            {
                _context.MainCategories.Remove(mainCategory);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Main Category '{mainCategory.MCategoryName}' deleted successfully.";
            }

            return RedirectToPage("./MainCategories");
        }
    }
}
