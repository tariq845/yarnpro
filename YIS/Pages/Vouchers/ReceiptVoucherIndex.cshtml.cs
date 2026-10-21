using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Vouchers
{
    public class ReceiptVoucherIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ReceiptVoucherIndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ReceiptVoucherViewModel> VoucherList { get; set; } = new List<ReceiptVoucherViewModel>();

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public class ReceiptVoucherViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public double? Amount { get; set; }
            public int ItemCount { get; set; }
            public string? Account { get; set; } // Customer Name
            public string? Remarks { get; set; } // EntryRemarks
            public bool IsPosted { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "RV" && e.UcpId == activeUcpId);

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    var matchingCustomerIds = new List<string>();
                    if (_context.CustomerMasters != null)
                    {
                        matchingCustomerIds = await _context.CustomerMasters
                            .Where(c => c.UcpId == activeUcpId && c.CustomerName != null && c.CustomerName.ToLower().Contains(term))
                            .Select(c => c.CustomerId.ToString())
                            .ToListAsync();
                    }

                    query = query.Where(e =>
                        e.EntryNumber.ToString().Contains(term) ||
                        (e.EntryRemarks != null && e.EntryRemarks.ToLower().Contains(term)) ||
                        (e.EntryAttribute1 != null && e.EntryAttribute1.ToLower().Contains(term)) ||
                        (e.EntryAttribute2 != null && (matchingCustomerIds.Contains(e.EntryAttribute2) || e.EntryAttribute2.ToLower().Contains(term)))
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

                // Get all Customer IDs from parent entries to resolve names
                var customerIds = parentEntries
                    .Where(e => !string.IsNullOrEmpty(e.EntryAttribute2))
                    .Select(e => long.TryParse(e.EntryAttribute2, out long id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                var customerMap = new Dictionary<long, string>();
                if (_context.CustomerMasters != null && customerIds.Any())
                {
                    var idsString = string.Join(",", customerIds);
                    var sql = $"SELECT * FROM CUSTOMER_MASTER WHERE CUSTOMER_ID IN ({idsString})";
                    var customers = await _context.CustomerMasters
                        .FromSqlRaw(sql)
                        .AsNoTracking()
                        .ToListAsync();
                    customerMap = customers.ToDictionary(c => c.CustomerId, c => c.CustomerName ?? "Unknown");
                }

                // Get child counts
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "RVL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);

                // Check which entries are posted
                var entryIds = parentEntries.Select(e => e.EntryId).ToList();
                var postedEntryIds = new HashSet<long>();
                if (entryIds.Any())
                {
                    // Use Raw SQL to avoid EF Core generating OPENJSON/WITH syntax not supported by older SQL levels
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
                    string custName = "Not Set";
                    if (!string.IsNullOrEmpty(e.EntryAttribute2) && long.TryParse(e.EntryAttribute2, out long cId))
                    {
                        customerMap.TryGetValue(cId, out custName);
                    }

                    return new ReceiptVoucherViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        Amount = e.Amount,
                        ItemCount = childCounts.GetValueOrDefault(e.EntryId, 0),
                        IsPosted = postedEntryIds.Contains(e.EntryId),
                        Account = custName,
                        Remarks = e.EntryRemarks
                    };
                }).ToList();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            var path = "/Vouchers/ReceiptVoucher"; // Use fixed path for consistency
            if (!AuthHelper.CanDelete(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete Receipt Vouchers.";
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
                var children = await _context.Entries.Where(e => e.EntryRefId == id && e.EntryType == "RVL").ToListAsync();
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

            // Get line items (RVL entries)
            var lineItems = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "RVL")
                .ToListAsync();

            if (!lineItems.Any())
            {
                TempData["ErrorMessage"] = "No line items found to post.";
                return RedirectToPage();
            }

            // Get the next TransNo
            var maxTransNo = await _context.Transactions.MaxAsync(t => (long?)t.TransNo) ?? 0;
            var newTransNo = maxTransNo + 1;

            // Get Active Company Profile from Session (Per-user storage)
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (activeUcpId == null)
            {
                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Get Customer Account from Parent Entry (EntryAttribute2)
                long? customerAccountId = null;
                if (long.TryParse(parentEntry.EntryAttribute2, out long customerId))
                {
                    var customer = await _context.CustomerMasters.FindAsync(customerId);
                    customerAccountId = customer?.AccountId;

                    // Fallback to matching by name if AccountId is null
                    if (customerAccountId == null && customer != null)
                    {
                        var matchedAccount = await _context.AccountMasters
                            .FirstOrDefaultAsync(a => a.AccountName == customer.CustomerName && a.UcpId == activeUcpId);
                        customerAccountId = matchedAccount?.AccountId;
                    }
                }

                if (customerAccountId == null)
                {
                    TempData["ErrorMessage"] = "Cannot post: Customer account not found or not linked. Please link an account to the customer first.";
                    return RedirectToPage();
                }

                // Entry 1: Credit - Customer Account (Total Amount)
                var creditTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "RV",
                    AccountId = customerAccountId,
                    Debit = 0,
                    Credit = parentEntry.Amount ?? 0,
                    Remarks = parentEntry.EntryRemarks ?? $"Receipt Voucher RV-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(creditTrans);

                // Entries 2...N: Debit - Each Bank/Cash Account from Line Items
                foreach (var line in lineItems)
                {
                    // Parse AccountId from EntryAttribute2 (stored as string in RVL)
                    long? lineAccountId = null;
                    if (long.TryParse(line.EntryAttribute2, out long parsedAccountId))
                    {
                        lineAccountId = parsedAccountId;
                    }

                    var debitTrans = new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "RV",
                        AccountId = lineAccountId,
                        Debit = line.Amount ?? 0,
                        Credit = 0,
                        Remarks = line.EntryRemarks ?? parentEntry.EntryRemarks ?? $"Receipt Voucher RV-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    };
                    _context.Transactions.Add(debitTrans);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["SuccessMessage"] = $"Voucher RV-{parentEntry.EntryNumber} posted to ledger successfully.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error posting to ledger: " + ex.Message;
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUnpostFromLedgerAsync(long id)
        {
            var path = "/Vouchers/ReceiptVoucher";
            if (!AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to unpost Receipt Vouchers.";
                return RedirectToPage();
            }

            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "RV")
            {
                TempData["ErrorMessage"] = "Receipt voucher not found.";
                return RedirectToPage();
            }

            var transRows = await _context.Transactions
                .Where(t => t.EntryId == id)
                .ToListAsync();

            if (!transRows.Any())
            {
                TempData["ErrorMessage"] = "This voucher is not posted to ledger.";
                return RedirectToPage();
            }

            using var dbTxn = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Transactions.RemoveRange(transRows);
                await _context.SaveChangesAsync();
                await dbTxn.CommitAsync();

                TempData["SuccessMessage"] = $"Receipt Voucher RV-{entry.EntryNumber} has been unposted from the ledger. {transRows.Count} GL entries removed.";
            }
            catch (Exception ex)
            {
                await dbTxn.RollbackAsync();
                TempData["ErrorMessage"] = "Error unposting from ledger: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}
