using YIS.Services;

using ClosedXML.Excel;

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



        public int PageSize { get; set; } = 10;







        [BindProperty(SupportsGet = true)]



        public string? SearchTerm { get; set; }







        [BindProperty(SupportsGet = true)]



        public string SortDir { get; set; } = "desc";







        [BindProperty(SupportsGet = true)]



        public long? PsdId { get; set; }







        public List<string> SearchSuggestions { get; set; } = new List<string>();







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
            public string? Remarks { get; set; }
            public string CompiledNarration { get; set; } = "";
            public List<InvoiceLineDetail> LineDetails { get; set; } = new();
        }

        public class InvoiceLineDetail
        {
            public string ItemName { get; set; } = "Item";
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
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



                    var smart = SmartSearchHelper.Parse(SearchTerm);



                    var matchingSupplierIds = SupplierNames



                        .Where(kv => kv.Value.ToLower().Contains(smart.CleanTerm))



                        .Select(kv => kv.Key.ToString())



                        .ToList();







                    query = query.Where(e =>



                        (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||



                        e.EntryNumber.ToString().Contains(smart.CleanTerm) ||



                        (e.EntryAttribute1 != null && (matchingSupplierIds.Contains(e.EntryAttribute1) || e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))) ||



                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(smart.CleanTerm)) ||



                        (e.EntryRefNo != null && e.EntryRefNo.ToString().Contains(smart.CleanTerm))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                // Populate search suggestions (recent PUR numbers and supplier names)



                var recentNumbers = await _context.Entries



                    .Where(e => e.EntryType == "PUR" && e.UcpId == activeUcpId && e.EntryNumber != null)



                    .OrderByDescending(e => e.EntryId)



                    .Take(8)



                    .Select(e => $"PUR-{e.EntryNumber}")



                    .ToListAsync();



                SearchSuggestions = recentNumbers



                    .Concat(SupplierNames.Values.Take(6))



                    .Distinct()



                    .ToList();







                var orderedQuery = (SortDir == "asc")



                    ? query.OrderBy(e => e.EntryId)



                    : query.OrderByDescending(e => e.EntryId);







                // Get Parent Entries



                var parents = await orderedQuery



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



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



                



                                // Fetch PURL line items with item names for narration
                var parentEntryIds = parents.Select(e => (long?)e.EntryId).ToList();
                var lineItemsRaw = await (from l in _context.Entries
                                          join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                          from i in itemJoin.DefaultIfEmpty()
                                          where l.EntryType == "PURL" && parentEntryIds.Contains(l.EntryRefId)
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

                Entries = parents.Select(e =>



                {



                    string supName = "Unknown";



                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long sId))



                    {



                        if (SupplierNames.ContainsKey(sId)) supName = SupplierNames[sId];



                    }







                    double.TryParse(e.EntryAttribute3, out double netAmt);







                    var lines = lineItemsMap.TryGetValue(e.EntryId, out var lList) ? lList : new List<InvoiceLineDetail>();
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
                    if (e.EntryRefNo.HasValue)
                    {
                        long psdNo = psdNumberMap.TryGetValue(e.EntryRefNo.Value, out var pNo) ? pNo : e.EntryRefNo.Value;
                        narration = string.IsNullOrEmpty(narration) ? $"Soda: PSD-{psdNo}" : $"{narration} | Soda: PSD-{psdNo}";
                    }
                    if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Purchase Invoice")
                    {
                        narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                    }

                    return new PurchaseViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        SupplierName = supName,
                        GrandTotalNet = netAmt,
                        ItemCount = childCounts.ContainsKey(e.EntryId) ? childCounts[e.EntryId] : (e.ItemId.HasValue ? 1 : 0),
                        EntryRefNo = e.EntryRefNo,
                        PsdNumber = (e.EntryRefNo.HasValue && psdNumberMap.ContainsKey(e.EntryRefNo.Value)) ? psdNumberMap[e.EntryRefNo.Value] : null,
                        IsPosted = postedEntryIds.Contains(e.EntryId),
                        Remarks = e.EntryRemarks,
                        CompiledNarration = narration,
                        LineDetails = lines
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



            // 1. Check if already posted



            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))



            {



                TempData["ErrorMessage"] = "This purchase entry is already posted.";



                return RedirectToPage();



            }







            // 2. Get the parent entry



            var parentEntry = await _context.Entries.FindAsync(id);



            if (parentEntry == null || parentEntry.EntryType != "PUR")



            {



                TempData["ErrorMessage"] = "Purchase entry not found.";



                return RedirectToPage();



            }







            // 3. Get Active Company Profile from Session



            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);



            if (!activeUcpId.HasValue)



            {



                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";



                return RedirectToPage("/SelectCompany");



            }







            // 4. Get Supplier and GL Accounts from SupplierMaster



            long? supplierAccountId = null;



            string supplierName = "Supplier";



            SupplierMaster? supplier = null;







            if (long.TryParse(parentEntry.EntryAttribute1, out long supplierId))



            {



                supplier = await _context.SupplierMasters.FindAsync(supplierId);



                if (supplier != null)



                {



                    supplierName = supplier.SupplierName ?? "Supplier";



                    if (supplier.AccountId.HasValue && supplier.AccountId.Value > 0)



                    {



                        supplierAccountId = supplier.AccountId.Value;



                    }



                }



            }







            // Verify supplier account exists in active company



            if (supplierAccountId.HasValue && supplierAccountId.Value > 0)



            {



                var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == supplierAccountId.Value && a.UcpId == activeUcpId);



                if (!exists)



                {



                    supplierAccountId = null;



                }



            }







            // Fallback 1: Match by Supplier Name in active company



            if (supplierAccountId == null || supplierAccountId <= 0)



            {



                var matchedSupplierAcc = await _context.AccountMasters



                    .FirstOrDefaultAsync(a => a.AccountName == supplierName && a.UcpId == activeUcpId);







                if (matchedSupplierAcc != null)



                {



                    supplierAccountId = matchedSupplierAcc.AccountId;



                }



            }







            // Fallback 2: Search for any existing Local Creditors / Trade Creditors account in active company



            if (supplierAccountId == null || supplierAccountId <= 0)



            {



                var tradeCreditorAcc = await _context.AccountMasters



                    .Where(a => a.UcpId == activeUcpId && 



                        (a.AccountName!.Contains("LOCAL CREDITOR") || 



                         a.AccountName!.Contains("TRADE CREDITOR") || 



                         a.AccountName!.Contains("TRADE CREATOR") || 



                         a.AccountName!.Contains("CREDITOR")))



                    .OrderBy(a => a.AccountId)



                    .FirstOrDefaultAsync();







                if (tradeCreditorAcc != null)



                {



                    supplierAccountId = tradeCreditorAcc.AccountId;



                }



                else



                {



                    var newAcc = await GetOrCreateAccountAsync("Trade Creditors (Local)", "Creditor", "Liability", activeUcpId.Value);



                    supplierAccountId = newAcc.AccountId;



                }



            }







            // 5. Get Child Line Items (PURL) to compute Yarn Purchase Price vs Tax Amounts



            var lineItems = await _context.Entries



                .Where(e => e.EntryRefId == id && e.EntryType == "PURL")



                .ToListAsync();







            double yarnExclusiveAmount = lineItems.Any()



                ? lineItems.Sum(l => l.Amount ?? 0)



                : (parentEntry.Amount ?? 0);







            double totalSalesTax = lineItems.Any()



                ? lineItems.Sum(l => l.TaxAmount ?? 0)



                : (parentEntry.TaxAmount ?? 0);







            // Grand Total (Supplier Payable)



            double grandTotal = yarnExclusiveAmount + totalSalesTax;



            if (grandTotal <= 0 && double.TryParse(parentEntry.EntryAttribute3, out double parsedGrandTotal))



            {



                grandTotal = parsedGrandTotal;



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







            // TransNo from PUR EntryNumber



            var newTransNo = parentEntry.EntryNumber ?? 0;



            if (newTransNo == 0)



            {



                TempData["ErrorMessage"] = "Cannot post: Purchase Entry Number is missing or zero.";



                return RedirectToPage();



            }







            // 6. Get GL accounts (Configured on Supplier with Fallback to Standard Defaults)



            // A. Purchase Account



            long purchaseAccountId = 0;



            if (supplier?.PurchaseAccountId.HasValue == true && supplier.PurchaseAccountId.Value > 0)



            {



                var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == supplier.PurchaseAccountId.Value && a.UcpId == activeUcpId);



                if (exists) purchaseAccountId = supplier.PurchaseAccountId.Value;



            }



            if (purchaseAccountId == 0)



            {



                var defPurchase = await GetOrCreateAccountAsync("Purchase / Inventory", "Purchase", "Expense", activeUcpId.Value);



                purchaseAccountId = defPurchase.AccountId;



            }







            // B. Tax Account



            long? inputTaxAccountId = null;



            if (totalSalesTax > 0)



            {



                if (supplier?.TaxAccountId.HasValue == true && supplier.TaxAccountId.Value > 0)



                {



                    var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == supplier.TaxAccountId.Value && a.UcpId == activeUcpId);



                    if (exists) inputTaxAccountId = supplier.TaxAccountId.Value;



                }



                if (!inputTaxAccountId.HasValue)



                {



                    var defTax = await GetOrCreateAccountAsync("Input Sales Tax", "Tax", "Asset", activeUcpId.Value);



                    inputTaxAccountId = defTax.AccountId;



                }



            }







            // C. Commission Expense Account



            long? commExpenseAccountId = null;



            if (commissionAmt > 0)



            {



                if (supplier?.CommExpenseAccountId.HasValue == true && supplier.CommExpenseAccountId.Value > 0)



                {



                    var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == supplier.CommExpenseAccountId.Value && a.UcpId == activeUcpId);



                    if (exists) commExpenseAccountId = supplier.CommExpenseAccountId.Value;



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



                if (supplier?.CommPayableAccountId.HasValue == true && supplier.CommPayableAccountId.Value > 0)



                {



                    var exists = await _context.AccountMasters.AnyAsync(a => a.AccountId == supplier.CommPayableAccountId.Value && a.UcpId == activeUcpId);



                    if (exists) commPayableAccountId = supplier.CommPayableAccountId.Value;



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







                // Entry 1: Debit - Purchase / Inventory Account (Yarn Exclusive Price)



                if (yarnExclusiveAmount > 0)



                {



                    _context.Transactions.Add(new Trans



                    {



                        TransId = 0,



                        TransNo = newTransNo,



                        TransType = "PUR",



                        AccountId = purchaseAccountId,



                        Debit = yarnExclusiveAmount,



                        Credit = 0,



                        Remarks = $"Inventory Received against Purchase Invoice PUR-{parentEntry.EntryNumber}",



                        EntryId = id,



                        UcpId = activeUcpId



                    });



                    glLineCount++;



                }







                // Entry 2: Debit - Input Sales Tax Account



                if (totalSalesTax > 0 && inputTaxAccountId.HasValue)



                {



                    _context.Transactions.Add(new Trans



                    {



                        TransId = 0,



                        TransNo = newTransNo,



                        TransType = "PUR",



                        AccountId = inputTaxAccountId.Value,



                        Debit = totalSalesTax,



                        Credit = 0,



                        Remarks = $"Input Sales Tax against Purchase Invoice PUR-{parentEntry.EntryNumber}",



                        EntryId = id,



                        UcpId = activeUcpId



                    });



                    glLineCount++;



                }







                // Entry 3: Credit - Supplier Payable / Trade Creditors Account



                _context.Transactions.Add(new Trans



                {



                    TransId = 0,



                    TransNo = newTransNo,



                    TransType = "PUR",



                    AccountId = supplierAccountId.Value,



                    Debit = 0,



                    Credit = grandTotal,



                    Remarks = $"Supplier Payable ({supplierName}) against Purchase Invoice PUR-{parentEntry.EntryNumber}",



                    EntryId = id,



                    UcpId = activeUcpId



                });



                glLineCount++;







                // Entry 4: Debit - Commission Expense Account (if applicable)



                if (commissionAmt > 0 && commExpenseAccountId.HasValue)



                {



                    _context.Transactions.Add(new Trans



                    {



                        TransId = 0,



                        TransNo = newTransNo,



                        TransType = "PUR",



                        AccountId = commExpenseAccountId.Value,



                        Debit = commissionAmt,



                        Credit = 0,



                        Remarks = $"Commission Expense against Purchase Invoice PUR-{parentEntry.EntryNumber}",



                        EntryId = id,



                        UcpId = activeUcpId



                    });



                    glLineCount++;



                }







                // Entry 5: Credit - Commission Payable to Agent Account (if applicable)



                if (commissionAmt > 0 && commPayableAccountId.HasValue)



                {



                    _context.Transactions.Add(new Trans



                    {



                        TransId = 0,



                        TransNo = newTransNo,



                        TransType = "PUR",



                        AccountId = commPayableAccountId.Value,



                        Debit = 0,



                        Credit = commissionAmt,



                        Remarks = $"Commission Payable to {(!string.IsNullOrWhiteSpace(agentName) ? agentName : "Agent")} against Purchase Invoice PUR-{parentEntry.EntryNumber}",



                        EntryId = id,



                        UcpId = activeUcpId



                    });



                    glLineCount++;



                }







                await _context.SaveChangesAsync();



                await transaction.CommitAsync();







                TempData["StatusMessage"] = $"Purchase PUR-{parentEntry.EntryNumber} posted to ledger successfully with {glLineCount} balanced GL entries.";



            }



            catch (Exception ex)



            {



                await transaction.RollbackAsync();



                TempData["ErrorMessage"] = "Error posting to ledger: " + ex.Message;



            }







            return RedirectToPage();



        }







        private async Task<AccountMaster> GetOrCreateAccountAsync(string defaultName, string searchKeyword, string preferredCategoryName, long activeUcpId)



        {



            var acc = await _context.AccountMasters



                .FirstOrDefaultAsync(a => a.AccountName != null && a.AccountName.Contains(searchKeyword) && a.UcpId == activeUcpId);







            if (acc != null) return acc;







            // Search category or pick first available category



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



            // 1. Permission check



            var path = HttpContext.Request.Path.Value ?? "";



            if (!AuthHelper.CanUpdate(HttpContext, path))



            {



                TempData["ErrorMessage"] = "Access Denied: You do not have permission to unpost ledger entries.";



                return RedirectToPage();



            }







            // 2. Find the parent PUR entry



            var entry = await _context.Entries.FindAsync(id);



            if (entry == null || entry.EntryType != "PUR")



            {



                TempData["ErrorMessage"] = "Purchase entry not found.";



                return RedirectToPage();



            }







            // 3. Get the TRANS rows (Debit + Credit) linked to this entry



            var transRows = await _context.Transactions



                .Where(t => t.EntryId == id)



                .ToListAsync();







            if (!transRows.Any())



            {



                TempData["ErrorMessage"] = "This entry is not posted to ledger.";



                return RedirectToPage();



            }







            // 4. Delete TRANS rows inside a DB transaction



            using var dbTxn = await _context.Database.BeginTransactionAsync();



            try



            {



                _context.Transactions.RemoveRange(transRows);



                await _context.SaveChangesAsync();



                await dbTxn.CommitAsync();







                TempData["StatusMessage"] = $"PUR-{entry.EntryNumber} has been unposted from the ledger. {transRows.Count} GL entries removed.";



            }



            catch (Exception ex)



            {



                await dbTxn.RollbackAsync();



                TempData["ErrorMessage"] = "Error unposting from ledger: " + ex.Message;



            }







            return RedirectToPage();



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.Entries == null) return RedirectToPage();

            var supplierNames = new Dictionary<long, string>();
            if (_context.SupplierMasters != null)
            {
                supplierNames = await _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId)
                    .ToDictionaryAsync(s => s.SupplierId, s => s.SupplierName ?? "Unknown");
            }

            var query = _context.Entries
                .Where(e => e.EntryType == "PUR" && e.UcpId == activeUcpId);

            if (PsdId.HasValue)
            {
                query = query.Where(e => e.EntryRefNo == PsdId.Value);
            }

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var smart = SmartSearchHelper.Parse(SearchTerm);
                var matchingSupplierIds = supplierNames
                    .Where(kv => kv.Value.ToLower().Contains(smart.CleanTerm))
                    .Select(kv => kv.Key.ToString())
                    .ToList();

                query = query.Where(e =>
                    (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                    e.EntryNumber.ToString().Contains(smart.CleanTerm) ||
                    (e.EntryAttribute1 != null && (matchingSupplierIds.Contains(e.EntryAttribute1) || e.EntryAttribute1.ToLower().Contains(smart.CleanTerm))) ||
                    (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(smart.CleanTerm)) ||
                    (e.EntryRefNo != null && e.EntryRefNo.ToString().Contains(smart.CleanTerm))
                );
            }

            var parents = await query.OrderByDescending(e => e.EntryId).ToListAsync();
            var parentIds = parents.Select(e => (long?)e.EntryId).ToList();

            var childSummaries = await _context.Entries
                .Where(e => e.EntryType == "PURL" && parentIds.Contains(e.EntryRefId))
                .GroupBy(e => e.EntryRefId)
                .Select(g => new
                {
                    ParentId = g.Key,
                    TotalAmount = g.Sum(x => (decimal?)(x.Amount ?? 0.0) ?? 0m)
                })
                .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.TotalAmount);

            // Fetch PSD Numbers mapping for Excel
            var exportPsdIds = parents.Where(e => e.EntryRefNo.HasValue).Select(e => e.EntryRefNo!.Value).Distinct().ToList();
            var exportPsdNumberMap = new Dictionary<long, long>();
            if (exportPsdIds.Any())
            {
                exportPsdNumberMap = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && exportPsdIds.Contains(e.EntryId))
                    .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);
            }

            // Fetch Line Items with Item Names for Excel
            var exportLineItemsRaw = await (from l in _context.Entries
                                            join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                            from i in itemJoin.DefaultIfEmpty()
                                            where l.EntryType == "PURL" && parentIds.Contains(l.EntryRefId)
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

            var exportList = parents.Select(e =>
            {
                long.TryParse(e.EntryAttribute1, out long sId);
                string suppName = supplierNames.TryGetValue(sId, out var name) ? name : (e.EntryAttribute1 ?? "-");
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
                if (e.EntryRefNo.HasValue)
                {
                    long psdNo = exportPsdNumberMap.TryGetValue(e.EntryRefNo.Value, out var pn) ? pn : e.EntryRefNo.Value;
                    narration = string.IsNullOrEmpty(narration) ? $"Soda: PSD-{psdNo}" : $"{narration} | Soda: PSD-{psdNo}";
                }
                if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Purchase Invoice")
                {
                    narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                }

                long psdDisplayNo = e.EntryRefNo.HasValue ? (exportPsdNumberMap.TryGetValue(e.EntryRefNo.Value, out var pn2) ? pn2 : e.EntryRefNo.Value) : 0;

                return new
                {
                    InvoiceNo = $"PUR-{e.EntryNumber}",
                    Date = e.EntryDate,
                    PsdNo = e.EntryRefNo.HasValue ? $"PSD-{psdDisplayNo}" : "-",
                    Supplier = suppName,
                    Narration = narration,
                    Broker = e.EntryAttribute2 ?? "-",
                    Amount = amt,
                    Remarks = e.EntryRemarks ?? "-"
                };
            }).ToList();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Invoice #", x => x.InvoiceNo, null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.Date, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("PSD #", x => x.PsdNo, null, XLAlignmentHorizontalValues.Center),
                new("Supplier Name", x => x.Supplier),
                new("Narration / Details", x => x.Narration),
                new("Broker", x => x.Broker),
                new("Total Amount", x => x.Amount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Remarks", x => x.Remarks)
            };

            var bytes = ExcelExportHelper.ExportToExcel("Purchase Invoices", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Purchase_Invoices_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
