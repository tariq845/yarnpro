using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class CreateCategoryMasterModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateCategoryMasterModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public long? Id { get; set; }

        public SelectList SubCategories { get; set; } = default!;
        public SelectList CategoryTypes { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // Populate Dropdowns
            SubCategories = new SelectList(_context.SubCategories.Where(s => s.UcpId == activeUcpId), "SCategoryId", "SCategoryName");
            CategoryTypes = new SelectList(_context.CategoryTypes.Where(t => t.UcpId == activeUcpId), "CategoryTypeId", "CategoryTypeName");

            if (Id.HasValue)
            {
                CategoryMaster = await _context.CategoryMasters.FindAsync(Id.Value);
                if (CategoryMaster == null)
                {
                    return NotFound();
                }
            }

            return Page();
        }

        [BindProperty]
        public CategoryMaster CategoryMaster { get; set; } = default!;

        public async Task<IActionResult> OnPostAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (!ModelState.IsValid || _context.CategoryMasters == null || CategoryMaster == null)
            {
                // Repopulate dropdowns if validation fails
                SubCategories = new SelectList(_context.SubCategories.Where(s => s.UcpId == activeUcpId), "SCategoryId", "SCategoryName");
                CategoryTypes = new SelectList(_context.CategoryTypes.Where(t => t.UcpId == activeUcpId), "CategoryTypeId", "CategoryTypeName");
                return Page();
            }

            // Get Active Company Profile from Session
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }


            // Check if this is an update or create operation
            var existingCategory = await _context.CategoryMasters.FindAsync(CategoryMaster.CategoryId);

            if (existingCategory != null)
            {
                // UPDATE existing category
                existingCategory.CategoryName = CategoryMaster.CategoryName;
                existingCategory.SCategoryId = CategoryMaster.SCategoryId;
                existingCategory.CategoryTypeId = CategoryMaster.CategoryTypeId;
                existingCategory.UcpId = activeUcpId;
                existingCategory.LastUpdateOn = DateTime.Now;
                existingCategory.LastUpdatedBy = Helpers.AuthHelper.GetUserId(User);

                _context.CategoryMasters.Update(existingCategory);
                TempData["StatusMessage"] = $"Category '{existingCategory.CategoryName}' updated successfully.";
            }
            else
            {
                // CREATE new category
                CategoryMaster.CreationOn = DateTime.Now;
                CategoryMaster.CreatedBy = Helpers.AuthHelper.GetUserId(User);
                CategoryMaster.UcpId = activeUcpId;
                _context.CategoryMasters.Add(CategoryMaster);
                TempData["StatusMessage"] = $"Category '{CategoryMaster.CategoryName}' created successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./CategoriesMaster");
        }
    }
}
