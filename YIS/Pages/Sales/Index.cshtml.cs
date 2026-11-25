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
    public class SalesModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly YIS.Services.FbrValidationService _fbrService;
        public SalesModel(ApplicationDbContext context, YIS.Services.FbrValidationService fbrService)
        {
            _context = context;
            _fbrService = fbrService;
        }
        public IList<SalesInvoiceViewModel> Invoices { get; set; } = new List<SalesInvoiceViewModel>();
        public Dictionary<long, string> CustomerNames { get; set; } = new Dictionary<long, string>();
        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 10;
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }
        [BindProperty(SupportsGet = true)]
        public string SortDir { get; set; } = "desc";
        [BindProperty(SupportsGet = true)]
        public long? SsdId { get; set; }
        public List<string> SearchSuggestions { get; set; } = new List<string>();
        public PaginationViewModel Pagination { get; set; } = default!;
        public class InvoiceLineDetail
        {
            public string ItemName { get; set; } = "";
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
        }

        public class SalesInvoiceViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string CustomerName { get; set; }
            public double Amount { get; set; } // Net / Grand Total
            public double? TaxAmount { get; set; }
            public string RefSsdNo { get; set; } // SSD-123
            public long? RefSsdId { get; set; }
            public long? SsdNumber { get; set; }
            public string? Remarks { get; set; }
            public string? CompiledNarration { get; set; }
            public List<InvoiceLineDetail> LineDetails { get; set; } = new List<InvoiceLineDetail>();
            public bool IsLedgerPosted { get; set; }
            public bool IsFbrPosted { get; set; }
            public string? ReturnNumber { get; set; }
            public long? ReturnEntryId { get; set; }
            public double ReturnAmount { get; set; }
            public double ReturnQty { get; set; }
            public bool HasReturn => ReturnAmount > 0 || ReturnQty > 0;
        }
        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            // Prefetch Customers for this company
            if (_context.CustomerMasters != null)
            {
                var custs = await _context.CustomerMasters
                   .Where(c => c.UcpId == activeUcpId)
                   .ToListAsync();
                foreach (var c in custs)
                {
                    if (!CustomerNames.ContainsKey(c.CustomerId)) CustomerNames.Add(c.CustomerId, c.CustomerName ?? "Unknown");
                }
            }
            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "SAL" && e.UcpId == activeUcpId);
                if (SsdId.HasValue)
                {
                    query = query.Where(e => e.EntryRefId == SsdId.Value);
                }
                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);
                    var matchingCustomerIds = CustomerNames
                        .Where(kv => kv.Value.ToLower().Contains(smart.CleanTerm))
                        .Select(kv => kv.Key.ToString())
                        .ToList();
                    query = query.Where(e =>
                        (smart.HasNumber && e.EntryNumber == smart.ExtractedNumber.Value) ||
                        e.EntryNumber.ToString().Contains(smart.CleanTerm) ||
                        (e.EntryAttribute1 != null && (matchingCustomerIds.Contains(e.EntryAttribute1) || e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))) ||
                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(smart.CleanTerm))
                    );
                }
                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);
                // Populate search suggestions (top recent invoice numbers and active customers)
                var recentNumbers = await _context.Entries
                    .Where(e => e.EntryType == "SAL" && e.UcpId == activeUcpId && e.EntryNumber != null)
                    .OrderByDescending(e => e.EntryId)
                    .Take(8)
                    .Select(e => $"SAL-{e.EntryNumber}")
                    .ToListAsync();
                SearchSuggestions = recentNumbers
                    .Concat(CustomerNames.Values.Take(6))
                    .Distinct()
                    .ToList();
                var orderedQuery = (SortDir == "asc")
                    ? query.OrderBy(e => e.EntryId)
                    : query.OrderByDescending(e => e.EntryId);
                var sales = await orderedQuery
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
                // Check which entries are posted
                var entryIds = sales.Select(e => e.EntryId).ToList();
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
                // Get SSD Numbers mapping
                var ssdIds = sales.Where(s => s.EntryRefId.HasValue).Select(s => s.EntryRefId!.Value).Distinct().ToList();
                var ssdNumberMap = new Dictionary<long, long>();
                if (ssdIds.Any())
                {
                    ssdNumberMap = await _context.Entries
                        .Where(e => e.EntryType == "SSD" && ssdIds.Contains(e.EntryId))
                        .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);
                }
                // Fetch Line Items with Item Names for Sales
                var salesEntryIds = sales.Select(e => (long?)e.EntryId).ToList();
                var lineItemsRaw = await (from l in _context.Entries
                                          join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                          from i in itemJoin.DefaultIfEmpty()
                                          where l.EntryType == "SALL" && salesEntryIds.Contains(l.EntryRefId)
                                          select new
                                          {
                                              ParentId = l.EntryRefId ?? 0,
                                              ItemName = i != null ? i.ItemName : "Unknown",
                                              l.Qty,
                                              l.Lbs,
                                              l.Rate,
                                              l.Amount
                                          }).ToListAsync();

                var lineItemsMap = lineItemsRaw
                    .GroupBy(x => x.ParentId)
                    .ToDictionary(g => g.Key, g => g.Select(x => new InvoiceLineDetail
                    {
                        ItemName = x.ItemName,
                        Qty = x.Qty,
                        Lbs = x.Lbs,
                        Rate = x.Rate,
                        Amount = x.Amount
                    }).ToList());

                // Fetch CN Returns linked to these sales invoices
                var salesEntryNumbers = sales.Where(s => s.EntryNumber.HasValue).Select(s => s.EntryNumber!.Value).ToList();
                var cnReturns = await (from cn in _context.Entries
                                       where cn.EntryType == "CN" && cn.UcpId == activeUcpId &&
                                             ((cn.EntryRefId.HasValue && entryIds.Contains(cn.EntryRefId.Value)) ||
                                              (cn.EntryRefNo.HasValue && salesEntryNumbers.Contains(cn.EntryRefNo.Value)))
                                       select new
                                       {
                                           CnId = cn.EntryId,
                                           CnNumber = cn.EntryNumber,
                                           cn.EntryRefId,
                                           cn.EntryRefNo,
                                           cn.Amount,
                                           cn.Qty
                                       }).ToListAsync();

                var cnBySalId = new Dictionary<long, (string ReturnNumber, long ReturnId, double Amount, double Qty)>();
                foreach (var cn in cnReturns)
                {
                    long? salId = null;
                    if (cn.EntryRefId.HasValue && entryIds.Contains(cn.EntryRefId.Value))
                    {
                        salId = cn.EntryRefId.Value;
                    }
                    else if (cn.EntryRefNo.HasValue)
                    {
                        var match = sales.FirstOrDefault(s => s.EntryNumber == cn.EntryRefNo.Value);
                        if (match != null) salId = match.EntryId;
                    }

                    if (salId.HasValue)
                    {
                        string rNum = $"CN-{cn.CnNumber ?? cn.CnId}";
                        var cur = cnBySalId.TryGetValue(salId.Value, out var c) ? c : (rNum, cn.CnId, 0.0, 0.0);
                        cnBySalId[salId.Value] = (cur.Item1, cur.Item2, cur.Item3 + (cn.Amount ?? 0), cur.Item4 + (cn.Qty ?? 0));
                    }
                }

                Invoices = sales.Select(s =>
                {
                    string cName = "Unknown";
                    if (long.TryParse(s.EntryAttribute1, out long cid) && CustomerNames.ContainsKey(cid))
                        cName = CustomerNames[cid];
                    long? ssdNum = s.EntryRefId.HasValue && ssdNumberMap.ContainsKey(s.EntryRefId.Value) ? ssdNumberMap[s.EntryRefId.Value] : s.EntryRefId;
                    string ssdRef = "-";
                    if (s.EntryRefId.HasValue) 
                    {
                        var num = ssdNumberMap.ContainsKey(s.EntryRefId.Value) ? ssdNumberMap[s.EntryRefId.Value].ToString() : s.EntryRefId.Value.ToString();
                        ssdRef = "SSD Ref: " + num;
                    }

                    var lines = lineItemsMap.TryGetValue(s.EntryId, out var lList) ? lList : new List<InvoiceLineDetail>();
                    var itemParts = new List<string>();
                    foreach (var itm in lines)
                    {
                        var sub = new List<string>();
                        if (itm.Qty.HasValue && itm.Qty > 0) sub.Add($"{itm.Qty:N0} Bags");
                        if (itm.Lbs.HasValue && itm.Lbs > 0) sub.Add($"{itm.Lbs:N2} Lbs");
                        if (itm.Rate.HasValue && itm.Rate > 0) sub.Add($"@{itm.Rate:N2}");
                        string p = itm.ItemName;
                        if (sub.Any()) p += $" ({string.Join(", ", sub)})";
                        itemParts.Add(p);
                    }
                    string narration = string.Join("; ", itemParts);
                    if (ssdNum.HasValue && ssdNum.Value > 0)
                    {
                        narration = string.IsNullOrEmpty(narration) ? $"Soda: SSD-{ssdNum}" : $"{narration} | Soda: SSD-{ssdNum}";
                    }
                    if (!string.IsNullOrWhiteSpace(s.EntryRemarks) && s.EntryRemarks != "Sales Invoice")
                    {
                        narration = string.IsNullOrEmpty(narration) ? s.EntryRemarks : $"{narration} - {s.EntryRemarks}";
                    }

                    cnBySalId.TryGetValue(s.EntryId, out var cnInfo);

                    return new SalesInvoiceViewModel
                    {
                        EntryId = s.EntryId,
                        EntryNumber = s.EntryNumber,
                        EntryDate = s.EntryDate,
                        CustomerName = cName,
                        Amount = s.Amount ?? 0,
                        TaxAmount = s.TaxAmount,
                        RefSsdNo = ssdRef,
                        RefSsdId = s.EntryRefId,
                        SsdNumber = ssdNum,
                        Remarks = s.EntryRemarks,
                        CompiledNarration = narration,
                        LineDetails = lines,
                        IsLedgerPosted = postedEntryIds.Contains(s.EntryId),
                        IsFbrPosted = s.EntryPostedToFbr == true,
                        ReturnNumber = cnInfo.Item1,
                        ReturnEntryId = cnInfo.Item2 > 0 ? cnInfo.Item2 : null,
                        ReturnAmount = cnInfo.Item3,
                        ReturnQty = cnInfo.Item4
                    };
                }).ToList();
            }
        }
        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanDelete(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete Sales entries.";
                return RedirectToPage();
            }
            if (_context.Entries == null) return NotFound();
            // Check if posted
            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))
            {
                TempData["ErrorMessage"] = "Cannot delete: This sales entry has already been posted to the ledger.";
                return RedirectToPage();
            }
            // Delete Child Items
            var children = await _context.Entries.Where(e => e.EntryRefId == id && e.EntryType == "SALL").ToListAsync();
            _context.Entries.RemoveRange(children);
            var entry = await _context.Entries.FindAsync(id);
            if (entry != null) _context.Entries.Remove(entry);
            await _context.SaveChangesAsync();
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostPostToLedgerAsync(long id)
        {
            // 1. Check if already posted
            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))
            {
                TempData["ErrorMessage"] = "This sales entry is already posted.";
                return RedirectToPage();
            }
            // 2. Get the parent entry
            var parentEntry = await _context.Entries.FindAsync(id);
            if (parentEntry == null || parentEntry.EntryType != "SAL")
            {
                TempData["ErrorMessage"] = "Sales entry not found.";
                return RedirectToPage();
            }
            // 3. Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }
            // 4. Get Customer and GL Accounts from CustomerMaster
            long? customerAccountId = null;
            string customerName = "Customer";
            CustomerMaster? customer = null;
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
            // Verify account exists in active company
            if (customerAccountId.HasValue && customerAccountId.Value > 0)
            {
                var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == customerAccountId.Value && a.UcpId == activeUcpId);
                if (!exists)
                {
                    customerAccountId = null;
                }
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
            // Fallback 2: Search for any existing Local Debtors / Trade Debtors account in active company
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
            // 5. Get Child Line Items (SALL) to compute Yarn Sales Price vs Tax Amounts
            var lineItems = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "SALL")
                .ToListAsync();
            double yarnExclusiveAmount = lineItems.Any()
                ? lineItems.Sum(l => l.Amount ?? 0)
                : (parentEntry.Amount ?? 0);
            double totalSalesTax = lineItems.Any()
                ? lineItems.Sum(l => l.TaxAmount ?? 0)
                : (parentEntry.TaxAmount ?? 0);
            // Grand Total (Customer Receivable)
            double grandTotal = yarnExclusiveAmount + totalSalesTax;
            if (grandTotal <= 0 && parentEntry.Amount.HasValue && parentEntry.Amount.Value > 0)
            {
                grandTotal = parentEntry.Amount.Value;
                if (yarnExclusiveAmount <= 0) yarnExclusiveAmount = grandTotal - totalSalesTax;
            }
            if (grandTotal <= 0)
            {
                TempData["ErrorMessage"] = "Cannot post: Grand total is zero or invalid.";
                return RedirectToPage();
            }
            // Commission details
            double commissionAmt = parentEntry.EntryCommAmt ?? 0;
            string? agentName = parentEntry.EntryAttribute4?.Trim();
            // TransNo from SAL EntryNumber
            var newTransNo = parentEntry.EntryNumber ?? 0;
            if (newTransNo == 0)
            {
                TempData["ErrorMessage"] = "Cannot post: Sales Entry Number is missing or zero.";
                return RedirectToPage();
            }
            // 6. Get GL accounts (Configured on Customer with Fallback to Standard Defaults)
            // A. Sales Account
            long salesAccountId = 0;
            if (customer?.SalesAccountId.HasValue == true && customer.SalesAccountId.Value > 0)
            {
                var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == customer.SalesAccountId.Value && a.UcpId == activeUcpId);
                if (exists) salesAccountId = customer.SalesAccountId.Value;
            }
            if (salesAccountId == 0)
            {
                var defSales = await GetOrCreateAccountAsync("Sales Revenue", "Sales", "Revenue", activeUcpId.Value);
                salesAccountId = defSales.AccountId;
            }
            // B. Tax Account
            long? outputTaxAccountId = null;
            if (totalSalesTax > 0)
            {
                if (customer?.TaxAccountId.HasValue == true && customer.TaxAccountId.Value > 0)
                {
                    var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == customer.TaxAccountId.Value && a.UcpId == activeUcpId);
                    if (exists) outputTaxAccountId = customer.TaxAccountId.Value;
                }
                if (!outputTaxAccountId.HasValue)
                {
                    var defTax = await GetOrCreateAccountAsync("Output Sales Tax", "Output Tax", "Liability", activeUcpId.Value);
                    outputTaxAccountId = defTax.AccountId;
                }
            }
            // C. Commission Expense Account
            long? commExpenseAccountId = null;
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
            }
            // D. Commission Payable Account
            long? commPayableAccountId = null;
            if (commissionAmt > 0)
            {
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
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int glLineCount = 0;
                // Entry 1: Debit - Customer Receivable / Trade Debtors (Local)
                _context.Transactions.Add(new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "SAL",
                    AccountId = customerAccountId.Value,
                    Debit = grandTotal,
                    Credit = 0,
                    Remarks = $"Customer Receivable against Sales Invoice # SAL-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                });
                glLineCount++;
                // Entry 2: Credit - Sales Revenue (Yarn Sales Price)
                if (yarnExclusiveAmount > 0)
                {
                    _context.Transactions.Add(new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "SAL",
                        AccountId = salesAccountId,
                        Debit = 0,
                        Credit = yarnExclusiveAmount,
                        Remarks = $"Sales against Sales Invoice # SAL-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    });
                    glLineCount++;
                }
                // Entry 3: Credit - Output Sales Tax
                if (totalSalesTax > 0 && outputTaxAccountId.HasValue)
                {
                    _context.Transactions.Add(new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "SAL",
                        AccountId = outputTaxAccountId.Value,
                        Debit = 0,
                        Credit = totalSalesTax,
                        Remarks = $"Output Sales Tax against Sales Invoice # SAL-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    });
                    glLineCount++;
                }
                // Entry 4: Debit - Commission Expense (if applicable)
                if (commissionAmt > 0 && commExpenseAccountId.HasValue)
                {
                    _context.Transactions.Add(new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "SAL",
                        AccountId = commExpenseAccountId.Value,
                        Debit = commissionAmt,
                        Credit = 0,
                        Remarks = $"Commission Expense against Sales Invoice # SAL-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    });
                    glLineCount++;
                }
                // Entry 5: Credit - Commission Payable to Agent (if applicable)
                if (commissionAmt > 0 && commPayableAccountId.HasValue)
                {
                    _context.Transactions.Add(new Trans
                    {
                        TransId = 0,
                        TransNo = newTransNo,
                        TransType = "SAL",
                        AccountId = commPayableAccountId.Value,
                        Debit = 0,
                        Credit = commissionAmt,
                        Remarks = $"Commission Payable to {(!string.IsNullOrWhiteSpace(agentName) ? agentName : "Agent")} against Sales Invoice # SAL-{parentEntry.EntryNumber}",
                        EntryId = id,
                        UcpId = activeUcpId
                    });
                    glLineCount++;
                }
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["StatusMessage"] = $"Sales Invoice SAL-{parentEntry.EntryNumber} posted to ledger successfully with {glLineCount} balanced GL entries.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error posting to ledger: " + ex.Message;
            }
            return RedirectToPage();
        }
        private Task<AccountMaster> GetOrCreateAccountAsync(string defaultName, string searchKeyword, string preferredCategoryName, long activeUcpId)
        {
            return AccountHelper.GetOrCreateSystemAccountAsync(_context, defaultName, preferredCategoryName, activeUcpId, searchKeyword);
        }
        public async Task<IActionResult> OnPostUnpostFromLedgerAsync(long id)
        {
            // 1. Permission check
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to unpost ledger entries.";
                return RedirectToPage();
            }
            // 2. Find the parent SAL entry
            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "SAL")
            {
                TempData["ErrorMessage"] = "Sales entry not found.";
                return RedirectToPage();
            }
            // 3. Block if already reported to FBR (legal requirement)
            if (entry.EntryPostedToFbr == true)
            {
                TempData["ErrorMessage"] = "Cannot unpost: SAL-" + entry.EntryNumber + " has already been reported to FBR. FBR-posted invoices cannot be unposted.";
                return RedirectToPage();
            }
            // 4. Get the 2 TRANS rows (Debit + Credit) linked to this entry
            var transRows = await _context.Transactions
                .Where(t => t.EntryId == id)
                .ToListAsync();
            if (!transRows.Any())
            {
                TempData["ErrorMessage"] = "This entry is not posted to ledger.";
                return RedirectToPage();
            }
            // 5. Delete both TRANS rows inside a DB transaction
            using var dbTxn = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Transactions.RemoveRange(transRows);
                await _context.SaveChangesAsync();
                await dbTxn.CommitAsync();
                TempData["StatusMessage"] = $"SAL-{entry.EntryNumber} has been unposted from the ledger. {transRows.Count} GL entries removed.";
            }
            catch (Exception ex)
            {
                await dbTxn.RollbackAsync();
                TempData["ErrorMessage"] = "Error unposting from ledger: " + ex.Message;
            }
            return RedirectToPage();
        }
        public async Task<IActionResult> OnPostPostToFbrAsync(long id)
        {
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to post to FBR.";
                return RedirectToPage();
            }
            var success = await _fbrService.ValidateSingleInvoiceAsync(id);
            if (success)
            {
                TempData["StatusMessage"] = "Invoice posted to FBR successfully!";
            }
            else
            {
                // Reload entity to ensure we get the latest error message from DB
                var entry = await _context.Entries.FirstOrDefaultAsync(e => e.EntryId == id);
                if (entry != null)
                {
                    await _context.Entry(entry).ReloadAsync();
                    TempData["ErrorMessage"] = "FBR Posting failed: " + (entry.EntryFbrError ?? "Unknown error");
                }
                else
                {
                    TempData["ErrorMessage"] = "FBR Posting failed: Invoice not found.";
                }
            }
            return RedirectToPage();
        }
        public async Task<JsonResult> OnGetPrintDataAsync(long id)
        {
            if (_context.Entries == null) return new JsonResult(new { success = false, message = "Database error" });
            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "SAL") return new JsonResult(new { success = false, message = "Invoice not found" });
            // Fetch Customer
            CustomerMaster? customer = null;
            if (long.TryParse(entry.EntryAttribute1, out long cid))
            {
                customer = await _context.CustomerMasters.FirstOrDefaultAsync(c => c.CustomerId == cid);
            }
            // Fetch Line Items with Item details
            var items = await (from s in _context.Entries
                               join i in _context.ItemMasters on s.ItemId equals i.ItemId into itemJoin
                               from item in itemJoin.DefaultIfEmpty()
                               where s.EntryRefId == id && s.EntryType == "SALL"
                               select new
                               {
                                   itemId = s.ItemId,
                                   itemName = item != null ? item.ItemName : "Unknown",
                                   hsCode = item != null ? item.ItemHsCode : "-",
                                   qty = s.Qty ?? 0,
                                   lbs = s.Lbs,
                                   rate = s.Rate ?? 0,
                                   amount = s.Amount ?? 0,
                                   gstPer = s.EntryAttribute1,
                                   gstAmt = s.TaxAmount ?? 0,
                                   totalAmount = (s.Amount ?? 0) + (s.TaxAmount ?? 0)
                               }).ToListAsync();
            return new JsonResult(new
            {
                success = true,
                header = new
                {
                    entryNumber = entry.EntryNumber,
                    entryDate = entry.EntryDate?.ToString("yyyy-MM-dd"),
                    remarks = entry.EntryRemarks ?? "",
                    totalQty = entry.Qty ?? 0,
                    totalTax = entry.TaxAmount ?? 0,
                    grandTotal = entry.Amount ?? 0
                },
                customer = customer == null ? null : new
                {
                    name = customer.CustomerName,
                    ntn = customer.CustomerNtnNo,
                    str = customer.CustomerStrNo,
                    address = customer.CustomerAddress,
                    province = "SINDH" // Defaulting to Sindh as per earlier requirement
                },
                items = items
            });
        }
                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.Entries == null) return RedirectToPage();
            var customerNames = new Dictionary<long, string>();
            if (_context.CustomerMasters != null)
            {
                customerNames = await _context.CustomerMasters
                   .Where(c => c.UcpId == activeUcpId)
                   .ToDictionaryAsync(c => c.CustomerId, c => c.CustomerName ?? "Unknown");
            }
            var query = _context.Entries
                .Where(e => e.EntryType == "SAL" && e.UcpId == activeUcpId);
            if (SsdId.HasValue)
            {
                query = query.Where(e => e.EntryRefId == SsdId.Value);
            }
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var smart = SmartSearchHelper.Parse(SearchTerm);
                var matchingCustomerIds = customerNames
                    .Where(kv => kv.Value.ToLower().Contains(smart.CleanTerm))
                    .Select(kv => kv.Key.ToString())
                    .ToList();
                query = query.Where(e =>
                    (smart.HasNumber && e.EntryNumber == smart.ExtractedNumber.Value) ||
                    e.EntryNumber.ToString().Contains(smart.CleanTerm) ||
                    (e.EntryAttribute1 != null && (matchingCustomerIds.Contains(e.EntryAttribute1) || e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))) ||
                    (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(smart.CleanTerm))
                );
            }
            var parents = await query.OrderByDescending(e => e.EntryId).ToListAsync();
            var parentIds = parents.Select(e => (long?)e.EntryId).ToList();
            var childSummaries = await _context.Entries
                .Where(e => e.EntryType == "SALL" && parentIds.Contains(e.EntryRefId))
                .GroupBy(e => e.EntryRefId)
                .Select(g => new
                {
                    ParentId = g.Key,
                    TotalAmount = g.Sum(x => (decimal?)(x.Amount ?? 0.0) ?? 0m)
                })
                .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.TotalAmount);
            // Get SSD Numbers mapping for Excel
            var exportSsdIds = parents.Where(s => s.EntryRefId.HasValue).Select(s => s.EntryRefId!.Value).Distinct().ToList();
            var exportSsdNumberMap = new Dictionary<long, long>();
            if (exportSsdIds.Any())
            {
                exportSsdNumberMap = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && exportSsdIds.Contains(e.EntryId))
                    .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);
            }

            var exportLineItemsRaw = await (from l in _context.Entries
                                            join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                            from i in itemJoin.DefaultIfEmpty()
                                            where l.EntryType == "SALL" && parentIds.Contains(l.EntryRefId)
                                            select new
                                            {
                                                ParentId = l.EntryRefId ?? 0,
                                                ItemName = i != null ? i.ItemName : "Unknown",
                                                l.Qty,
                                                l.Lbs,
                                                l.Rate,
                                                l.Amount
                                            }).ToListAsync();

            var exportLineItemsMap = exportLineItemsRaw
                .GroupBy(x => x.ParentId)
                .ToDictionary(g => g.Key, g => g.Select(x => new InvoiceLineDetail
                {
                    ItemName = x.ItemName,
                    Qty = x.Qty,
                    Lbs = x.Lbs,
                    Rate = x.Rate,
                    Amount = x.Amount
                }).ToList());

            // CN returns for Excel
            var exportSalNumbers = parents.Where(p => p.EntryNumber.HasValue).Select(p => p.EntryNumber!.Value).ToList();
            var exportSalIds = parents.Select(p => p.EntryId).ToList();
            var exportCnReturns = await (from cn in _context.Entries
                                         where cn.EntryType == "CN" && cn.UcpId == activeUcpId &&
                                               ((cn.EntryRefId.HasValue && exportSalIds.Contains(cn.EntryRefId.Value)) ||
                                                (cn.EntryRefNo.HasValue && exportSalNumbers.Contains(cn.EntryRefNo.Value)))
                                         select new
                                         {
                                             CnNumber = cn.EntryNumber,
                                             cn.EntryRefId,
                                             cn.EntryRefNo,
                                             Amount = cn.Amount ?? 0
                                         }).ToListAsync();

            var exportCnMap = new Dictionary<long, (string Ref, double Amount)>();
            foreach (var cn in exportCnReturns)
            {
                long? salId = null;
                if (cn.EntryRefId.HasValue && exportSalIds.Contains(cn.EntryRefId.Value))
                {
                    salId = cn.EntryRefId.Value;
                }
                else if (cn.EntryRefNo.HasValue)
                {
                    var match = parents.FirstOrDefault(s => s.EntryNumber == cn.EntryRefNo.Value);
                    if (match != null) salId = match.EntryId;
                }

                if (salId.HasValue)
                {
                    string refNo = $"CN-{cn.CnNumber}";
                    var cur = exportCnMap.TryGetValue(salId.Value, out var c) ? c : (refNo, 0.0);
                    exportCnMap[salId.Value] = (cur.Item1, cur.Item2 + cn.Amount);
                }
            }

            var exportList = parents.Select(e =>
            {
                long.TryParse(e.EntryAttribute1, out long cId);
                string custName = customerNames.TryGetValue(cId, out var name) ? name : (e.EntryAttribute1 ?? "-");
                decimal amt = childSummaries.TryGetValue(e.EntryId, out var a) ? a : 0m;

                var lines = exportLineItemsMap.TryGetValue(e.EntryId, out var lList) ? lList : new List<InvoiceLineDetail>();
                var itemParts = new List<string>();
                foreach (var itm in lines)
                {
                    var sub = new List<string>();
                    if (itm.Qty.HasValue && itm.Qty > 0) sub.Add($"{itm.Qty:N0} Bags");
                    if (itm.Lbs.HasValue && itm.Lbs > 0) sub.Add($"{itm.Lbs:N2} Lbs");
                    if (itm.Rate.HasValue && itm.Rate > 0) sub.Add($"@{itm.Rate:N2}");
                    string p = itm.ItemName;
                    if (sub.Any()) p += $" ({string.Join(", ", sub)})";
                    itemParts.Add(p);
                }
                string narration = string.Join("; ", itemParts);
                if (e.EntryRefId.HasValue)
                {
                    long ssdNo = exportSsdNumberMap.TryGetValue(e.EntryRefId.Value, out var sn) ? sn : e.EntryRefId.Value;
                    narration = string.IsNullOrEmpty(narration) ? $"Soda: SSD-{ssdNo}" : $"{narration} | Soda: SSD-{ssdNo}";
                }
                if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Sales Invoice")
                {
                    narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                }

                long ssdDisplayNo = e.EntryRefId.HasValue ? (exportSsdNumberMap.TryGetValue(e.EntryRefId.Value, out var sn2) ? sn2 : e.EntryRefId.Value) : 0;
                exportCnMap.TryGetValue(e.EntryId, out var cnExport);

                return new
                {
                    InvoiceNo = $"SAL-{e.EntryNumber}",
                    Date = e.EntryDate,
                    SsdNo = e.EntryRefId.HasValue ? $"SSD-{ssdDisplayNo}" : "-",
                    Customer = custName,
                    Narration = narration,
                    Broker = e.EntryAttribute2 ?? "-",
                    ReturnInfo = cnExport.Item2 > 0 ? $"{cnExport.Item1} ({cnExport.Item2:N2})" : "-",
                    Amount = amt,
                    Remarks = e.EntryRemarks ?? "-"
                };
            }).ToList();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Invoice #", x => x.InvoiceNo, null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.Date, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("SSD #", x => x.SsdNo, null, XLAlignmentHorizontalValues.Center),
                new("Customer Name", x => x.Customer),
                new("Narration / Details", x => x.Narration),
                new("Broker", x => x.Broker),
                new("Return (CN)", x => x.ReturnInfo, null, XLAlignmentHorizontalValues.Center),
                new("Total Amount", x => x.Amount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Remarks", x => x.Remarks)
            };
            var bytes = ExcelExportHelper.ExportToExcel("Sales Invoices", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Sales_Invoices_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
