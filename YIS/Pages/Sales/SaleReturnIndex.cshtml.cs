using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Sales
{
    public class SaleReturnIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SaleReturnIndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ReturnViewModel> CNList { get; set; } = new List<ReturnViewModel>();

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
            public string? AttributeName { get; set; } // Customer
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
                    .Where(e => e.EntryType == "CN" && e.UcpId == activeUcpId);

                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);

                    var matchedCustomerIds = await _context.CustomerMasters
                        .Where(c => c.UcpId == activeUcpId && c.CustomerName.ToLower().Contains(smart.CleanTerm))
                        .Select(c => c.CustomerId.ToString())
                        .ToListAsync();

                    query = query.Where(e =>
                        (smart.HasNumber && e.EntryNumber == smart.ExtractedNumber.Value) ||
                        e.EntryNumber.ToString().Contains(smart.CleanTerm) ||
                        (e.EntryAttribute1 != null && (matchedCustomerIds.Contains(e.EntryAttribute1) || e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))) ||
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
                        .Where(e => e.EntryType == "CN" && e.UcpId == activeUcpId && e.EntryNumber.HasValue)
                        .OrderByDescending(e => e.EntryId)
                        .Take(6)
                        .Select(e => "CN-" + e.EntryNumber)
                        .ToListAsync();

                    var topCusts = await _context.CustomerMasters
                        .Where(c => c.UcpId == activeUcpId && !string.IsNullOrEmpty(c.CustomerName))
                        .OrderBy(c => c.CustomerName)
                        .Take(6)
                        .Select(c => c.CustomerName)
                        .ToListAsync();

                    SearchSuggestions = topReturns.Concat(topCusts).Distinct().ToList();
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
                    .Where(e => e.EntryType == "CNL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);

                var customers = await _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .ToDictionaryAsync(s => s.CustomerId.ToString(), s => s.CustomerName);

                CNList = parentEntries.Select(e => new ReturnViewModel
                {
                    EntryId = e.EntryId,
                    EntryNumber = e.EntryNumber,
                    EntryDate = e.EntryDate,
                    AttributeName = customers.ContainsKey(e.EntryAttribute1 ?? "") ? customers[e.EntryAttribute1 ?? ""] : e.EntryAttribute1,
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
                var children = await _context.Entries.Where(e => e.EntryRefId == id && e.EntryType == "CNL").ToListAsync();
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
            if (parentEntry == null || parentEntry.EntryType != "CN")
            {
                TempData["ErrorMessage"] = "Credit Note not found.";
                return RedirectToPage();
            }

            var lineItems = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "CNL")
                .ToListAsync();

            if (!lineItems.Any())
            {
                TempData["ErrorMessage"] = "No line items found to post.";
                return RedirectToPage();
            }

            long? customerAccountId = null;
            CustomerMaster? customer = null;
            if (long.TryParse(parentEntry.EntryAttribute1, out long customerId))
            {
                customer = await _context.CustomerMasters.FindAsync(customerId);
                customerAccountId = customer?.AccountId;
            }

            if (customerAccountId == null)
            {
                TempData["ErrorMessage"] = "Customer account not found.";
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
                // CN is Sales Return: Debit Sales/GST, Credit Customer
                AccountMaster? salesAccount = null;
                if (customer?.SalesAccountId.HasValue == true)
                {
                    salesAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountId == customer.SalesAccountId.Value && a.UcpId == activeUcpId);
                }
                if (salesAccount == null)
                {
                    salesAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountName.Contains("Sales") && !a.AccountName.Contains("GST") && a.UcpId == activeUcpId);
                }

                AccountMaster? gstAccount = null;
                if (customer?.TaxAccountId.HasValue == true)
                {
                    gstAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountId == customer.TaxAccountId.Value && a.UcpId == activeUcpId);
                }
                if (gstAccount == null)
                {
                    gstAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountName.Contains("GST") && a.UcpId == activeUcpId);
                }

                foreach (var line in lineItems)
                {
                    if (salesAccount != null && (line.Amount ?? 0) > 0)
                    {
                        var debitSales = new Trans
                        {
                            TransId = 0,
                            TransNo = newTransNo,
                            TransType = "CN",
                            AccountId = salesAccount.AccountId,
                            Debit = line.Amount ?? 0,
                            Credit = 0,
                            Remarks = $"CN-{parentEntry.EntryNumber} Sales Return",
                            EntryId = id,
                            UcpId = activeUcpId
                        };
                        _context.Transactions.Add(debitSales);
                    }

                    if (gstAccount != null && (line.TaxAmount ?? 0) > 0)
                    {
                        var debitGst = new Trans
                        {
                            TransId = 0,
                            TransNo = newTransNo,
                            TransType = "CN",
                            AccountId = gstAccount.AccountId,
                            Debit = line.TaxAmount ?? 0,
                            Credit = 0,
                            Remarks = $"CN-{parentEntry.EntryNumber} GST Reversal",
                            EntryId = id,
                            UcpId = activeUcpId
                        };
                        _context.Transactions.Add(debitGst);
                    }
                }

                var creditTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "CN",
                    AccountId = customerAccountId,
                    Debit = 0,
                    Credit = parentEntry.Amount ?? 0,
                    Remarks = $"Credit Note CN-{parentEntry.EntryNumber} (Sales Return)",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(creditTrans);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["StatusMessage"] = $"Credit Note posted successfully.";
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
