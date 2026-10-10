using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Suppliers
{
    public class CreateSupplierModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateSupplierModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public SupplierMaster Supplier { get; set; } = default!;

        public SelectList AccountList { get; set; } = default!;
        public SelectList AcountId => AccountList;

        public string? Id { get; set; }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            await LoadAccountsDropdown(activeUcpId);

            if (id == null)
            {
                // Create mode
                Supplier = new SupplierMaster { SupplierId = 0 };
                return Page();
            }

            // Edit mode
            var supplier = await _context.SupplierMasters.FindAsync(id);

            if (supplier == null)
            {
                return NotFound();
            }

            Supplier = supplier;
            Id = id.ToString();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            ModelState.Remove("Supplier.SupplierId");

            try
            {
                if (Supplier.SupplierId == 0)
                {
                    // New Supplier
                    Supplier.CreationOn = DateTime.Now;
                    Supplier.UcpId = activeUcpId;
                    _context.SupplierMasters.Add(Supplier);
                    await _context.SaveChangesAsync();

                    TempData["StatusMessage"] = $"Supplier '{Supplier.SupplierName}' created successfully.";
                }
                else
                {
                    // Update Supplier
                    var existingSupplier = await _context.SupplierMasters.FindAsync(Supplier.SupplierId);
                    if (existingSupplier == null)
                    {
                        return NotFound();
                    }

                    existingSupplier.SupplierName = Supplier.SupplierName;
                    existingSupplier.SupplierAddress = Supplier.SupplierAddress;
                    existingSupplier.SupplierContact = Supplier.SupplierContact;
                    existingSupplier.SupplierEmail = Supplier.SupplierEmail;
                    existingSupplier.SupplierNtnNo = Supplier.SupplierNtnNo;
                    existingSupplier.SupplierStrNo = Supplier.SupplierStrNo;
                    existingSupplier.SupplierCnic = Supplier.SupplierCnic;
                    existingSupplier.SupplierDeactiveOn = Supplier.SupplierDeactiveOn;

                    // Dedicated GL Account Mappings
                    existingSupplier.AccountId = Supplier.AccountId;
                    existingSupplier.PurchaseAccountId = Supplier.PurchaseAccountId;
                    existingSupplier.TaxAccountId = Supplier.TaxAccountId;
                    existingSupplier.CommExpenseAccountId = Supplier.CommExpenseAccountId;
                    existingSupplier.CommPayableAccountId = Supplier.CommPayableAccountId;

                    existingSupplier.UcpId = activeUcpId;
                    existingSupplier.LastUpdateOn = DateTime.Now;

                    _context.SupplierMasters.Update(existingSupplier);
                    await _context.SaveChangesAsync();

                    TempData["StatusMessage"] = $"Supplier '{existingSupplier.SupplierName}' updated successfully.";
                }

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error saving supplier: " + ex.Message;
                await LoadAccountsDropdown(activeUcpId);
                return Page();
            }
        }

        private async Task LoadAccountsDropdown(long? activeUcpId)
        {
            if (_context.AccountMasters != null && activeUcpId.HasValue)
            {
                var accounts = await _context.AccountMasters
                     .Where(a => a.UcpId == activeUcpId)
                     .OrderBy(a => a.AccountName)
                     .ToListAsync();
                AccountList = new SelectList(accounts, "AccountId", "AccountName");
            }
            else
            {
                AccountList = new SelectList(new List<AccountMaster>(), "AccountId", "AccountName");
            }
        }
    }
}
