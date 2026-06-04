using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Purchase
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<PurchaseViewModel> Entries { get; set; } = new List<PurchaseViewModel>();
        public Dictionary<long, string> SupplierNames { get; set; } = new Dictionary<long, string>();

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? PsdId { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public class PurchaseViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public int ItemCount { get; set; }
            public double? GrandTotalNet { get; set; }
            public long? EntryRefNo { get; set; }
            public long? PsdNumber { get; set; }
            public bool IsPosted { get; set; }
            public double? Rate { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.SupplierMasters != null)
            {
                SupplierNames = await _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId)
                    .ToDictionaryAsync(s => s.SupplierId, s => s.SupplierName ?? "Unknown");
            }

            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "PUR" && e.UcpId == activeUcpId);

                if (PsdId.HasValue)
                {
                    query = query.Where(e => e.EntryRefNo == PsdId.Value);
                }

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var term = SearchTerm.ToLower();
                    query = query.Where(e =>
                        e.EntryNumber.ToString().Contains(term) ||
                        (e.EntryAttribute1 != null && e.EntryAttribute1.ToLower().Contains(term)) ||
                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(term)) ||
                        (e.EntryRefNo != null && e.EntryRefNo.ToString().Contains(term))
                    );
                }

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                // Get Parent Entries
                var parents = await query
                    .OrderByDescending(e => e.EntryId)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
                    .ToListAsync();

                // Get Child Counts
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "PURL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { pId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.pId ?? 0, v => v.Count);

                // Check which entries are posted
                var entryIds = parents.Select(e => e.EntryId).ToList();
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

                // Get PSD Numbers mapping
                var psdIds = parents.Where(e => e.EntryRefNo.HasValue).Select(e => e.EntryRefNo!.Value).Distinct().ToList();
                var psdNumberMap = new Dictionary<long, long>();
                if (psdIds.Any())
                {
                    psdNumberMap = await _context.Entries
                        .Where(e => e.EntryType == "PSD" && psdIds.Contains(e.EntryId))
                        .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);
                }
                
                Entries = parents.Select(e =>
                {
                    string supName = "Unknown";
                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long sId))
                    {
                        if (SupplierNames.ContainsKey(sId)) supName = SupplierNames[sId];
                    }

                    double.TryParse(e.EntryAttribute3, out double netAmt);

                    return new PurchaseViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        SupplierName = supName,
                        GrandTotalNet = netAmt, // Assuming Grand Total Net is stored in Attr3
                        ItemCount = childCounts.ContainsKey(e.EntryId) ? childCounts[e.EntryId] : (e.ItemId.HasValue ? 1 : 0), // Fallback for legacy
                        EntryRefNo = e.EntryRefNo,
                        PsdNumber = e.EntryRefNo.HasValue && psdNumberMap.ContainsKey(e.EntryRefNo.Value) ? psdNumberMap[e.EntryRefNo.Value] : e.EntryRefNo,
                        IsPosted = postedEntryIds.Contains(e.EntryId)
                    };
                }).ToList();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanDelete(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete Purchase entries.";
                return RedirectToPage();
            }

            if (_context.Entries == null) return NotFound();

            // Check if posted
            bool isPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);
            if (isPosted)
            {
                TempData["ErrorMessage"] = "Cannot delete: This purchase entry has already been posted to the ledger.";
                return RedirectToPage();
            }

            // Delete Children
            var children = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "PURL")
                .ToListAsync();
            _context.Entries.RemoveRange(children);

            // Delete Parent
            var entry = await _context.Entries.FindAsync(id);
            if (entry != null && entry.EntryType == "PUR")
            {
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
                TempData["ErrorMessage"] = "This purchase entry is already posted.";
                return RedirectToPage();
            }

            // Get the parent entry
            var parentEntry = await _context.Entries.FindAsync(id);
            if (parentEntry == null || parentEntry.EntryType != "PUR")
            {
                TempData["ErrorMessage"] = "Purchase entry not found.";
                return RedirectToPage();
            }

            // Get Supplier Account
            long? supplierAccountId = null;
            if (long.TryParse(parentEntry.EntryAttribute1, out long supplierId))
            {
                var supplier = await _context.SupplierMasters.FindAsync(supplierId);
                supplierAccountId = supplier?.AccountId;
            }

            if (supplierAccountId == null)
            {
                TempData["ErrorMessage"] = "Supplier account not found. Please link an account to the supplier first.";
                return RedirectToPage();
            }

            // Get Grand Total (inc. GST) from EntryAttribute3
            double grandTotal = 0;
            double.TryParse(parentEntry.EntryAttribute3, out grandTotal);

            if (grandTotal <= 0)
            {
                TempData["ErrorMessage"] = "Cannot post: Grand total is zero or invalid.";
                return RedirectToPage();
            }

            // Use the entry's own EntryNumber as TransNo (PUR-1 → TRANS_NO = 1)
            var newTransNo = parentEntry.EntryNumber ?? 0;

            if (newTransNo == 0)
            {
                TempData["ErrorMessage"] = "Cannot post: Purchase Entry Number is missing or zero.";
                return RedirectToPage();
            }

            // Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            // Dynamically find "Purchase" account from Chart of Accounts
            var purchaseAccount = await _context.AccountMasters
                .FirstOrDefaultAsync(a => a.AccountName!.Contains("Purchase") && a.UcpId == activeUcpId);

            if (purchaseAccount == null)
            {
                TempData["ErrorMessage"] = "Cannot post: 'Purchase' account not found in Chart of Accounts. Please create it first.";
                return RedirectToPage();
            }
            // Dynamically find "Trade Creditors" account from Chart of Accounts
            var tradeCreditorAccount = await _context.AccountMasters
                .FirstOrDefaultAsync(a => a.AccountName!.Contains("TRADE CREATOR (LOCAL)") && a.UcpId == activeUcpId);

            if (tradeCreditorAccount == null)
            {
                TempData["ErrorMessage"] = "Cannot post: '(TRADE CREATOR (LOCAL)' account not found in Chart of Accounts. Please create it first.";
                return RedirectToPage();
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Entry 1: Debit - Purchase Account (dynamically picked from Chart of Accounts)
                var debitTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "PUR",
                    AccountId = purchaseAccount.AccountId,
                    Debit = grandTotal,
                    Credit = 0,
                    Remarks = $"Purchase Entry PUR-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(debitTrans);

                // Entry 2: Credit - Trade Creditors Account (dynamically picked from Chart of Accounts)
                var creditTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "PUR",
                    AccountId = tradeCreditorAccount.AccountId,
                    Debit = 0,
                    Credit = grandTotal,
                    Remarks = $"Purchase Entry PUR-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(creditTrans);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["StatusMessage"] = $"Purchase PUR-{parentEntry.EntryNumber} posted to ledger successfully.";
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
