using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Customers
{
    public class CreateCustomerModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateCustomerModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public CustomerMaster Customer { get; set; } = default!;

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
            var customer = await _context.CustomerMasters.FindAsync(id);

            if (customer == null)
            {
                return NotFound();
            }

            Customer = customer;
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

            if (Customer.CustomerId == 0)
            {
                // New Customer
                Customer.CreationOn = DateTime.Now;
                Customer.UcpId = activeUcpId;
                _context.CustomerMasters.Add(Customer);
                TempData["StatusMessage"] = $"Customer '{Customer.CustomerName}' created successfully.";
            }
            else
            {
                // Update Customer
                var existingCustomer = await _context.CustomerMasters.FindAsync(Customer.CustomerId);
                if (existingCustomer == null)
                {
                    return NotFound();
                }

                existingCustomer.CustomerName = Customer.CustomerName;
                existingCustomer.Province = Customer.Province;
                existingCustomer.CustomerAddress = Customer.CustomerAddress;
                existingCustomer.CustomerContact = Customer.CustomerContact;
                existingCustomer.CustomerEmail = Customer.CustomerEmail;
                existingCustomer.CustomerNtnNo = Customer.CustomerNtnNo;
                existingCustomer.CustomerStrNo = Customer.CustomerStrNo;
                existingCustomer.CustomerCnic = Customer.CustomerCnic;
                existingCustomer.RegistrationType = Customer.RegistrationType;
                existingCustomer.CustomerDeactiveOn = Customer.CustomerDeactiveOn;
                existingCustomer.AccountId = Customer.AccountId;
                existingCustomer.UcpId = activeUcpId;
                existingCustomer.LastUpdateOn = DateTime.Now;

                _context.CustomerMasters.Update(existingCustomer);
                TempData["StatusMessage"] = $"Customer '{existingCustomer.CustomerName}' updated successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
