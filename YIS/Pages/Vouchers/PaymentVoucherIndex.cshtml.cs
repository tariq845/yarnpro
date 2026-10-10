using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Vouchers
{
    public class PaymentVoucherIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PaymentVoucherIndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<PaymentVoucherViewModel> VoucherList { get; set; } = new List<PaymentVoucherViewModel>();

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public class PaymentVoucherViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public double? Amount { get; set; }
            public int ItemCount { get; set; }
            public string? Account { get; set; } // EntryAttribute1
            public string? Remarks { get; set; } // EntryAttribute2
            public bool IsPosted { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "PV" && e.UcpId == activeUcpId);

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(e =>
                        e.EntryNumber.ToString().Contains(term) ||
                        (e.EntryAttribute1 != null && e.EntryAttribute1.ToLower().Contains(term)) ||
                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(term))
                    );
                }

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                // Get parent entries
                var parentEntries = await query
                    .OrderByDescending(e => e.EntryId)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
                    .ToListAsync();

                // Get all Supplier IDs from parent entries to resolve names
                var supplierIds = parentEntries
                    .Where(e => !string.IsNullOrEmpty(e.EntryAttribute2))
                    .Select(e => long.TryParse(e.EntryAttribute2, out long id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                var supplierMap = new Dictionary<long, string>();
                if (_context.SupplierMasters != null && supplierIds.Any())
                {
                    var idsString = string.Join(",", supplierIds);
                    var sql = $"SELECT * FROM SUPPLIER_MASTER WHERE SUPPLIER_ID IN ({idsString})";
                    var suppliers = await _context.SupplierMasters
                        .FromSqlRaw(sql)
                        .AsNoTracking()
                        .ToListAsync();
                    supplierMap = suppliers.ToDictionary(s => s.SupplierId, s => s.SupplierName ?? "Unknown");
                }

                // Get child counts
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "PVL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);

                // Check which entries are posted
                var entryIds = parentEntries.Select(e => e.EntryId).ToList();
                var postedEntryIds = new HashSet<long>();
                if (entryIds.Any())
                {
                    var idsString = string.Join(",", entryIds);
                    var sql = $"SELECT * FROM TRANS WHERE ENTRY_ID IN ({idsString})";

                    var transactions = await _context.Transactions
                        .FromSqlRaw(sql)
                        .AsNoTracking()
                        .ToListAsync();

                    foreach (var t in transactions)
                    {
                        if (t.EntryId.HasValue) postedEntryIds.Add(t.EntryId.Value);
                    }
                }

                VoucherList = parentEntries.Select(e =>
                {
                    string supName = "Not Set";
                    if (!string.IsNullOrEmpty(e.EntryAttribute2) && long.TryParse(e.EntryAttribute2, out long sId))
                    {
                        supplierMap.TryGetValue(sId, out supName);
                    }

                    return new PaymentVoucherViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        Amount = e.Amount,
                        ItemCount = childCounts.GetValueOrDefault(e.EntryId, 0),
                        Account = supName, // Showing Supplier Name here
                        Remarks = e.EntryRemarks,
                        IsPosted = postedEntryIds.Contains(e.EntryId)
                    };
                }).ToList();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            var path = "/Vouchers/PaymentVoucher"; // Use fixed path for consistency
            if (!AuthHelper.CanDelete(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete Payment Vouchers.";
                return RedirectToPage();
            }

            // Check if posted
            bool isPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);
            if (isPosted)
            {
                TempData["ErrorMessage"] = "Cannot delete: This voucher has already been posted to the ledger.";
                return RedirectToPage();
            }

            var entry = await _context.Entries.FindAsync(id);
            if (entry != null)
            {
                // Delete children first
                var children = await _context.Entries.Where(e => e.EntryRefId == id && e.EntryType == "PVL").ToListAsync();
                _context.Entries.RemoveRange(children);

                _context.Entries.Remove(entry);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostPostToLedgerAsync(long id)
        {
            // Check if already posted
            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))
            {
                TempData["ErrorMessage"] = "This voucher is already posted.";
                return RedirectToPage();
            }

            // Get the parent voucher entry
            var parentEntry = await _context.Entries.FindAsync(id);
            if (parentEntry == null)
            {
                TempData["ErrorMessage"] = "Voucher not found.";
                return RedirectToPage();
            }

            // Get the next TransNo
            var maxTransNo = await _context.Transactions.MaxAsync(t => (long?)t.TransNo) ?? 0;
            var newTransNo = maxTransNo + 1;

            // Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            // Get Line Items (Bank/Cash accounts are here now)
            var lineItems = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "PVL")
                .ToListAsync();

            if (!lineItems.Any())
            {
                TempData["ErrorMessage"] = "Cannot post: Voucher has no line items.";
                return RedirectToPage();
            }

            // Dynamically find "Trade Creditors" account from Chart of Accounts
            var tradeCreditorAccount = await _context.AccountMasters
                .FirstOrDefaultAsync(a => (a.AccountName!.Contains("TRADE CREATOR (LOCAL)") || a.AccountName!.Contains("TRADE CREATOR")) && a.UcpId == activeUcpId);

            if (tradeCreditorAccount == null)
            {
                TempData["ErrorMessage"] = "Cannot post: 'TRADE CREATOR (LOCAL)' account not found in Chart of Accounts. Please create it first.";
                return RedirectToPage();
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Entry 1: Debit - Trade Creditors Account (Total Amount)
                var debitTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "PV",
                    AccountId = tradeCreditorAccount.AccountId,
                    Debit = parentEntry.Amount ?? 0,
                    Credit = 0,
                    Remarks = parentEntry.EntryRemarks ?? $"Payment Voucher PV-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(debitTrans);

                // Entries 2...N: Credit - Each Bank/Cash Account from Line Items
                foreach (var line in lineItems)
                {
                    if (!long.TryParse(line.EntryAttribute2, out long lineAccountId))
                    {
                        continue;
                    }

                    var creditTrans = new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "PV",
                        AccountId = lineAccountId,
                        Debit = 0,
                        Credit = line.Amount ?? 0,
                        Remarks = line.EntryRemarks ?? parentEntry.EntryRemarks ?? $"Payment Voucher PV-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    };
                    _context.Transactions.Add(creditTrans);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["SuccessMessage"] = $"Voucher PV-{parentEntry.EntryNumber} posted to ledger successfully.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error posting to ledger: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}
