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
                Customer = new CustomerMaster { CustomerId = 0 };
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

            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            ModelState.Remove("Customer.CustomerId");

            try
            {
                if (Customer.CustomerId == 0)
                {
                    // New Customer
                    Customer.CreationOn = DateTime.Now;
                    Customer.UcpId = activeUcpId;
                    _context.CustomerMasters.Add(Customer);
                    await _context.SaveChangesAsync();

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

                    // Dedicated GL Account Mappings
                    existingCustomer.AccountId = Customer.AccountId;
                    existingCustomer.SalesAccountId = Customer.SalesAccountId;
                    existingCustomer.TaxAccountId = Customer.TaxAccountId;
                    existingCustomer.CommExpenseAccountId = Customer.CommExpenseAccountId;
                    existingCustomer.CommPayableAccountId = Customer.CommPayableAccountId;

                    existingCustomer.UcpId = activeUcpId;
                    existingCustomer.LastUpdateOn = DateTime.Now;

                    _context.CustomerMasters.Update(existingCustomer);
                    await _context.SaveChangesAsync();

                    TempData["StatusMessage"] = $"Customer '{existingCustomer.CustomerName}' updated successfully.";
                }

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error saving customer: " + ex.Message;
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
