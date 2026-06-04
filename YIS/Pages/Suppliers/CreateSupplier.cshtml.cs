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

        public SelectList AcountId { get; set; } = default!;

        public string? Id { get; set; }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.AccountMasters != null)
            {
                var accounts = await _context.AccountMasters
                     .Where(a => a.UcpId == activeUcpId)
                     .ToListAsync();
                AcountId = new SelectList(accounts, "AccountId", "AccountName");
            }

            if (id == null)
            {
                // Create mode
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

            if (!ModelState.IsValid)
            {
                // Reload accounts if validaton fails
                if (_context.AccountMasters != null)
                {
                    var accounts = await _context.AccountMasters
                         .Where(a => a.UcpId == activeUcpId)
                         .ToListAsync();
                    AcountId = new SelectList(accounts, "AccountId", "AccountName");
                }
                return Page();
            }

            // Get Active Company Profile from Session
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (Supplier.SupplierId == 0)
            {
                // New Supplier
                Supplier.CreationOn = DateTime.Now;
                Supplier.UcpId = activeUcpId;
                _context.SupplierMasters.Add(Supplier);
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
                existingSupplier.AccountId = Supplier.AccountId;
                existingSupplier.UcpId = activeUcpId;
                existingSupplier.LastUpdateOn = DateTime.Now;

                _context.SupplierMasters.Update(existingSupplier);
                TempData["StatusMessage"] = $"Supplier '{existingSupplier.SupplierName}' updated successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
