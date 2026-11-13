using YIS.Services;
using ClosedXML.Excel;
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
                var companyCustomerIds = await _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .Select(c => c.CustomerId.ToString())
                    .ToListAsync();
                var query = _context.Entries
                    .Where(e => e.EntryType == "CN" && (e.UcpId == activeUcpId || (e.UcpId == null && e.EntryAttribute1 != null && companyCustomerIds.Contains(e.EntryAttribute1))));
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
            string customerName = "Customer";
            if (long.TryParse(parentEntry.EntryAttribute1, out long customerId))
            {
                customer = await _context.CustomerMasters.FindAsync(customerId);
                if (customer != null)
                {
                    customerName = customer.CustomerName ?? "Customer";
                    if (customer.AccountId.HasValue && customer.AccountId.Value > 0)
                    {
                        customerAccountId = customer.AccountId.Value;
                    }
                }
            }

            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            // Verify customer account exists in active company
            if (customerAccountId.HasValue && customerAccountId.Value > 0)
            {
                var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == customerAccountId.Value && a.UcpId == activeUcpId);
                if (!exists) customerAccountId = null;
            }

            // Fallback 1: Match by Customer Name in active company
            if (customerAccountId == null || customerAccountId <= 0)
            {
                var matchedCustomerAcc = await _context.AccountMasters
                    .FirstOrDefaultAsync(a => a.AccountName == customerName && a.UcpId == activeUcpId);
                if (matchedCustomerAcc != null)
                {
                    customerAccountId = matchedCustomerAcc.AccountId;
                }
            }

            // Fallback 2: Trade Debtors account
            if (customerAccountId == null || customerAccountId <= 0)
            {
                var tradeDebtorAcc = await _context.AccountMasters
                    .Where(a => a.UcpId == activeUcpId && 
                        (a.AccountName!.Contains("LOCAL DEBTOR") || 
                         a.AccountName!.Contains("TRADE DEBTOR") || 
                         a.AccountName!.Contains("DEBTOR")))
                    .OrderBy(a => a.AccountId)
                    .FirstOrDefaultAsync();

                if (tradeDebtorAcc != null)
                {
                    customerAccountId = tradeDebtorAcc.AccountId;
                }
                else
                {
                    var newAcc = await GetOrCreateAccountAsync("Trade Debtors (Local)", "Debtor", "Asset", activeUcpId.Value);
                    customerAccountId = newAcc.AccountId;
                }
            }

            // Commission details
            double commissionAmt = parentEntry.EntryCommAmt ?? 0;
            long? commExpenseAccountId = null;
            long? commPayableAccountId = null;

            if (commissionAmt > 0)
            {
                if (customer?.CommExpenseAccountId.HasValue == true && customer.CommExpenseAccountId.Value > 0)
                {
                    var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == customer.CommExpenseAccountId.Value && a.UcpId == activeUcpId);
                    if (exists) commExpenseAccountId = customer.CommExpenseAccountId.Value;
                }
                if (!commExpenseAccountId.HasValue)
                {
                    var defCommExp = await GetOrCreateAccountAsync("Commission Expense", "Commission Expense", "Expense", activeUcpId.Value);
                    commExpenseAccountId = defCommExp.AccountId;
                }

                if (customer?.CommPayableAccountId.HasValue == true && customer.CommPayableAccountId.Value > 0)
                {
                    var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == customer.CommPayableAccountId.Value && a.UcpId == activeUcpId);
                    if (exists) commPayableAccountId = customer.CommPayableAccountId.Value;
                }
                if (!commPayableAccountId.HasValue)
                {
                    var defCommPay = await GetOrCreateAccountAsync("Commission Payable", "Commission Payable", "Liability", activeUcpId.Value);
                    commPayableAccountId = defCommPay.AccountId;
                }
            }

            var maxTransNo = await _context.Transactions.MaxAsync(t => (long?)t.TransNo) ?? 0;
            var newTransNo = maxTransNo + 1;
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int glLineCount = 0;

                // CN is Sales Return: Debit Sales/GST, Credit Customer
                AccountMaster? salesAccount = null;
                if (customer?.SalesAccountId.HasValue == true)
                {
                    salesAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountId == customer.SalesAccountId.Value && a.UcpId == activeUcpId);
                }
                if (salesAccount == null)
                {
                    salesAccount = await GetOrCreateAccountAsync("Sales Revenue", "Sales", "Revenue", activeUcpId.Value);
                }

                AccountMaster? gstAccount = null;
                if (customer?.TaxAccountId.HasValue == true)
                {
                    gstAccount = await _context.AccountMasters.FirstOrDefaultAsync(a => a.AccountId == customer.TaxAccountId.Value && a.UcpId == activeUcpId);
                }
                if (gstAccount == null)
                {
                    gstAccount = await GetOrCreateAccountAsync("Output Sales Tax", "Output Tax", "Liability", activeUcpId.Value);
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
                        glLineCount++;
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
                        glLineCount++;
                    }
                }

                // Entry 3: Credit - Customer Receivable / Trade Debtors (Local)
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
                glLineCount++;

                // Commission Reversals (if applicable)
                // Entry 4: Debit - Commission Payable (Liability Decreases)
                if (commissionAmt > 0 && commPayableAccountId.HasValue)
                {
                    var debitCommPayable = new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "CN",
                        AccountId = commPayableAccountId.Value,
                        Debit = commissionAmt,
                        Credit = 0,
                        Remarks = $"Commission Payable Reversal against Credit Note CN-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    };
                    _context.Transactions.Add(debitCommPayable);
                    glLineCount++;
                }

                // Entry 5: Credit - Commission Expense (Expense Decreases)
                if (commissionAmt > 0 && commExpenseAccountId.HasValue)
                {
                    var creditCommExpense = new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "CN",
                        AccountId = commExpenseAccountId.Value,
                        Debit = 0,
                        Credit = commissionAmt,
                        Remarks = $"Commission Expense Reversal against Credit Note CN-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    };
                    _context.Transactions.Add(creditCommExpense);
                    glLineCount++;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["StatusMessage"] = $"Credit Note CN-{parentEntry.EntryNumber} posted to ledger successfully with {glLineCount} balanced GL entries.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }
            return RedirectToPage();
        }

        private async Task<AccountMaster> GetOrCreateAccountAsync(string defaultName, string searchKeyword, string preferredCategoryName, long activeUcpId)
        {
            var acc = await _context.AccountMasters
                .FirstOrDefaultAsync(a => a.AccountName != null && a.AccountName.Contains(searchKeyword) && a.UcpId == activeUcpId);
            if (acc != null) return acc;

            var category = await _context.CategoryMasters
                .Where(c => c.UcpId == activeUcpId)
                .OrderByDescending(c => c.CategoryName != null && c.CategoryName.Contains(preferredCategoryName))
                .FirstOrDefaultAsync();

            if (category == null)
            {
                category = await _context.CategoryMasters.FirstOrDefaultAsync(c => c.UcpId == activeUcpId);
                if (category == null)
                {
                    category = new CategoryMaster
                    {
                        CategoryName = preferredCategoryName,
                        UcpId = activeUcpId,
                        CreationOn = DateTime.Now
                    };
                    _context.CategoryMasters.Add(category);
                    await _context.SaveChangesAsync();
                }
            }

            acc = new AccountMaster
            {
                AccountId = 0,
                AccountName = defaultName,
                CategoryId = category.CategoryId,
                UcpId = activeUcpId,
                CreationOn = DateTime.Now
            };
            _context.AccountMasters.Add(acc);
            await _context.SaveChangesAsync();
            return acc;
        }

        public async Task<IActionResult> OnPostUnpostFromLedgerAsync(long id)
        {
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to unpost ledger entries.";
                return RedirectToPage();
            }
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "CN" || (activeUcpId.HasValue && entry.UcpId.HasValue && entry.UcpId != activeUcpId))
            {
                TempData["ErrorMessage"] = "Credit Note not found.";
                return RedirectToPage();
            }
            var transRows = await _context.Transactions
                .Where(t => t.EntryId == id)
                .ToListAsync();
            if (!transRows.Any())
            {
                TempData["ErrorMessage"] = "This Credit Note is not posted to ledger.";
                return RedirectToPage();
            }
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Transactions.RemoveRange(transRows);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["StatusMessage"] = $"CN-{entry.EntryNumber} has been unposted from the ledger. {transRows.Count} GL entries removed.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error unposting from ledger: " + ex.Message;
            }
            return RedirectToPage();
        }
                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.Entries == null) return RedirectToPage();
            var query = _context.Entries
                .Where(e => e.EntryType == "CN" && e.UcpId == activeUcpId);
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var smart = SmartSearchHelper.Parse(SearchTerm);
                query = query.Where(e =>
                    (smart.HasNumber && e.EntryNumber == smart.ExtractedNumber.Value) ||
                    e.EntryNumber.ToString().Contains(smart.CleanTerm) ||
                    (e.EntryAttribute1 != null && e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))
                );
            }
            var parents = await query.OrderByDescending(e => e.EntryId).ToListAsync();
            var customerIds = parents
                .Where(e => !string.IsNullOrEmpty(e.EntryAttribute1))
                .Select(e => long.TryParse(e.EntryAttribute1, out long id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            var customerMap = new Dictionary<long, string>();
            if (customerIds.Any() && _context.CustomerMasters != null)
            {
                customerMap = await _context.CustomerMasters
                    .Where(c => customerIds.Contains(c.CustomerId))
                    .ToDictionaryAsync(c => c.CustomerId, c => c.CustomerName ?? "Unknown");
            }
            var parentIds = parents.Select(e => (long?)e.EntryId).ToList();
            var childSummaries = await _context.Entries
                .Where(e => e.EntryType == "CNL" && parentIds.Contains(e.EntryRefId))
                .GroupBy(e => e.EntryRefId)
                .Select(g => new
                {
                    ParentId = g.Key,
                    ItemCount = g.Count(),
                    TotalCredit = g.Sum(x => (decimal?)(x.Amount ?? 0.0) ?? 0m)
                })
                .ToDictionaryAsync(x => x.ParentId ?? 0, x => x);
            var exportList = parents.Select(e =>
            {
                long.TryParse(e.EntryAttribute1, out long cId);
                string custName = customerMap.TryGetValue(cId, out var name) ? name : (e.EntryAttribute1 ?? "-");
                var summary = childSummaries.TryGetValue(e.EntryId, out var s) ? s : null;
                return new
                {
                    CnNo = $"CN-{e.EntryNumber}",
                    Date = e.EntryDate,
                    Customer = custName,
                    Items = summary?.ItemCount ?? 0,
                    Amount = summary?.TotalCredit ?? 0m,
                    Remarks = e.EntryRemarks ?? "-"
                };
            }).ToList();
            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Credit Note (CN) #", x => x.CnNo, null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.Date, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("Customer Name", x => x.Customer),
                new("Items", x => x.Items, "#,##0", XLAlignmentHorizontalValues.Center),
                new("Total Amount", x => x.Amount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Remarks", x => x.Remarks)
            };
            var bytes = ExcelExportHelper.ExportToExcel("Sale Returns (Credit Notes)", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Credit_Notes_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}