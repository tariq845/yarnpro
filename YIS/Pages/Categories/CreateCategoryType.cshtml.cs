using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Categories
{
    public class CreateCategoryTypeModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateCategoryTypeModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string? Id { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!string.IsNullOrEmpty(Id))
            {
                CategoryType = await _context.CategoryTypes.FindAsync(Id);
                if (CategoryType == null)
                {
                    return NotFound();
                }
            }
            return Page();
        }

        [BindProperty]
        public CategoryType CategoryType { get; set; } = default!;

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid || _context.CategoryTypes == null || CategoryType == null)
            {
                return Page();
            }

            // check if update or insert
            // Since ID is string and PK, if we are in "Edit Mode" (Id has value), it's an update.
            // If Id is null, it's an insert. However, user input is in CategoryType.CategoryTypeId.

            bool exists = await _context.CategoryTypes.AnyAsync(e => e.CategoryTypeId == CategoryType.CategoryTypeId);

            // Get Active Company Profile from Session
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (exists)
            {
                // Check if we intended to create but ID conflicts
                if (string.IsNullOrEmpty(Id))
                {
                    ModelState.AddModelError("CategoryType.CategoryTypeId", "Category Type ID already exists.");
                    return Page();
                }

                _context.Attach(CategoryType).State = EntityState.Modified;
                CategoryType.UcpId = activeUcpId;
                TempData["StatusMessage"] = $"Category Type '{CategoryType.CategoryTypeName}' updated successfully.";
                // Ensure Audit fields if needed
            }
            else
            {
                CategoryType.CreationOn = DateTime.Now;
                CategoryType.UcpId = activeUcpId;
                _context.CategoryTypes.Add(CategoryType);
                TempData["StatusMessage"] = $"Category Type '{CategoryType.CategoryTypeName}' created successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./CategoryTypes");
        }
    }
}
