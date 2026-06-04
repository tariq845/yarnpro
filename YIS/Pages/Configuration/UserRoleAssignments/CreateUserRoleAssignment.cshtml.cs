using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Configuration.UserRoleAssignments
{
    public class CreateUserRoleAssignmentModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateUserRoleAssignmentModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public class MenuAssignmentViewModel
        {
            public long MoId { get; set; }
            public string? MoName { get; set; }
            public bool IsSelected { get; set; }
            public bool CanCreate { get; set; }
            public bool CanRead { get; set; }
            public bool CanUpdate { get; set; }
            public bool CanDelete { get; set; }
            public DateTime? DisableOn { get; set; }
        }

        [BindProperty]
        public List<MenuAssignmentViewModel> MenuAssignments { get; set; } = new List<MenuAssignmentViewModel>();

        [BindProperty(SupportsGet = true)]
        public long? UserId { get; set; }

        public string? UserName { get; set; }

        public async Task<IActionResult> OnGetAsync(long? userId)
        {
            if (!userId.HasValue)
            {
                return RedirectToPage("/Configuration/Users/Index");
            }

            // Set breadcrumb
            ViewData["BreadcrumbParent"] = "Configuration";
            ViewData["BreadcrumbParentUrl"] = "/Configuration/Users/Index";

            UserId = userId;
            var user = await _context.UserMasters.FindAsync(userId.Value);
            if (user == null)
            {
                return NotFound();
            }
            UserName = user.UmName ?? user.UmUsername;

            // Load all available menus
            var allMenus = await _context.MenuOptions
                .Where(m => !m.MoDeactiveOn.HasValue || m.MoDeactiveOn > DateTime.Today)
                .OrderBy(m => m.MoName)
                .ToListAsync();

            // Load existing assignments
            var existingAssignments = await _context.UserRoleAssignments
                .Where(ura => ura.UraUmId == userId.Value)
                .ToListAsync();

            // Populate ViewModel
            MenuAssignments = allMenus.Select(m => {
                var existing = existingAssignments.FirstOrDefault(a => a.UraMoId == m.MoId);
                return new MenuAssignmentViewModel
                {
                    MoId = m.MoId,
                    MoName = m.MoName,
                    IsSelected = existing != null,
                    CanCreate = existing?.CanCreate ?? false,
                    CanRead = existing?.CanRead ?? false,
                    CanUpdate = existing?.CanUpdate ?? false,
                    CanDelete = existing?.CanDelete ?? false,
                    DisableOn = existing?.UraDisableOn
                };
            }).ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!UserId.HasValue)
            {
                return RedirectToPage("/Configuration/Users/Index");
            }

            // Get current assignments in DB
            var existingAssignments = await _context.UserRoleAssignments
                .Where(ura => ura.UraUmId == UserId.Value)
                .ToListAsync();

            foreach (var vm in MenuAssignments)
            {
                var existing = existingAssignments.FirstOrDefault(a => a.UraMoId == vm.MoId);

                if (vm.IsSelected)
                {
                    if (existing == null)
                    {
                        // Add new
                        _context.UserRoleAssignments.Add(new UserRoleAssignment
                        {
                            UraId = 0,
                            UraUmId = UserId.Value,
                            UraMoId = vm.MoId,
                            UraRights = -1, // Hiding rights and sending -1
                            CanCreate = vm.CanCreate,
                            CanRead = vm.CanRead,
                            CanUpdate = vm.CanUpdate,
                            CanDelete = vm.CanDelete,
                            UraDisableOn = vm.DisableOn,
                            CreationOn = DateTime.Now
                        });
                    }
                    else
                    {
                        // Update existing
                        existing.UraRights = -1; // Standardize to -1
                        existing.CanCreate = vm.CanCreate;
                        existing.CanRead = vm.CanRead;
                        existing.CanUpdate = vm.CanUpdate;
                        existing.CanDelete = vm.CanDelete;
                        existing.UraDisableOn = vm.DisableOn;
                        existing.LastUpdateOn = DateTime.Now;
                        _context.UserRoleAssignments.Update(existing);
                    }
                }
                else if (existing != null)
                {
                    // Remove
                    _context.UserRoleAssignments.Remove(existing);
                }
            }

            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = "Permissions updated successfully.";
            return RedirectToPage(new { userId = UserId });
        }

        public async Task<IActionResult> OnPostDeleteAsync(long uraId, long? userId)
        {
            var assignment = await _context.UserRoleAssignments.FindAsync(uraId);
            if (assignment != null)
            {
                _context.UserRoleAssignments.Remove(assignment);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = "Permission removed successfully.";
            }
            return RedirectToPage(new { userId = userId });
        }
    }
}
