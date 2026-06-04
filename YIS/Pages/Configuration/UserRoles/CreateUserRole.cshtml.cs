using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Configuration.UserRoles
{
    public class CreateUserRoleModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateUserRoleModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public UserRole UserRole { get; set; } = default!;

        public string? Id { get; set; }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            // Set breadcrumb
            ViewData["BreadcrumbParent"] = "Configuration";
            ViewData["BreadcrumbParentUrl"] = "/Configuration/UserRoles/Index";
            
            if (id == null)
            {
                // Create mode
                return Page();
            }

            // Edit mode
            var role = await _context.UserRoles.FindAsync(id);

            if (role == null)
            {
                return NotFound();
            }

            UserRole = role;
            Id = id.ToString();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (UserRole.UrId == 0)
            {
                // New Role
                UserRole.CreationOn = DateTime.Now;
                _context.UserRoles.Add(UserRole);
                TempData["StatusMessage"] = $"Role '{UserRole.UrName}' created successfully.";
            }
            else
            {
                // Update Role
                var existingRole = await _context.UserRoles.FindAsync(UserRole.UrId);
                if (existingRole == null)
                {
                    return NotFound();
                }

                existingRole.UrName = UserRole.UrName;
                existingRole.LastUpdateOn = DateTime.Now;
                
                _context.UserRoles.Update(existingRole);
                TempData["StatusMessage"] = $"Role '{existingRole.UrName}' updated successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
