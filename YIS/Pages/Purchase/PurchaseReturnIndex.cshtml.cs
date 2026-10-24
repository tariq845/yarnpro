using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Purchase
{
    public class PurchaseReturnIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PurchaseReturnIndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ReturnViewModel> DNList { get; set; } = new List<ReturnViewModel>();

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 10;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string SortDir { get; set; } = "desc";

        public List<string> SearchSuggestions { get; set; } = new();

        public PaginationViewModel Pagination { get; set; } = default!;

        public class ReturnViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? AttributeName { get; set; } // Supplier/Customer
            public double? Amount { get; set; } // Net Amount
            public int ItemCount { get; set; }
            public bool IsPosted { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "DN" && e.UcpId == activeUcpId);

                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);

                    var matchedSupplierIds = await _context.SupplierMasters
                        .Where(s => s.UcpId == activeUcpId && s.SupplierName.ToLower().Contains(smart.CleanTerm))
                        .Select(s => s.SupplierId.ToString())
                        .ToListAsync();

                    query = query.Where(e =>
                        (smart.HasNumber && e.EntryNumber == smart.ExtractedNumber.Value) ||
                        e.EntryNumber.ToString().Contains(smart.CleanTerm) ||
                        (e.EntryAttribute1 != null && (matchedSupplierIds.Contains(e.EntryAttribute1) || e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))) ||
                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(smart.CleanTerm))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                query = (SortDir == "asc")
                    ? query.OrderBy(e => e.EntryNumber).ThenBy(e => e.EntryId)
                    : query.OrderByDescending(e => e.EntryNumber).ThenByDescending(e => e.EntryId);

                var parentEntries = await query
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                // Live search suggestions
                try
                {
                    var topReturns = await _context.Entries
                        .Where(e => e.EntryType == "DN" && e.UcpId == activeUcpId && e.EntryNumber.HasValue)
                        .OrderByDescending(e => e.EntryId)
                        .Take(6)
                        .Select(e => "DN-" + e.EntryNumber)
                        .ToListAsync();

                    var topSupps = await _context.SupplierMasters
                        .Where(s => s.UcpId == activeUcpId && !string.IsNullOrEmpty(s.SupplierName))
                        .OrderBy(s => s.SupplierName)
                        .Take(6)
                        .Select(s => s.SupplierName)
                        .ToListAsync();

                    SearchSuggestions = topReturns.Concat(topSupps).Distinct().ToList();
                }
                catch
                {
                    SearchSuggestions = new List<string>();
                }

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

                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "DNL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);

                var suppliers = await _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId)
                    .ToDictionaryAsync(s => s.SupplierId.ToString(), s => s.SupplierName);

                DNList = parentEntries.Select(e => new ReturnViewModel
                {
                    EntryId = e.EntryId,
                    EntryNumber = e.EntryNumber,
                    EntryDate = e.EntryDate,
                    AttributeName = suppliers.ContainsKey(e.EntryAttribute1 ?? "") ? suppliers[e.EntryAttribute1 ?? ""] : e.EntryAttribute1,
                    Amount = e.Amount,
                    ItemCount = childCounts.GetValueOrDefault(e.EntryId, 0),
                    IsPosted = postedEntryIds.Contains(e.EntryId)
                }).ToList();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            // Check if posted
            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))
            {
                TempData["ErrorMessage"] = "Cannot delete: This entry has already been posted to the ledger.";
                return RedirectToPage();
            }

            var entry = await _context.Entries.FindAsync(id);
            if (entry != null)
            {
                var children = await _context.Entries.Where(e => e.EntryRefId == id && e.EntryType == "DNL").ToListAsync();
                _context.Entries.RemoveRange(children);
                _context.Entries.Remove(entry);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostPostToLedgerAsync(long id)
        {
            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))
            {
                TempData["ErrorMessage"] = "This entry is already posted.";
                return RedirectToPage();
            }

            var parentEntry = await _context.Entries.FindAsync(id);
            if (parentEntry == null || parentEntry.EntryType != "DN")
            {
                TempData["ErrorMessage"] = "Debit Note not found.";
                return RedirectToPage();
            }

            var lineItems = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "DNL")
                .ToListAsync();

            if (!lineItems.Any())
            {
                TempData["ErrorMessage"] = "No line items found to post.";
                return RedirectToPage();
            }

            long? supplierAccountId = null;
            SupplierMaster? supplier = null;
            if (long.TryParse(parentEntry.EntryAttribute1, out long supplierId))
            {
                supplier = await _context.SupplierMasters.FindAsync(supplierId);
                supplierAccountId = supplier?.AccountId;
            }

            if (supplierAccountId == null)
            {
                TempData["ErrorMessage"] = "Supplier account not found.";
                return RedirectToPage();
            }

            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            var maxTransNo = await _context.Transactions.MaxAsync(t => (long?)t.TransNo) ?? 0;
            var newTransNo = maxTransNo + 1;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // DN is Purchase Return: Debit Supplier, Credit Purchase/GST
                var debitTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "DN",
                    AccountId = supplierAccountId.Value,
                    Debit = parentEntry.Amount ?? 0,
                    Credit = 0,
                    Remarks = $"Debit Note DN-{parentEntry.EntryNumber} (Purchase Return)",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(debitTrans);

                AccountMaster? purchaseAccount = null;
                if (supplier?.PurchaseAccountId.HasValue == true)
                {
                    purchaseAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountId == supplier.PurchaseAccountId.Value && a.UcpId == activeUcpId);
                }
                if (purchaseAccount == null)
                {
                    purchaseAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountName.Contains("Purchase") && a.UcpId == activeUcpId);
                }

                AccountMaster? gstAccount = null;
                if (supplier?.TaxAccountId.HasValue == true)
                {
                    gstAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountId == supplier.TaxAccountId.Value && a.UcpId == activeUcpId);
                }
                if (gstAccount == null)
                {
                    gstAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountName.Contains("GST") && a.UcpId == activeUcpId);
                }

                foreach (var line in lineItems)
                {
                    if (purchaseAccount != null && (line.Amount ?? 0) > 0)
                    {
                        var creditPurchase = new Trans
                        {
                            TransId = 0,
                            TransNo = newTransNo,
                            TransType = "DN",
                            AccountId = purchaseAccount.AccountId,
                            Debit = 0,
                            Credit = line.Amount ?? 0,
                            Remarks = $"DN-{parentEntry.EntryNumber} Item Return",
                            EntryId = id,
                            UcpId = activeUcpId
                        };
                        _context.Transactions.Add(creditPurchase);
                    }

                    if (gstAccount != null && (line.TaxAmount ?? 0) > 0)
                    {
                        var creditGst = new Trans
                        {
                            TransId = 0,
                            TransNo = newTransNo,
                            TransType = "DN",
                            AccountId = gstAccount.AccountId,
                            Debit = 0,
                            Credit = line.TaxAmount ?? 0,
                            Remarks = $"DN-{parentEntry.EntryNumber} GST Reversal",
                            EntryId = id,
                            UcpId = activeUcpId
                        };
                        _context.Transactions.Add(creditGst);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["StatusMessage"] = $"Debit Note posted successfully.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }

            return RedirectToPage();
        }
    }
}
