using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Accounts
{
    public class CreateAccountModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateAccountModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public AccountMaster Account { get; set; } = default!;

        public SelectList CategoryId { get; set; } = default!;

        public string? Id { get; set; }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.CategoryMasters != null)
            {
                var categories = await _context.CategoryMasters
                     .Where(c => c.UcpId == activeUcpId)
                     .ToListAsync();
                CategoryId = new SelectList(categories, "CategoryId", "CategoryName");
            }

            if (id == null)
            {
                // Create mode
                return Page();
            }

            // Edit mode
            var account = await _context.AccountMasters.FindAsync(id);

            if (account == null)
            {
                return NotFound();
            }

            Account = account;
            Id = id.ToString(); // Helper for view
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (!ModelState.IsValid)
            {
                // Reload categories if returning page due to validation error
                if (_context.CategoryMasters != null)
                {
                    var categories = await _context.CategoryMasters
                         .Where(c => c.UcpId == activeUcpId)
                         .ToListAsync();
                    CategoryId = new SelectList(categories, "CategoryId", "CategoryName");
                }
                return Page();
            }

            // Get Active Company Profile from Session
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (Account.AccountId == 0)
            {
                // New Account
                Account.CreationOn = DateTime.Now;
                Account.UcpId = activeUcpId;
                _context.AccountMasters.Add(Account);
                TempData["StatusMessage"] = $"Account '{Account.AccountName}' created successfully.";
            }
            else
            {
                // Update Account
                var existingAccount = await _context.AccountMasters.FindAsync(Account.AccountId);
                if (existingAccount == null)
                {
                    return NotFound();
                }

                existingAccount.AccountName = Account.AccountName;
                existingAccount.CategoryId = Account.CategoryId;
                existingAccount.UcpId = activeUcpId;
                existingAccount.LastUpdateOn = DateTime.Now;

                _context.AccountMasters.Update(existingAccount);
                TempData["StatusMessage"] = $"Account '{existingAccount.AccountName}' updated successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
