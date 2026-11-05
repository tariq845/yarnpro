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
    public class IndexSodaModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexSodaModel(ApplicationDbContext context)
        {
            _context = context;
        }
        public IList<PurchaseSodaViewModel> Entries { get; set; } = new List<PurchaseSodaViewModel>();
        public Dictionary<long, string> SupplierNames { get; set; } = new Dictionary<long, string>();
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

        // ViewModel for list display
        public class PurchaseSodaViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public double? GrandTotal { get; set; }
            public int ItemCount { get; set; }
            public int InvoiceCount { get; set; }
            public string? Remarks { get; set; }
            public string? CompiledNarration { get; set; }
            public List<SodaLineDetail> LineDetails { get; set; } = new List<SodaLineDetail>();
        }
        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            // Prefetch Suppliers for display lookup
            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId)
                    .ToListAsync();
                foreach (var s in suppliers)
                {
                    if (!SupplierNames.ContainsKey(s.SupplierId))
                    {
                        SupplierNames.Add(s.SupplierId, s.SupplierName ?? "Unknown");
                    }
                }
            }
            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId);
                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);
                    query = query.Where(e => 
                        (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                        (e.EntryNumber != null && e.EntryNumber.ToString().Contains(smart.CleanTerm)) ||
                        (_context.SupplierMasters.Any(s => s.SupplierId.ToString() == e.EntryAttribute1 && s.SupplierName.ToLower().Contains(smart.CleanTerm)))
                    );
                }
                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);
                // Populate search suggestions (recent PSD numbers and supplier names)
                var recentNumbers = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId && e.EntryNumber != null)
                    .OrderByDescending(e => e.EntryId)
                    .Take(8)
                    .Select(e => $"PSD-{e.EntryNumber}")
                    .ToListAsync();
                SearchSuggestions = recentNumbers
                    .Concat(SupplierNames.Values.Take(6))
                    .Distinct()
                    .ToList();
                var orderedQuery = (SortDir == "asc")
                    ? query.OrderBy(e => e.EntryId)
                    : query.OrderByDescending(e => e.EntryId);
                // Get parent entries (PSD type only)
                var parentEntries = await orderedQuery
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
                // Get child entries count per parent
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "PSDL" && e.UcpId == activeUcpId)
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);
                // Get invoice counts (PUR type referencing these PSD ids via EntryRefNo)
                var psdIds = parentEntries.Select(e => e.EntryId).ToHashSet();
                var invoiceStats = new Dictionary<long, int>();
                if (psdIds.Any())
                {
                    // Fetch all PUR entries and filter in memory
                    var allPurchaseEntries = await _context.Entries
                        .Where(e => e.EntryType == "PUR" && e.EntryRefNo != null && e.UcpId == activeUcpId)
                        .Select(e => e.EntryRefNo.Value)
                        .ToListAsync();
                    
                    // Filter and group in memory
                    invoiceStats = allPurchaseEntries
                        .Where(psdId => psdIds.Contains(psdId))
                        .GroupBy(psdId => psdId)
                        .ToDictionary(g => g.Key, g => g.Count());
                }
                // Fetch Line Items with Item Names for PSD
                var psdEntryIds = parentEntries.Select(e => (long?)e.EntryId).ToList();
                var lineItemsRaw = await (from l in _context.Entries
                                          join i in _context.ItemMasters on l.ItemId equals i.ItemId into itemJoin
                                          from i in itemJoin.DefaultIfEmpty()
                                          where l.EntryType == "PSDL" && psdEntryIds.Contains(l.EntryRefId)
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

                // Build view models
                Entries = parentEntries.Select(e =>
                {
                    string supplierName = "Unknown";
                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long supId))
                    {
                        if (SupplierNames.ContainsKey(supId))
                        {
                            supplierName = SupplierNames[supId];
                        }
                    }

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
                    if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Purchase Soda")
                    {
                        narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                    }

                    return new PurchaseSodaViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        SupplierName = supplierName,
                        GrandTotal = e.Amount, // Grand total stored in Amount
                        ItemCount = lines.Count > 0 ? lines.Count : childCounts.GetValueOrDefault(e.EntryId, e.ItemId.HasValue ? 1 : 0),
                        InvoiceCount = invoiceStats.ContainsKey(e.EntryId) ? invoiceStats[e.EntryId] : 0,
                        Remarks = e.EntryRemarks,
                        CompiledNarration = narration,
                        LineDetails = lines
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
                .Where(e => e.EntryRefId == id && e.EntryType == "PSDL")
                .ToListAsync();
            _context.Entries.RemoveRange(children);
            // Delete parent entry
            var entry = await _context.Entries.FindAsync(id);
            if (entry != null)
            {
                _context.Entries.Remove(entry);
            }
            await _context.SaveChangesAsync();
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
                .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId);
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var smart = SmartSearchHelper.Parse(SearchTerm);
                query = query.Where(e =>
                    (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                    (e.EntryNumber != null && e.EntryNumber.ToString().Contains(smart.CleanTerm)) ||
                    (_context.SupplierMasters.Any(s => s.SupplierId.ToString() == e.EntryAttribute1 && s.SupplierName.ToLower().Contains(smart.CleanTerm)))
                );
            }
            var parents = await query.OrderByDescending(e => e.EntryId).ToListAsync();
            var parentIds = parents.Select(e => (long?)e.EntryId).ToList();
            var childSummaries = await _context.Entries
                .Where(e => e.EntryType == "PSDL" && parentIds.Contains(e.EntryRefId))
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
                                            where l.EntryType == "PSDL" && parentIds.Contains(l.EntryRefId)
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
                long.TryParse(e.EntryAttribute1, out long sId);
                string suppName = supplierNames.TryGetValue(sId, out var name) ? name : (e.EntryAttribute1 ?? "-");
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
                if (!string.IsNullOrWhiteSpace(e.EntryRemarks) && e.EntryRemarks != "Purchase Soda")
                {
                    narration = string.IsNullOrEmpty(narration) ? e.EntryRemarks : $"{narration} - {e.EntryRemarks}";
                }

                decimal gTotal = (decimal?)(e.Amount ?? 0.0) ?? 0m;
                if (gTotal == 0m && summary != null) gTotal = summary.GrandTotal;

                return new
                {
                    ContractNo = $"PSD-{e.EntryNumber}",
                    Date = e.EntryDate,
                    Supplier = suppName,
                    Narration = narration,
                    Items = lines.Count > 0 ? lines.Count : (summary?.ItemCount ?? 0),
                    GrandTotal = gTotal,
                    Remarks = e.EntryRemarks ?? "-"
                };
            }).ToList();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Contract (PSD) #", x => x.ContractNo, null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.Date, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("Supplier Name", x => x.Supplier),
                new("Narration / Details", x => x.Narration),
                new("Grand Total", x => x.GrandTotal, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Remarks", x => x.Remarks)
            };
            var bytes = ExcelExportHelper.ExportToExcel("Purchase Soda Contracts", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Purchase_Soda_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
