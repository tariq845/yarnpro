using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using YIS.Data;
using YIS.Models;
using BCrypt.Net;
using YIS.Helpers;

namespace YIS.Pages.Configuration.Users
{
    public class CreateUserModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateUserModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public UserMaster User { get; set; } = default!;

        public string? Id { get; set; }

        [BindProperty]
        public List<long> SelectedCompanyIds { get; set; } = new();

        public List<UserCompanyProfile> AvailableCompanies { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            // Set breadcrumb
            ViewData["BreadcrumbParent"] = "Configuration";
            ViewData["BreadcrumbParentUrl"] = "/Configuration/Users/Index";

            AvailableCompanies = await _context.UserCompanyProfiles
                .Where(c => c.UcpIsActive)
                .OrderBy(c => c.UcpCompanyName)
                .ToListAsync();

            if (id == null)
            {
                // Create mode
                User = new UserMaster { UmId = 0, UmUrId = -1 };
                return Page();
            }

            // Edit mode - select only non-binary fields
            var user = await _context.UserMasters
                .AsNoTracking()
                .Where(u => u.UmId == id)
                .Select(u => new UserMaster
                {
                    UmId = u.UmId,
                    UmName = u.UmName,
                    UmUsername = u.UmUsername,
                    UmContact = u.UmContact,
                    UmActiveOn = u.UmActiveOn,
                    UmDeactiveOn = u.UmDeactiveOn,
                    UmUrId = u.UmUrId,
                    CanCreate = u.CanCreate,
                    CanRead = u.CanRead,
                    CanUpdate = u.CanUpdate,
                    CanDelete = u.CanDelete,
                    CreatedBy = u.CreatedBy,
                    CreationOn = u.CreationOn,
                    LastUpdatedBy = u.LastUpdatedBy,
                    LastUpdateOn = u.LastUpdateOn,
                    LastLogin = u.LastLogin
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound();
            }

            User = user;
            Id = id.ToString();

            // Load existing assignments
            SelectedCompanyIds = await _context.UserCompanyAssignments
                .Where(uca => uca.UmId == id)
                .Select(uca => uca.UcpId)
                .ToListAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? userPassword)
        {
            if (!ModelState.IsValid)
            {
                AvailableCompanies = await _context.UserCompanyProfiles
                    .Where(c => c.UcpIsActive)
                    .OrderBy(c => c.UcpCompanyName)
                    .ToListAsync();
                return Page();
            }

            // Get Active Company Profile from Session
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            long targetUserId = User.UmId;

            if (User.UmId == 0)
            {
                var newUser = new UserMaster
                {
                    UmId = 0,
                    UmName = User.UmName,
                    UmUsername = User.UmUsername,
                    UmContact = User.UmContact,
                    UmActiveOn = User.UmActiveOn,
                    UmDeactiveOn = User.UmDeactiveOn,
                    UmUrId = -1,
                    CanCreate = User.CanCreate,
                    CanRead = User.CanRead,
                    CanUpdate = User.CanUpdate,
                    CanDelete = User.CanDelete,
                    CreatedBy = Helpers.AuthHelper.GetUserId(HttpContext.User),
                    CreationOn = DateTime.Now,
                    UcpId = activeUcpId // Default company
                };

                if (!string.IsNullOrEmpty(userPassword))
                {
                    newUser.UmPassword = BCrypt.Net.BCrypt.HashPassword(userPassword, workFactor: 12);
                }

                _context.UserMasters.Add(newUser);
                await _context.SaveChangesAsync();
                targetUserId = newUser.UmId;
                TempData["StatusMessage"] = $"User '{newUser.UmUsername}' created successfully.";
            }
            else
            {
                // Update User
                await _context.UserMasters
                    .Where(u => u.UmId == User.UmId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(u => u.UmName, User.UmName)
                        .SetProperty(u => u.UmUsername, User.UmUsername)
                        .SetProperty(u => u.UmContact, User.UmContact)
                        .SetProperty(u => u.UmActiveOn, User.UmActiveOn)
                        .SetProperty(u => u.UmDeactiveOn, User.UmDeactiveOn)
                        .SetProperty(u => u.CanCreate, User.CanCreate)
                        .SetProperty(u => u.CanRead, User.CanRead)
                        .SetProperty(u => u.CanUpdate, User.CanUpdate)
                        .SetProperty(u => u.CanDelete, User.CanDelete)
                        .SetProperty(u => u.LastUpdatedBy, Helpers.AuthHelper.GetUserId(HttpContext.User))
                        .SetProperty(u => u.LastUpdateOn, DateTime.Now)
                    );
                TempData["StatusMessage"] = $"User '{User.UmUsername}' updated successfully.";
            }

            // Update Company Assignments
            // 1. Remove old assignments
            await _context.UserCompanyAssignments
                .Where(uca => uca.UmId == targetUserId)
                .ExecuteDeleteAsync();

            // 2. Add new assignments
            if (SelectedCompanyIds != null && SelectedCompanyIds.Any())
            {
                var newAssignments = SelectedCompanyIds.Select(ucpId => new UserCompanyAssignment
                {
                    UmId = targetUserId,
                    UcpId = ucpId
                });
                _context.UserCompanyAssignments.AddRange(newAssignments);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
