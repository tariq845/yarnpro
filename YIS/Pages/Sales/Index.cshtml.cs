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
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? SsdId { get; set; }

        public PaginationViewModel Pagination { get; set; } = default!;

        public class SalesInvoiceViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string CustomerName { get; set; }
            public double Amount { get; set; } // Net / Grand Total
            public double? TaxAmount { get; set; }
            public string RefSsdNo { get; set; } // SSD-123
            public bool IsLedgerPosted { get; set; }
            public bool IsFbrPosted { get; set; }
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
                    var term = SearchTerm.ToLower();
                    query = query.Where(e =>
                        e.EntryNumber.ToString().Contains(term) ||
                        (e.EntryAttribute1 != null && e.EntryAttribute1.ToLower().Contains(term)) ||
                        (e.EntryAttribute2 != null && e.EntryAttribute2.ToLower().Contains(term))
                    );
                }

                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, 20);

                var sales = await query
                    .OrderByDescending(e => e.EntryId)
                    .Skip((CurrentPage - 1) * 20)
                    .Take(20)
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

                Invoices = sales.Select(s =>
                {
                    string cName = "Unknown";
                    if (long.TryParse(s.EntryAttribute1, out long cid) && CustomerNames.ContainsKey(cid))
                        cName = CustomerNames[cid];

                    string ssdRef = "-";
                    if (s.EntryRefId.HasValue) 
                    {
                        var num = ssdNumberMap.ContainsKey(s.EntryRefId.Value) ? ssdNumberMap[s.EntryRefId.Value].ToString() : s.EntryRefId.Value.ToString();
                        ssdRef = "SSD Ref: " + num;
                    }

                    return new SalesInvoiceViewModel
                    {
                        EntryId = s.EntryId,
                        EntryNumber = s.EntryNumber,
                        EntryDate = s.EntryDate,
                        CustomerName = cName,
                        Amount = s.Amount ?? 0,
                        TaxAmount = s.TaxAmount,
                        RefSsdNo = ssdRef,
                        IsLedgerPosted = postedEntryIds.Contains(s.EntryId),
                        IsFbrPosted = s.EntryPostedToFbr == true
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
            // Check if already posted
            if (await _context.Transactions.AnyAsync(t => t.EntryId == id))
            {
                TempData["ErrorMessage"] = "This sales entry is already posted.";
                return RedirectToPage();
            }

            // Get the parent entry
            var parentEntry = await _context.Entries.FindAsync(id);
            if (parentEntry == null || parentEntry.EntryType != "SAL")
            {
                TempData["ErrorMessage"] = "Sales entry not found.";
                return RedirectToPage();
            }

            // Get Customer Account
            long? customerAccountId = null;
            if (long.TryParse(parentEntry.EntryAttribute1, out long customerId))
            {
                var customer = await _context.CustomerMasters.FindAsync(customerId);
                customerAccountId = customer?.AccountId;
            }

            if (customerAccountId == null)
            {
                TempData["ErrorMessage"] = "Customer account not found. Please link an account to the customer first.";
                return RedirectToPage();
            }

            // Grand Total (inc. GST) is already stored in Amount field
            double grandTotal = parentEntry.Amount ?? 0;

            if (grandTotal <= 0)
            {
                TempData["ErrorMessage"] = "Cannot post: Grand total is zero or invalid.";
                return RedirectToPage();
            }

            // Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot post: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            // Use the invoice's own EntryNumber as TransNo (SAL-1 → TRANS_NO = 1)
            var newTransNo = parentEntry.EntryNumber ?? 0;

            if (newTransNo == 0)
            {
                TempData["ErrorMessage"] = "Cannot post: Sales Entry Number is missing or zero.";
                return RedirectToPage();
            }

            // Find Sales account from Chart of Accounts
            var salesAccount = await _context.AccountMasters
                .FirstOrDefaultAsync(a => a.AccountName!.Contains("Sales") && a.UcpId == activeUcpId);

            if (salesAccount == null)
            {
                // Auto-create Sales Account
                var category = await _context.CategoryMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .OrderByDescending(c => c.CategoryName!.Contains("Sales"))
                    .ThenByDescending(c => c.CategoryName!.Contains("Revenue"))
                    .ThenByDescending(c => c.CategoryName!.Contains("Income"))
                    .ThenByDescending(c => c.CategoryName!.Contains("General"))
                    .FirstOrDefaultAsync();

                if (category == null)
                {
                    TempData["ErrorMessage"] = "Cannot auto-create Sales account: No Account Categories found. Please create at least one Category first.";
                    return RedirectToPage();
                }

                salesAccount = new AccountMaster
                {
                    AccountId = 0,
                    AccountName = "Sales",
                    CategoryId = category.CategoryId,
                    CreationOn = DateTime.Now,
                    UcpId = activeUcpId
                };
                _context.AccountMasters.Add(salesAccount);
                await _context.SaveChangesAsync();
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Entry 1: Debit - Customer Account / Trade Debtors (Local)
                var debitTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "SAL",
                    AccountId = customerAccountId,
                    Debit = grandTotal,
                    Credit = 0,
                    Remarks = $"Sales Invoice SAL-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(debitTrans);

                // Entry 2: Credit - Sales Revenue Account
                var creditTrans = new Trans
                {
                    TransId = 0,
                    TransNo = newTransNo,
                    TransType = "SAL",
                    AccountId = salesAccount.AccountId,
                    Debit = 0,
                    Credit = grandTotal,
                    Remarks = $"Sales Invoice SAL-{parentEntry.EntryNumber}",
                    EntryId = id,
                    UcpId = activeUcpId
                };
                _context.Transactions.Add(creditTrans);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["StatusMessage"] = $"Sales Invoice SAL-{parentEntry.EntryNumber} posted to ledger successfully. (Details: TransNo={newTransNo})";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error posting to ledger: " + ex.Message;
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
    }
}
