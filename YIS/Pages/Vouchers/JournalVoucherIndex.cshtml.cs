using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;



using YIS.Helpers;







namespace YIS.Pages.Vouchers



{



    public class JournalVoucherIndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public JournalVoucherIndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<JournalVoucherViewModel> VoucherList { get; set; } = new List<JournalVoucherViewModel>();







        [BindProperty(SupportsGet = true)]



        public int CurrentPage { get; set; } = 1;







        [BindProperty(SupportsGet = true)]



        public int PageSize { get; set; } = 10;







        [BindProperty(SupportsGet = true)]



        public string? SearchTerm { get; set; }







        [BindProperty(SupportsGet = true)]



        public string SortDir { get; set; } = "desc";







        public List<string> SearchSuggestions { get; set; } = new List<string>();







        public PaginationViewModel Pagination { get; set; } = default!;







        public class JournalVoucherViewModel



        {



            public long EntryId { get; set; }



            public long? EntryNumber { get; set; }



            public DateTime? EntryDate { get; set; }



            public double? Amount { get; set; }



            public int ItemCount { get; set; }



            public bool IsPosted { get; set; }



        }







        public async Task OnGetAsync()



        {



            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);







            if (_context.Entries != null)



            {



                var query = _context.Entries



                    .Where(e => e.EntryType == "JV" && e.UcpId == activeUcpId);







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var smart = SmartSearchHelper.Parse(SearchTerm);



                    query = query.Where(e =>



                        (smart.HasNumber && e.EntryNumber == smart.ExtractedNumber.Value) ||



                        e.EntryNumber.ToString().Contains(smart.CleanTerm) ||



                        (e.EntryAttribute1 != null && e.EntryAttribute1.ToLower().Contains(smart.CleanTerm)) ||



                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(smart.CleanTerm))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                // Populate search suggestions (recent JV numbers)



                var recentNumbers = await _context.Entries



                    .Where(e => e.EntryType == "JV" && e.UcpId == activeUcpId && e.EntryNumber != null)



                    .OrderByDescending(e => e.EntryId)



                    .Take(8)



                    .Select(e => $"JV-{e.EntryNumber}")



                    .ToListAsync();



                SearchSuggestions = recentNumbers;







                var orderedQuery = (SortDir == "asc")



                    ? query.OrderBy(e => e.EntryId)



                    : query.OrderByDescending(e => e.EntryId);







                // Get parent entries



                var parentEntries = await orderedQuery



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();







                // Get child counts



                var childCounts = await _context.Entries



                    .Where(e => e.EntryType == "JVL")



                    .GroupBy(e => e.EntryRefId)



                    .Select(g => new { ParentId = g.Key, Count = g.Count() })



                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);







                // Check which entries are posted



                var entryIds = parentEntries.Select(e => e.EntryId).ToList();



                var postedEntryIds = new HashSet<long>();



                if (entryIds.Any())



                {



                    // Use Raw SQL to avoid EF Core generating OPENJSON/WITH syntax



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







                VoucherList = parentEntries.Select(e => new JournalVoucherViewModel



                {



                    EntryId = e.EntryId,



                    EntryNumber = e.EntryNumber,



                    EntryDate = e.EntryDate,



                    Amount = e.Amount,



                    ItemCount = childCounts.GetValueOrDefault(e.EntryId, 0),



                    IsPosted = postedEntryIds.Contains(e.EntryId)



                }).ToList();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            var path = "/Vouchers/JournalVoucher"; // Use fixed path for consistency



            if (!AuthHelper.CanDelete(HttpContext, path))



            {



                TempData["ErrorMessage"] = "Access Denied: You do not have permission to delete Journal Vouchers.";



                return RedirectToPage();



            }







            // Check if posted



            bool isPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);



            if (isPosted)



            {



                TempData["ErrorMessage"] = "Cannot delete: This voucher is posted to the ledger.";



                return RedirectToPage();



            }







            var entry = await _context.Entries.FindAsync(id);



            if (entry != null)



            {



                // Delete children first



                var children = await _context.Entries.Where(e => e.EntryRefId == id && e.EntryType == "JVL").ToListAsync();



                _context.Entries.RemoveRange(children);







                _context.Entries.Remove(entry);



                await _context.SaveChangesAsync();



            }







            return RedirectToPage();



        }







        public async Task<IActionResult> OnPostPostToLedgerAsync(long id)



        {



            // RBAC Check - Reuse Create/Update or Custom? Using Update logic implies modifying ledger



            var path = "/Vouchers/JournalVoucher";



            if (!AuthHelper.CanUpdate(HttpContext, path))



            {



                TempData["ErrorMessage"] = "Access Denied: You do not have permission to post vouchers.";



                return RedirectToPage();



            }







            // Check if already posted



            bool isPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);



            if (isPosted)



            {



                TempData["ErrorMessage"] = "Voucher is already posted.";



                return RedirectToPage();



            }







            var parentEntry = await _context.Entries.FindAsync(id);



            if (parentEntry == null || parentEntry.EntryType != "JV") return NotFound();







            var lineItems = await _context.Entries



                .Where(e => e.EntryRefId == id && e.EntryType == "JVL")



                .ToListAsync();







            if (!lineItems.Any())



            {



                TempData["ErrorMessage"] = "Cannot post: Voucher has no line items.";



                return RedirectToPage();



            }







            // Generate TransNo



            var maxTrans = await _context.Transactions.MaxAsync(t => (long?)t.TransNo) ?? 0;



            long newTransNo = maxTrans + 1;







            // Get Active Company Profile from Session



            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);



            if (!activeUcpId.HasValue)



            {



                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";



                return RedirectToPage("/SelectCompany");



            }







            using var transaction = await _context.Database.BeginTransactionAsync();



            try



            {



                // Create Trans entries for each line item



                foreach (var line in lineItems)



                {



                    long.TryParse(line.EntryAttribute1, out long accountId);



                    double.TryParse(line.EntryAttribute4, out double debit);



                    double.TryParse(line.EntryAttribute5, out double credit);







                    if (accountId > 0 && (debit > 0 || credit > 0))



                    {



                        var trans = new Trans



                        {



                            TransId = 0,



                            TransNo = newTransNo,



                            TransType = "JV",



                            AccountId = accountId,



                            Debit = debit,



                            Credit = credit,



                            Remarks = line.EntryRemarks,



                            EntryId = id,



                            UcpId = activeUcpId



                        };



                        _context.Transactions.Add(trans);



                    }



                }







                await _context.SaveChangesAsync();



                await transaction.CommitAsync();







                TempData["StatusMessage"] = "Journal Voucher posted to ledger successfully.";



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



            var path = "/Vouchers/JournalVoucher";



            if (!AuthHelper.CanUpdate(HttpContext, path))



            {



                TempData["ErrorMessage"] = "Access Denied: You do not have permission to unpost Journal Vouchers.";



                return RedirectToPage();



            }







            var entry = await _context.Entries.FindAsync(id);



            if (entry == null || entry.EntryType != "JV")



            {



                TempData["ErrorMessage"] = "Journal voucher not found.";



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







                TempData["StatusMessage"] = $"Journal Voucher JV-{entry.EntryNumber} has been unposted from the ledger. {transRows.Count} GL entries removed.";



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

            var query = _context.Entries
                .Where(e => e.EntryType == "JV" && e.UcpId == activeUcpId);

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
            var parentIds = parents.Select(e => (long?)e.EntryId).ToList();

            var childSummaries = await _context.Entries
                .Where(e => e.EntryType == "JVL" && parentIds.Contains(e.EntryRefId))
                .GroupBy(e => e.EntryRefId)
                .Select(g => new
                {
                    ParentId = g.Key,
                    ItemCount = g.Count(),
                    TotalDebit = g.Sum(x => (decimal?)(x.Amount ?? 0.0) ?? 0m)
                })
                .ToDictionaryAsync(x => x.ParentId ?? 0, x => x);

            var exportList = parents.Select(e =>
            {
                var summary = childSummaries.TryGetValue(e.EntryId, out var s) ? s : null;
                return new
                {
                    VoucherNo = $"JV-{e.EntryNumber}",
                    Date = e.EntryDate,
                    Items = summary?.ItemCount ?? 0,
                    Amount = summary?.TotalDebit ?? 0m,
                    Remarks = e.EntryRemarks ?? "-"
                };
            }).ToList();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Voucher (JV) #", x => x.VoucherNo, null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.Date, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("No. of Entries", x => x.Items, "#,##0", XLAlignmentHorizontalValues.Center),
                new("Total Amount", x => x.Amount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Remarks / Narration", x => x.Remarks)
            };

            var bytes = ExcelExportHelper.ExportToExcel("Journal Vouchers", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Journal_Vouchers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
