using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages
{
    public class ProfileModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ProfileModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public UserMaster? CurrentUser { get; set; }

        [BindProperty]
        public ChangePasswordInputModel ChangePasswordInput { get; set; } = new();

        public class ChangePasswordInputModel
        {
            [Required(ErrorMessage = "Current Password is required")]
            [DataType(DataType.Password)]
            [Display(Name = "Current Password")]
            public string CurrentPassword { get; set; } = string.Empty;

            [Required(ErrorMessage = "New Password is required")]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "New Password")]
            public string NewPassword { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Confirm New Password")]
            [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        private long GetUserId()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId");
            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out long userId))
            {
                return userId;
            }
            return 0;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            long userId = GetUserId();
            if (userId == 0)
            {
                return RedirectToPage("/Login");
            }

            CurrentUser = await _context.UserMasters
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UmId == userId);

            if (CurrentUser == null)
            {
                return NotFound();
            }

            // Set Breadcrumbs
            ViewData["Title"] = "My Profile";
            //ViewData["BreadcrumbParent"] = "Home";
            
            return Page();
        }

        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            if (!ModelState.IsValid)
            {
                // Reload user data to redisplay profile info
                await OnGetAsync();
                return Page();
            }

            long userId = GetUserId();
            var user = await _context.UserMasters.FindAsync(userId);

            if (user == null)
            {
                return NotFound();
            }

            // Verify Current Password
            bool isPasswordCorrect = false;
            try
            {
                if (!string.IsNullOrEmpty(user.UmPassword))
                {
                    isPasswordCorrect = BCrypt.Net.BCrypt.Verify(ChangePasswordInput.CurrentPassword, user.UmPassword);
                }
            }
            catch
            {
                isPasswordCorrect = false;
            }

            if (!isPasswordCorrect)
            {
                ModelState.AddModelError("ChangePasswordInput.CurrentPassword", "Incorrect current password.");
                TempData["ErrorMessage"] = "Failed to update password. Please check your current password.";
                
                // If HTMX request, send header and return current page to ensure headers are sent
                if (Request.Headers.ContainsKey("HX-Request"))
                {
                    Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new { 
                        showToast = new { message = "Incorrect current password.", type = "error" } 
                    });
                    await OnGetAsync();
                    return Page();
                }

                // Reload user data
                await OnGetAsync();
                return Page();
            }

            // Hash New Password
            string newPasswordHash = BCrypt.Net.BCrypt.HashPassword(ChangePasswordInput.NewPassword, workFactor: 12);

            // Update Password
            user.UmPassword = newPasswordHash;
            user.LastUpdateOn = DateTime.Now;
            user.LastUpdatedBy = userId; // Self-updated

            await _context.SaveChangesAsync();

            StatusMessage = "Your password has been changed successfully.";

            // If HTMX request, send header and return Page() instead of redirect
            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new { 
                    showToast = new { message = "Password updated successfully!", type = "success" } 
                });
                await OnGetAsync();
                return Page();
            }

            return RedirectToPage();
        }
    }
}
