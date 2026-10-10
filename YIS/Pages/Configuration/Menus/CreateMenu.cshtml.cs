using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Configuration.Menus
{
    public class CreateMenuModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateMenuModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public MenuOption Menu { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            // Set breadcrumb
            ViewData["BreadcrumbParent"] = "Configuration";
            ViewData["BreadcrumbParentUrl"] = "/Configuration/Menus/Index";

            if (id == null)
            {
                // Create mode
                Menu = new MenuOption { MoId = 0, MoType = "Page" };
                return Page();
            }

            // Edit mode
            var menu = await _context.MenuOptions.FirstOrDefaultAsync(m => m.MoId == id);

            if (menu == null)
            {
                return NotFound();
            }

            Menu = menu;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (Menu.MoId == 0)
            {
                _context.MenuOptions.Add(Menu);
            }
            else
            {
                _context.Attach(Menu).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
