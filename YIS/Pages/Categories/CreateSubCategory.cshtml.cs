using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class CreateSubCategoryModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateSubCategoryModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public long? Id { get; set; }

        public SelectList MainCategories { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // Populate MainCategories dropdown
            MainCategories = new SelectList(_context.MainCategories.Where(m => m.UcpId == activeUcpId), "MCategoryId", "MCategoryName");

            if (Id.HasValue)
            {
                SubCategory = await _context.SubCategories.FindAsync(Id.Value);
                if (SubCategory == null)
                {
                    return NotFound();
                }
            }
            return Page();
        }

        [BindProperty]
        public SubCategory SubCategory { get; set; } = default!;

        public async Task<IActionResult> OnPostAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (!ModelState.IsValid || _context.SubCategories == null || SubCategory == null)
            {
                MainCategories = new SelectList(_context.MainCategories.Where(m => m.UcpId == activeUcpId), "MCategoryId", "MCategoryName");
                return Page();
            }

            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (SubCategory.SCategoryId > 0)
            {
                // Update
                _context.Attach(SubCategory).State = EntityState.Modified;
                SubCategory.UcpId = activeUcpId;
                SubCategory.LastUpdateOn = DateTime.Now;
                TempData["StatusMessage"] = $"Sub Category '{SubCategory.SCategoryName}' updated successfully.";
            }
            else
            {
                // Create
                SubCategory.CreationOn = DateTime.Now;
                SubCategory.UcpId = activeUcpId;
                _context.SubCategories.Add(SubCategory);
                TempData["StatusMessage"] = $"Sub Category '{SubCategory.SCategoryName}' created successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./SubCategories");
        }
    }
}
