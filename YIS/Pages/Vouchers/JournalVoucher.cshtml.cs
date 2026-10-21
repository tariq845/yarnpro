using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Vouchers
{
    public class JournalVoucherModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public JournalVoucherModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = new Entry { EntryId = 0 };

        [BindProperty]
        public List<JVLineItem> LineItems { get; set; } = new List<JVLineItem>();

        public SelectList? AccountList { get; set; }
        public string DisplayEntryNumber { get; set; } = "New";

        [BindProperty]
        public bool IsPosted { get; set; }

        public class JVLineItem
        {
            public long EntryId { get; set; }
            public long? LinkedAccountId { get; set; }
            public string? Description { get; set; }
            public double? Debit { get; set; }
            public double? Credit { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            var path = "/Vouchers/JournalVoucher"; // Standardize path for RBAC
            if (!AuthHelper.CanRead(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Journal Vouchers.";
                return RedirectToPage("/Index");
            }

            await LoadDropdowns();

            if (id.HasValue && id > 0)
            {
                Entry = await _context.Entries.FindAsync(id);
                if (Entry == null || Entry.EntryType != "JV") return NotFound();

                DisplayEntryNumber = $"JV-{Entry.EntryNumber}";

                // Check if posted
                IsPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

                var children = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "JVL")
                    .ToListAsync();

                LineItems = children.Select(c =>
                {
                    long.TryParse(c.EntryAttribute1, out long parsedAcc);
                    double.TryParse(c.EntryAttribute4, out double debit);
                    double.TryParse(c.EntryAttribute5, out double credit);

                    return new JVLineItem
                    {
                        EntryId = c.EntryId,
                        LinkedAccountId = parsedAcc > 0 ? parsedAcc : null,
                        Description = c.EntryRemarks,
                        Debit = debit > 0 ? debit : null,
                        Credit = credit > 0 ? credit : null
                    };
                }).ToList();
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryType = "JV",
                    EntryDate = DateTime.Today
                };
                LineItems.Add(new JVLineItem());
            }

            return Page();
        }

        private async Task LoadDropdowns()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return;

            if (_context.AccountMasters != null)
            {
                var accounts = await _context.AccountMasters
                    .Where(a => a.UcpId == activeUcpId)
                    .Select(a => new { Id = a.AccountId.ToString(), Name = a.AccountName })
                    .OrderBy(a => a.Name)
                    .ToListAsync();
                AccountList = new SelectList(accounts, "Id", "Name");
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var path = "/Vouchers/JournalVoucher";
            bool isNew = Entry.EntryId == 0;

            if (isNew && !AuthHelper.CanCreate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to create Journal Vouchers.";
                return RedirectToPage("./JournalVoucherIndex");
            }

            if (!isNew)
            {
                if (!AuthHelper.CanUpdate(HttpContext, path))
                {
                    TempData["ErrorMessage"] = "Access Denied: You do not have permission to update Journal Vouchers.";
                    return RedirectToPage("./JournalVoucherIndex");
                }

                // Check if posted
                bool isPosted = await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId);
                if (isPosted)
                {
                    TempData["ErrorMessage"] = "Cannot update: This voucher is posted to the ledger.";
                    return RedirectToPage("./JournalVoucherIndex");
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                return Page();
            }

            Entry.EntryType = "JV";

            // Validate Balance
            double totalDebit = LineItems.Sum(l => l.Debit ?? 0);
            double totalCredit = LineItems.Sum(l => l.Credit ?? 0);

            if (Math.Abs(totalDebit - totalCredit) > 0.01)
            {
                ModelState.AddModelError("", $"Journal is not balanced! Total Debit: {totalDebit}, Total Credit: {totalCredit}, Diff: {totalDebit - totalCredit}");
                await LoadDropdowns();
                return Page();
            }

            Entry.Amount = totalDebit; // Store total flow as amount

            // Get Active Company Profile from Session (Per-user storage)
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
                   .Where(e => e.EntryType == "JV" && e.UcpId == activeUcpId)
                   .OrderByDescending(e => e.EntryNumber)
                   .FirstOrDefaultAsync();
                Entry.EntryNumber = (last?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
                await _context.SaveChangesAsync();

                // Children
                foreach (var l in LineItems)
                {
                    if (l.LinkedAccountId.HasValue && ((l.Debit ?? 0) > 0 || (l.Credit ?? 0) > 0))
                    {
                        var child = new Entry
                        {
                            EntryId = 0,
                            EntryType = "JVL",
                            EntryRefId = Entry.EntryId,
                            EntryDate = Entry.EntryDate,
                            CreationOn = DateTime.Now,
                            CreatedBy = Helpers.AuthHelper.GetUserId(User),

                            // Map Fields based on request
                            EntryAttribute1 = l.LinkedAccountId.ToString(), // Account
                            // EntryAttribute2 & 3 skipped as per request logic (not specified)
                            EntryAttribute4 = (l.Debit ?? 0).ToString(), // DEBIT
                            EntryAttribute5 = (l.Credit ?? 0).ToString(), // CREDIT

                            EntryRemarks = l.Description,

                            // Standard Amount field logic: usually Debit is +ve, Credit is -ve or just magnitude
                            // Storing magnitude in Amount or logic?
                            // Let's store Debit - Credit in Amount for signed value
                            Amount = (l.Debit ?? 0) - (l.Credit ?? 0),
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
                existing.UcpId = activeUcpId;
                existing.LastUpdateOn = DateTime.Now;
                existing.LastUpdatedBy = Helpers.AuthHelper.GetUserId(User);

                var existingChildren = await _context.Entries.Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "JVL").ToListAsync();
                _context.Entries.RemoveRange(existingChildren);

                foreach (var l in LineItems)
                {
                    if (l.LinkedAccountId.HasValue && ((l.Debit ?? 0) > 0 || (l.Credit ?? 0) > 0))
                    {
                        var child = new Entry
                        {
                            EntryId = 0,
                            EntryType = "JVL",
                            EntryRefId = Entry.EntryId,
                            EntryDate = Entry.EntryDate,
                            CreationOn = DateTime.Now,
                            CreatedBy = Helpers.AuthHelper.GetUserId(User), // New children in update

                            EntryAttribute1 = l.LinkedAccountId.ToString(),
                            EntryAttribute4 = (l.Debit ?? 0).ToString(), // DEBIT
                            EntryAttribute5 = (l.Credit ?? 0).ToString(), // CREDIT

                            EntryRemarks = l.Description,
                            Amount = (l.Debit ?? 0) - (l.Credit ?? 0),
                            UcpId = activeUcpId
                        };

                        _context.Entries.Add(child);
                    }
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = isNew ? "Journal voucher created successfully!" : "Journal voucher updated successfully!";
            return RedirectToPage("./JournalVoucherIndex");
        }

        public async Task<IActionResult> OnPostUnpostFromLedgerAsync(long id)
        {
            var path = "/Vouchers/JournalVoucher";
            if (!AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to unpost Journal Vouchers.";
                return RedirectToPage(new { id });
            }

            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "JV")
            {
                TempData["ErrorMessage"] = "Journal voucher not found.";
                return RedirectToPage("./JournalVoucherIndex");
            }

            var transRows = await _context.Transactions
                .Where(t => t.EntryId == id)
                .ToListAsync();

            if (!transRows.Any())
            {
                TempData["ErrorMessage"] = "This voucher is not posted to ledger.";
                return RedirectToPage(new { id });
            }

            using var dbTxn = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Transactions.RemoveRange(transRows);
                await _context.SaveChangesAsync();
                await dbTxn.CommitAsync();

                TempData["StatusMessage"] = $"Journal Voucher JV-{entry.EntryNumber} has been unposted from the ledger. It is now unlocked and editable.";
            }
            catch (Exception ex)
            {
                await dbTxn.RollbackAsync();
                TempData["ErrorMessage"] = "Error unposting from ledger: " + ex.Message;
            }

            return RedirectToPage(new { id });
        }
    }
}
