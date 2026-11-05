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
    public class IndexSodaModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexSodaModel(ApplicationDbContext context)
        {
            _context = context;
        }
        public IList<SaleSodaViewModel> Entries { get; set; } = new List<SaleSodaViewModel>();
        public Dictionary<long, string> CustomerNames { get; set; } = new Dictionary<long, string>();
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
        public class SodaLineDetail
        {
            public string ItemName { get; set; } = "";
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
        }

        public class SaleSodaViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? CustomerName { get; set; }
            public double? TotalQty { get; set; }
            public double? GrandTotal { get; set; }
            public string? RefPurchaseNo { get; set; }
            public long? RefPsdId { get; set; }
            public long? PsdNumber { get; set; }
            public string? Remarks { get; set; }
            public string? CompiledNarration { get; set; }
            public List<SodaLineDetail> LineDetails { get; set; } = new List<SodaLineDetail>();
            public int ItemCount { get; set; }
            public int InvoiceCount { get; set; }
        }
        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            // Prefetch Customers
            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .ToListAsync();
                foreach (var c in customers)
                {
                    if (!CustomerNames.ContainsKey(c.CustomerId))
                    {
                        CustomerNames.Add(c.CustomerId, c.CustomerName ?? "Unknown");
                    }
                }
            }
            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId);
                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);
                    query = query.Where(e =>
                        (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                        (e.EntryNumber != null && e.EntryNumber.ToString().Contains(smart.CleanTerm)) ||
                        (e.EntryRefNo != null && e.EntryRefNo.ToString().Contains(smart.CleanTerm)) ||
                        (_context.CustomerMasters.Any(c => c.CustomerId.ToString() == e.EntryAttribute1 && c.CustomerName.ToLower().Contains(smart.CleanTerm)))
                    );
                }
                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);
                // Populate search suggestions (recent SSD numbers and customer names)
                var recentNumbers = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId && e.EntryNumber != null)
                    .OrderByDescending(e => e.EntryId)
                    .Take(8)
                    .Select(e => $"SSD-{e.EntryNumber}")
                    .ToListAsync();
                SearchSuggestions = recentNumbers
                    .Concat(CustomerNames.Values.Take(6))
                    .Distinct()
                    .ToList();
                var orderedQuery = (SortDir == "asc")
                    ? query.OrderBy(e => e.EntryId)
                    : query.OrderByDescending(e => e.EntryId);
                // Get parent entries (SSD type only)
                var parentEntries = await orderedQuery
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
                // Get child entries raw
                var childRaw = await _context.Entries
                    .Where(e => e.EntryType == "SSDL" && e.EntryRefId != null && e.UcpId == activeUcpId)
                    .Select(e => new { RefId = e.EntryRefId, Qty = e.Qty ?? 0 })
                    .ToListAsync();
                
                var childStats = childRaw
                    .GroupBy(e => e.RefId)
                    .Select(g => new { ParentId = g.Key, ItemCount = g.Count(), QtySum = g.Sum(x => x.Qty) })
                    .ToDictionary(x => x.ParentId ?? 0, x => x);
                // Get invoice counts (SAL type referencing these SSD ids)
                var ssdIds = parentEntries.Select(e => e.EntryId).ToHashSet();
                var invoiceStats = new Dictionary<long, int>();
                if (ssdIds.Any())
                {
                    // Fetch all SAL entries and filter in memory to avoid SQL translation issues
                    var allSalesEntries = await _context.Entries
                        .Where(e => e.EntryType == "SAL" && e.EntryRefId != null && e.UcpId == activeUcpId)
                        .Select(e => e.EntryRefId.Value)
                        .ToListAsync();
                    
                    // Filter and group in memory
                    invoiceStats = allSalesEntries
                        .Where(ssdId => ssdIds.Contains(ssdId))
                        .GroupBy(ssdId => ssdId)
                        .ToDictionary(g => g.Key, g => g.Count());
                }
                // Fetch Line Items with Item Names for SSD
                var ssdEntryIds = parentEntries.Select(e => (long?)e.EntryId).ToList();
                var lineItemsRaw = await (from l in _context.Entries
                                          join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                          from i in itemJoin.DefaultIfEmpty()
                                          where l.EntryType == "SSDL" && ssdEntryIds.Contains(l.EntryRefId)
                                          select new
                                          {
                                              ParentId = l.EntryRefId ?? 0,
                                              ItemName = i != null ? i.ItemName : "Unknown",
                                              l.Qty,
                                              l.Lbs,
                                              l.Rate,
                                              l.Amount
                                          }).ToListAsync();

                var legacyParentItemIds = parentEntries.Where(e => e.ItemId.HasValue).Select(e => e.ItemId!.Value).Distinct().ToList();
                var legacyItemNameMap = new Dictionary<long, string>();
                if (legacyParentItemIds.Any())
                {
                    legacyItemNameMap = await _context.ItemMasters
                        .Where(i => legacyParentItemIds.Contains(i.ItemId))
                        .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "Unknown");
                }

                // PSD Numbers mapping for referenced PSD
                var refPsdIds = parentEntries
                    .Where(e => e.EntryRefId.HasValue)
                    .Select(e => e.EntryRefId!.Value)
                    .Distinct()
                    .ToList();
                var psdNumberMap = new Dictionary<long, long>();
                if (refPsdIds.Any())
                {
                    psdNumberMap = await _context.Entries
                        .Where(e => e.EntryType == "PSD" && refPsdIds.Contains(e.EntryId))
                        .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);
                }

                var lineItemsMap = lineItemsRaw
                    .GroupBy(x => x.ParentId)
                    .ToDictionary(g => g.Key, g => g.Select(x => new SodaLineDetail
                    {
                        ItemName = x.ItemName,
                        Qty = x.Qty,
                        Lbs = x.Lbs,
                        Rate = x.Rate,
                        Amount = x.Amount
                    }).ToList());

                Entries = parentEntries.Select(e =>
                {
                    string customerName = "Unknown";
                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long custId))
                    {
                        if (CustomerNames.ContainsKey(custId))
                        {
                            customerName = CustomerNames[custId];
                        }
                    }

                    long? psdNum = null;
                    if (e.EntryRefId.HasValue && psdNumberMap.ContainsKey(e.EntryRefId.Value))
                    {
                        psdNum = psdNumberMap[e.EntryRefId.Value];
                    }
                    else if (e.EntryRefNo.HasValue && e.EntryRefNo > 0)
                    {
                        psdNum = e.EntryRefNo.Value;
                    }
                    string refNo = psdNum.HasValue ? "PSD-" + psdNum : (!string.IsNullOrEmpty(e.EntryRefNo?.ToString()) ? "PSD-" + e.EntryRefNo : "-");

                    var lines = lineItemsMap.TryGetValue(e.EntryId, out var lList) ? lList : new List<SodaLineDetail>();
                    if (lines.Count == 0 && e.ItemId.HasValue)
                    {
                        string legacyName = legacyItemNameMap.TryGetValue(e.ItemId.Value, out var iname) ? iname : "Item";
                        lines = new List<SodaLineDetail>
                        {
                            new SodaLineDetail
                            {
                                ItemName = legacyName,
                                Qty = e.Qty,
                                Lbs = e.Lbs,
                                Rate = e.Rate,
                                Amount = e.Amount
                            }
                        };
                    }

                    int count = lines.Count;
                    double qty = e.Qty ?? 0;
                    if (childStats.ContainsKey(e.EntryId))
                    {
                        var stats = childStats[e.EntryId];
                        if (count == 0) count = stats.ItemCount;
                        if (qty == 0) qty = stats.QtySum;
                    }
                    else if (e.ItemId.HasValue && count == 0)
                    {
                        count = 1;
                    }

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
                    if (psdNum.HasValue && psdNum.Value > 0)
                    {
                        narration = string.IsNullOrEmpty(narration) ? $"Soda: PSD-{psdNum}" : $"{narration} | Soda: PSD-{psdNum}";
                    }
                    if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Sales Soda")
                    {
                        narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                    }

                    return new SaleSodaViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        CustomerName = customerName,
                        TotalQty = qty,
                        GrandTotal = e.Amount,
                        RefPurchaseNo = refNo,
                        RefPsdId = e.EntryRefId,
                        PsdNumber = psdNum,
                        Remarks = e.EntryRemarks,
                        CompiledNarration = narration,
                        LineDetails = lines,
                        ItemCount = count,
                        InvoiceCount = invoiceStats.ContainsKey(e.EntryId) ? invoiceStats[e.EntryId] : 0
                    };
                }).ToList();
            }
        }
        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.Entries == null)
            {
                return NotFound();
            }
            // Delete child entries first
            var children = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "SSDL")
                .ToListAsync();
            _context.Entries.RemoveRange(children);
            // Delete parent entry
            var entry = await _context.Entries.FindAsync(id);
            if (entry != null)
            {
                _context.Entries.Remove(entry);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
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
                .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId);
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var smart = SmartSearchHelper.Parse(SearchTerm);
                query = query.Where(e =>
                    (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                    (e.EntryNumber != null && e.EntryNumber.ToString().Contains(smart.CleanTerm)) ||
                    (_context.CustomerMasters.Any(c => c.CustomerId.ToString() == e.EntryAttribute1 && c.CustomerName.ToLower().Contains(smart.CleanTerm)))
                );
            }
            var parents = await query.OrderByDescending(e => e.EntryId).ToListAsync();
            var parentIds = parents.Select(e => (long?)e.EntryId).ToList();
            var childSummaries = await _context.Entries
                .Where(e => e.EntryType == "SSDL" && parentIds.Contains(e.EntryRefId))
                .GroupBy(e => e.EntryRefId)
                .Select(g => new
                {
                    ParentId = g.Key,
                    ItemCount = g.Count(),
                    GrandTotal = g.Sum(x => (decimal?)(x.Amount ?? 0.0) ?? 0m)
                })
                .ToDictionaryAsync(x => x.ParentId ?? 0, x => x);
            var exportLineItemsRaw = await (from l in _context.Entries
                                            join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                            from i in itemJoin.DefaultIfEmpty()
                                            where l.EntryType == "SSDL" && parentIds.Contains(l.EntryRefId)
                                            select new
                                            {
                                                ParentId = l.EntryRefId ?? 0,
                                                ItemName = i != null ? i.ItemName : "Unknown",
                                                l.Qty,
                                                l.Lbs,
                                                l.Rate,
                                                l.Amount
                                            }).ToListAsync();

            var exportLegacyItemIds = parents.Where(e => e.ItemId.HasValue).Select(e => e.ItemId!.Value).Distinct().ToList();
            var exportLegacyItemMap = new Dictionary<long, string>();
            if (exportLegacyItemIds.Any())
            {
                exportLegacyItemMap = await _context.ItemMasters
                    .Where(i => exportLegacyItemIds.Contains(i.ItemId))
                    .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "Unknown");
            }

            var exportPsdIds = parents.Where(e => e.EntryRefId.HasValue).Select(e => e.EntryRefId!.Value).Distinct().ToList();
            var exportPsdNumberMap = new Dictionary<long, long>();
            if (exportPsdIds.Any())
            {
                exportPsdNumberMap = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && exportPsdIds.Contains(e.EntryId))
                    .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);
            }

            var exportLineItemsMap = exportLineItemsRaw
                .GroupBy(x => x.ParentId)
                .ToDictionary(g => g.Key, g => g.Select(x => new SodaLineDetail
                {
                    ItemName = x.ItemName,
                    Qty = x.Qty,
                    Lbs = x.Lbs,
                    Rate = x.Rate,
                    Amount = x.Amount
                }).ToList());

            var exportList = parents.Select(e =>
            {
                long.TryParse(e.EntryAttribute1, out long cId);
                string custName = customerNames.TryGetValue(cId, out var name) ? name : (e.EntryAttribute1 ?? "-");
                var summary = childSummaries.TryGetValue(e.EntryId, out var s) ? s : null;

                var lines = exportLineItemsMap.TryGetValue(e.EntryId, out var lList) ? lList : new List<SodaLineDetail>();
                if (lines.Count == 0 && e.ItemId.HasValue)
                {
                    string legacyName = exportLegacyItemMap.TryGetValue(e.ItemId.Value, out var iname) ? iname : "Item";
                    lines = new List<SodaLineDetail>
                    {
                        new SodaLineDetail
                        {
                            ItemName = legacyName,
                            Qty = e.Qty,
                            Lbs = e.Lbs,
                            Rate = e.Rate,
                            Amount = e.Amount
                        }
                    };
                }

                long? psdNum = null;
                if (e.EntryRefId.HasValue && exportPsdNumberMap.ContainsKey(e.EntryRefId.Value))
                {
                    psdNum = exportPsdNumberMap[e.EntryRefId.Value];
                }
                else if (e.EntryRefNo.HasValue && e.EntryRefNo > 0)
                {
                    psdNum = e.EntryRefNo.Value;
                }

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
                if (psdNum.HasValue && psdNum.Value > 0)
                {
                    narration = string.IsNullOrEmpty(narration) ? $"Soda: PSD-{psdNum}" : $"{narration} | Soda: PSD-{psdNum}";
                }
                if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Sales Soda")
                {
                    narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                }

                decimal gTotal = (decimal?)(e.Amount ?? 0.0) ?? 0m;
                if (gTotal == 0m && summary != null) gTotal = summary.GrandTotal;

                return new
                {
                    ContractNo = $"SSD-{e.EntryNumber}",
                    Date = e.EntryDate,
                    Customer = custName,
                    Narration = narration,
                    Items = lines.Count > 0 ? lines.Count : (summary?.ItemCount ?? 0),
                    GrandTotal = gTotal,
                    Remarks = e.EntryRemarks ?? "-"
                };
            }).ToList();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Contract (SSD) #", x => x.ContractNo, null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.Date, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("Customer Name", x => x.Customer),
                new("Narration / Details", x => x.Narration),
                new("Grand Total", x => x.GrandTotal, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Remarks", x => x.Remarks)
            };
            var bytes = ExcelExportHelper.ExportToExcel("Sales Soda Contracts", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Sales_Soda_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
