using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class CreateMainCategoryModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateMainCategoryModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public long? Id { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (Id.HasValue)
            {
                MainCategory = await _context.MainCategories.FindAsync(Id.Value);
                if (MainCategory == null)
                {
                    return NotFound();
                }
            }
            return Page();
        }

        [BindProperty]
        public MainCategory MainCategory { get; set; } = default!;

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid || _context.MainCategories == null || MainCategory == null)
            {
                return Page();
            }

            // Get Active Company Profile from Session
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (MainCategory.MCategoryId > 0)
            {
                // Update
                _context.Attach(MainCategory).State = EntityState.Modified;
                MainCategory.UcpId = activeUcpId;
                MainCategory.LastUpdateOn = DateTime.Now;
                TempData["StatusMessage"] = $"Main Category '{MainCategory.MCategoryName}' updated successfully.";
            }
            else
            {
                // Create
                MainCategory.CreationOn = DateTime.Now;
                MainCategory.UcpId = activeUcpId;
                _context.MainCategories.Add(MainCategory);
                TempData["StatusMessage"] = $"Main Category '{MainCategory.MCategoryName}' created successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./MainCategories");
        }
    }
}
