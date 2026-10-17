using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Vouchers
{
    public class ReceiptVoucherModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ReceiptVoucherModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = new Entry { EntryId = 0 };

        [BindProperty]
        public List<RVLineItem> LineItems { get; set; } = new List<RVLineItem>();

        public SelectList? AccountList { get; set; }
        public SelectList? BankCashAccountList { get; set; }
        public SelectList? CustomerList { get; set; }
        public string DisplayEntryNumber { get; set; } = "New";

        [BindProperty]
        public bool IsPosted { get; set; }

        public class RVLineItem
        {
            public long EntryId { get; set; }
            public long? LinkedAccountId { get; set; }
            public string? ChequeNo { get; set; }
            public DateTime? ChequeDate { get; set; }
            public string? Description { get; set; }
            public double? Amount { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            var path = "/Vouchers/ReceiptVoucher"; // Standardize path for RBAC
            if (!AuthHelper.CanRead(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Receipt Vouchers.";
                return RedirectToPage("/Index");
            }

            await LoadDropdowns();

            if (id.HasValue && id > 0)
            {
                Entry = await _context.Entries.FindAsync(id);
                if (Entry == null || Entry.EntryType != "RV") return NotFound();

                DisplayEntryNumber = $"RV-{Entry.EntryNumber}";

                // Check if posted
                IsPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

                var children = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "RVL")
                    .ToListAsync();

                LineItems = children.Select(c =>
                {
                    long.TryParse(c.EntryAttribute2, out long parsedAcc);

                    DateTime? cDate = null;
                    if (DateTime.TryParse(c.EntryAttribute5, out DateTime parsedDate)) cDate = parsedDate;

                    return new RVLineItem
                    {
                        EntryId = c.EntryId,
                        LinkedAccountId = parsedAcc > 0 ? parsedAcc : null,
                        ChequeNo = c.EntryAttribute4,
                        ChequeDate = cDate,
                        Description = c.EntryRemarks,
                        Amount = c.Amount
                    };
                }).ToList();
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryType = "RV",
                    EntryDate = DateTime.Today
                };
                LineItems.Add(new RVLineItem());
            }

            return Page();
        }

        private async Task LoadDropdowns()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return;

            if (_context.AccountMasters != null)
            {
                var allAccounts = await _context.AccountMasters
                    .Include(a => a.Category)
                    .Where(a => a.UcpId == activeUcpId)
                    .OrderBy(a => a.AccountName)
                    .ToListAsync();

                AccountList = new SelectList(allAccounts, "AccountId", "AccountName");

                var bankCashAccounts = allAccounts
                    .Where(a => a.Category?.CategoryName == "CASH N BANK")
                    .ToList();
                BankCashAccountList = new SelectList(bankCashAccounts, "AccountId", "AccountName");
            }

            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .Select(c => new { Id = c.CustomerId.ToString(), Name = c.CustomerName })
                    .OrderBy(c => c.Name)
                    .ToListAsync();
                CustomerList = new SelectList(customers, "Id", "Name");
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var path = "/Vouchers/ReceiptVoucher";
            bool isNew = Entry.EntryId == 0;

            if (isNew && !AuthHelper.CanCreate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to create Receipt Vouchers.";
                return RedirectToPage("./ReceiptVoucherIndex");
            }

            if (!isNew)
            {
                if (!AuthHelper.CanUpdate(HttpContext, path))
                {
                    TempData["ErrorMessage"] = "Access Denied: You do not have permission to update Receipt Vouchers.";
                    return RedirectToPage("./ReceiptVoucherIndex");
                }

                // Check if posted
                bool isPosted = await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId);
                if (isPosted)
                {
                    TempData["ErrorMessage"] = "Cannot update: This voucher is posted to the ledger.";
                    return RedirectToPage("./ReceiptVoucherIndex");
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                return Page();
            }

            Entry.EntryType = "RV";

            // Calculate Total
            double totalAmt = 0;
            foreach (var l in LineItems)
            {
                totalAmt += l.Amount ?? 0;
            }
            Entry.Amount = totalAmt;

            // Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (Entry.EntryId == 0)
            {
                Entry.UcpId = activeUcpId;
                Entry.CreationOn = DateTime.Now;
                Entry.CreatedBy = Helpers.AuthHelper.GetUserId(User);
                var last = await _context.Entries
                   .Where(e => e.EntryType == "RV" && e.UcpId == activeUcpId)
                   .OrderByDescending(e => e.EntryNumber)
                   .FirstOrDefaultAsync();
                Entry.EntryNumber = (last?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
                await _context.SaveChangesAsync();

                // Children
                foreach (var l in LineItems)
                {
                    if (l.LinkedAccountId.HasValue && l.Amount.HasValue)
                    {
                        var child = new Entry
                        {
                            EntryId = 0,
                            EntryType = "RVL",
                            EntryRefId = Entry.EntryId,
                            EntryDate = Entry.EntryDate,
                            CreationOn = DateTime.Now,
                            CreatedBy = Helpers.AuthHelper.GetUserId(User),

                            // Map Fields
                            EntryAttribute2 = l.LinkedAccountId.ToString(), // Account/Supplier
                            EntryAttribute4 = l.ChequeNo, // Cheque No
                            EntryAttribute5 = l.ChequeDate?.ToString("yyyy-MM-dd"), // Cheque Date
                            EntryRemarks = l.Description,
                            Amount = l.Amount,
                            UcpId = activeUcpId
                        };

                        _context.Entries.Add(child);
                    }
                }
            }
            else
            {
                var existing = await _context.Entries.FindAsync(Entry.EntryId);
                if (existing == null) return NotFound();

                existing.EntryDate = Entry.EntryDate;
                existing.EntryRemarks = Entry.EntryRemarks;
                existing.Amount = Entry.Amount;

                // Update Header Attributes
                existing.EntryAttribute1 = Entry.EntryAttribute1; // Bank/Cash Account
                existing.EntryAttribute2 = Entry.EntryAttribute2; // Customer
                existing.UcpId = activeUcpId;

                existing.LastUpdateOn = DateTime.Now;
                existing.LastUpdatedBy = Helpers.AuthHelper.GetUserId(User);

                var existingChildren = await _context.Entries.Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "RVL").ToListAsync();
                _context.Entries.RemoveRange(existingChildren);

                foreach (var l in LineItems)
                {
                    if (l.LinkedAccountId.HasValue && l.Amount.HasValue)
                    {
                        var child = new Entry
                        {
                            EntryId = 0,
                            EntryType = "RVL",
                            EntryRefId = Entry.EntryId,
                            EntryDate = Entry.EntryDate,
                            CreationOn = DateTime.Now,
                            CreatedBy = Helpers.AuthHelper.GetUserId(User), // New children in update

                            EntryAttribute2 = l.LinkedAccountId.ToString(), // Account/Supplier
                            EntryAttribute4 = l.ChequeNo, // Cheque No
                            EntryAttribute5 = l.ChequeDate?.ToString("yyyy-MM-dd"), // Cheque Date
                            EntryRemarks = l.Description,
                            Amount = l.Amount,
                            UcpId = activeUcpId
                        };

                        _context.Entries.Add(child);
                    }
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = isNew ? "Receipt voucher created successfully!" : "Receipt voucher updated successfully!";
            return RedirectToPage("./ReceiptVoucherIndex");
        }
    }
}
