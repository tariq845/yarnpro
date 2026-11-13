using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;
using YIS.Services;
using ClosedXML.Excel;

namespace YIS.Pages.Reports
{
    public class SupplierPendingBagsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SupplierPendingBagsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // --- Filters ---
        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? SupplierId { get; set; }

        // --- Output ---
        public List<PendingRowViewModel> Rows { get; set; } = new();
        public List<ItemSummaryViewModel> ItemSummaries { get; set; } = new();
        public List<SupplierMaster> Suppliers { get; set; } = new();
        public double TotalOrdered { get; set; }
        public double TotalReceived { get; set; }
        public double TotalReturned { get; set; }
        public double TotalNetReceived { get; set; }
        public double TotalPending { get; set; }

        public class ItemSummaryViewModel
        {
            public string ItemName { get; set; } = "Unknown";
            public double TotalOrdered { get; set; }
            public double TotalReceived { get; set; }
            public double TotalReturned { get; set; }
            public double TotalNetReceived { get; set; }
            public double Balance { get; set; }
            public List<PendingRowViewModel> PsdLines { get; set; } = new();
        }

        public class PendingRowViewModel
        {
            public long PsdId { get; set; }
            public long? PsdNumber { get; set; }
            public DateTime? PsdDate { get; set; }
            public string SupplierName { get; set; } = "Unknown";
            public string ItemName { get; set; } = "Unknown";
            public double OrderedQty { get; set; }
            public double ReceivedQty { get; set; }
            public double ReturnedQty { get; set; }
            public double NetReceivedQty { get; set; }
            public double PendingQty { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            await LoadPendingBagsDataAsync(activeUcpId.Value);
            return Page();
        }

        public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            await LoadPendingBagsDataAsync(activeUcpId.Value);

            var companyProfile = await _context.UserCompanyProfiles.FirstOrDefaultAsync(c => c.UcpId == activeUcpId.Value);
            string companyName = companyProfile?.UcpCompanyName ?? "Yarn Pro";

            var filterParts = new List<string>();
            if (FromDate.HasValue) filterParts.Add($"From: {FromDate.Value:dd-MM-yyyy}");
            if (ToDate.HasValue) filterParts.Add($"To: {ToDate.Value:dd-MM-yyyy}");
            if (SupplierId.HasValue)
            {
                var suppName = Suppliers.FirstOrDefault(s => s.SupplierId == SupplierId.Value)?.SupplierName;
                if (!string.IsNullOrEmpty(suppName)) filterParts.Add($"Supplier: {suppName}");
            }
            string filterInfo = filterParts.Any() ? string.Join(" | ", filterParts) : "All Records";

            // Define Summary Sheet Columns
            var summaryColumns = new List<ExcelColumn<ItemSummaryViewModel>>
            {
                new ExcelColumn<ItemSummaryViewModel>("Item Name", x => x.ItemName, null, XLAlignmentHorizontalValues.Left),
                new ExcelColumn<ItemSummaryViewModel>("Total Ordered (PSD Bags)", x => x.TotalOrdered, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Received (PUR Bags)", x => x.TotalReceived, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Returned (DN Bags)", x => x.TotalReturned, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Net Received", x => x.TotalNetReceived, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Pending Bags", x => x.Balance, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Active PSD Count", x => x.PsdLines.Count, "#,##0", XLAlignmentHorizontalValues.Center)
            };

            // Define Detail Sheet Columns
            var detailColumns = new List<ExcelColumn<PendingRowViewModel>>
            {
                new ExcelColumn<PendingRowViewModel>("PSD #", x => $"PSD-{x.PsdNumber ?? x.PsdId}", null, XLAlignmentHorizontalValues.Center),
                new ExcelColumn<PendingRowViewModel>("PSD Date", x => x.PsdDate, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new ExcelColumn<PendingRowViewModel>("Supplier Name", x => x.SupplierName, null, XLAlignmentHorizontalValues.Left),
                new ExcelColumn<PendingRowViewModel>("Item Name", x => x.ItemName, null, XLAlignmentHorizontalValues.Left),
                new ExcelColumn<PendingRowViewModel>("Ordered Qty", x => x.OrderedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Received Qty", x => x.ReceivedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Returned Qty (DN)", x => x.ReturnedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Net Received Qty", x => x.NetReceivedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Pending Qty", x => x.PendingQty, "#,##0", XLAlignmentHorizontalValues.Right)
            };

            var bytes = ExcelExportHelper.CreateMultiSheetWorkbook(workbook =>
            {
                // Sheet 1: Summary
                var summarySheet = workbook.Worksheets.Add("Item Summary");
                ExcelExportHelper.PopulateWorksheet(summarySheet, "Supplier Pending Bags Summary", summaryColumns, ItemSummaries, companyName, filterInfo, addTotalsRow: true);

                // Sheet 2: Detailed Drilldown
                var detailSheet = workbook.Worksheets.Add("Detailed Pending Orders");
                ExcelExportHelper.PopulateWorksheet(detailSheet, "Supplier Pending Bags Detailed", detailColumns, Rows, companyName, filterInfo, addTotalsRow: true);
            });

            string fileName = $"Supplier_Pending_Bags_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        private async Task LoadPendingBagsDataAsync(long activeUcpId)
        {
            if (_context.Entries == null) return;

            // Load suppliers for filter dropdown
            Suppliers = await _context.SupplierMasters
                .Where(s => s.UcpId == activeUcpId)
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            // --- Step 1: Load all PSDs with supplier join ---
            var psdQuery = _context.Entries
                .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId);

            if (FromDate.HasValue)
                psdQuery = psdQuery.Where(e => e.EntryDate >= FromDate.Value);
            if (ToDate.HasValue)
                psdQuery = psdQuery.Where(e => e.EntryDate <= ToDate.Value);
            if (SupplierId.HasValue)
                psdQuery = psdQuery.Where(e => e.EntryAttribute1 == SupplierId.Value.ToString());

            var psds = await psdQuery.ToListAsync();
            if (!psds.Any()) return;

            var psdIds = psds.Select(p => p.EntryId).ToList();
            var psdNumbers = psds.Select(p => p.EntryNumber).ToList();

            // Supplier name map
            var supplierIds = psds
                .Where(p => long.TryParse(p.EntryAttribute1, out _))
                .Select(p => long.Parse(p.EntryAttribute1!))
                .Distinct().ToList();

            var supplierMap = await _context.SupplierMasters
                .Where(s => supplierIds.Contains(s.SupplierId) && s.UcpId == activeUcpId)
                .ToDictionaryAsync(s => s.SupplierId, s => s.SupplierName ?? "Unknown");

            // --- Step 2: PSDL line items (ordered qty per PSD per item) ---
            var psdlItems = await (from psdl in _context.Entries
                                   join item in _context.ItemMasters on psdl.ItemId equals item.ItemId into itemJoin
                                   from item in itemJoin.DefaultIfEmpty()
                                   where psdl.EntryType == "PSDL"
                                         && psdl.UcpId == activeUcpId
                                         && psdIds.Contains(psdl.EntryRefId ?? 0)
                                   select new
                                   {
                                       psdl.EntryRefId,   // = PSD EntryId
                                       psdl.ItemId,
                                       ItemName = item != null ? item.ItemName : "Unknown",
                                       psdl.Qty
                                   }).ToListAsync();

            // --- Step 3: PURL line items (received qty per PSD per item) ---
            // PUR links to PSD via: PUR.EntryRefNo = PSD.EntryId
            var purlItems = await (from pur in _context.Entries
                                   join purl in _context.Entries on pur.EntryId equals purl.EntryRefId
                                   where pur.EntryType == "PUR"
                                         && purl.EntryType == "PURL"
                                         && pur.UcpId == activeUcpId
                                         && purl.UcpId == activeUcpId
                                         && psdIds.Contains(pur.EntryRefNo ?? 0)
                                   select new
                                   {
                                       PurId = pur.EntryId,
                                       PurNumber = pur.EntryNumber,
                                       PsdId = pur.EntryRefNo,   // PSD EntryId
                                       purl.ItemId,
                                       purl.Qty
                                   }).ToListAsync();

            // --- Step 3b: DNL line items (returned qty per PSD per item) ---
            var purIds = purlItems.Select(x => x.PurId).Distinct().ToList();
            var purNumbers = purlItems.Where(x => x.PurNumber.HasValue).Select(x => x.PurNumber!.Value).Distinct().ToList();

            var dnlItems = await (from dn in _context.Entries
                                  join dnl in _context.Entries on dn.EntryId equals dnl.EntryRefId
                                  where dn.EntryType == "DN"
                                        && dnl.EntryType == "DNL"
                                        && dn.UcpId == activeUcpId
                                        && dnl.UcpId == activeUcpId
                                        && ((dn.EntryRefId.HasValue && purIds.Contains(dn.EntryRefId.Value)) ||
                                            (dn.EntryRefNo.HasValue && purNumbers.Contains(dn.EntryRefNo.Value)))
                                  select new
                                  {
                                      PurId = dn.EntryRefId,
                                      PurNumber = dn.EntryRefNo,
                                      dnl.ItemId,
                                      dnl.Qty
                                  }).ToListAsync();

            var purToPsd = purlItems.GroupBy(x => x.PurId).ToDictionary(g => g.Key, g => g.First().PsdId);
            var purNumToPsd = purlItems.Where(x => x.PurNumber.HasValue).GroupBy(x => x.PurNumber!.Value).ToDictionary(g => g.Key, g => g.First().PsdId);

            var returnedMap = new Dictionary<(long?, long?), double>();
            foreach (var dnItem in dnlItems)
            {
                long? pId = null;
                if (dnItem.PurId.HasValue && purToPsd.TryGetValue(dnItem.PurId.Value, out long? p1))
                {
                    pId = p1;
                }
                else if (dnItem.PurNumber.HasValue && purNumToPsd.TryGetValue(dnItem.PurNumber.Value, out long? p2))
                {
                    pId = p2;
                }

                if (pId.HasValue)
                {
                    var key = (pId, dnItem.ItemId);
                    returnedMap[key] = (returnedMap.TryGetValue(key, out double r) ? r : 0) + (dnItem.Qty ?? 0);
                }
            }

            // Build received map: PsdId + ItemId → net received (Received - Returned)
            var receivedMap = purlItems
                .GroupBy(x => (x.PsdId, x.ItemId))
                .ToDictionary(
                    g => g.Key, 
                    g => Math.Max(0, g.Sum(x => x.Qty ?? 0) - (returnedMap.TryGetValue(g.Key, out double ret) ? ret : 0))
                );

            // Build raw received map: PsdId + ItemId
            var rawReceivedMap = purlItems
                .GroupBy(x => (x.PsdId, x.ItemId))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty ?? 0));

            // --- Step 4: Build rows ---
            var psdMap = psds.ToDictionary(p => p.EntryId);

            var rows = new List<PendingRowViewModel>();

            foreach (var line in psdlItems)
            {
                var key = (line.EntryRefId, line.ItemId);
                double ordered = line.Qty ?? 0;
                double received = rawReceivedMap.TryGetValue(key, out double r) ? r : 0;
                double returned = returnedMap.TryGetValue(key, out double ret) ? ret : 0;
                double netReceived = Math.Max(0, received - returned);
                double pending = ordered - netReceived;
                if (pending <= 0) continue; // Only show pending

                var psd = psdMap.TryGetValue(line.EntryRefId ?? 0, out var p) ? p : null;
                if (psd == null) continue;

                long suppId = long.TryParse(psd.EntryAttribute1, out long sid) ? sid : 0;

                rows.Add(new PendingRowViewModel
                {
                    PsdId = psd.EntryId,
                    PsdNumber = psd.EntryNumber,
                    PsdDate = psd.EntryDate,
                    SupplierName = supplierMap.TryGetValue(suppId, out string? sn) ? sn : "Unknown",
                    ItemName = line.ItemName ?? "Unknown",
                    OrderedQty = ordered,
                    ReceivedQty = received,
                    ReturnedQty = returned,
                    NetReceivedQty = netReceived,
                    PendingQty = pending
                });
            }

            // Sort by supplier name then PSD date
            Rows = rows.OrderBy(r => r.SupplierName).ThenByDescending(r => r.PsdDate).ToList();

            // Item-wise Summary
            ItemSummaries = Rows
                .GroupBy(r => r.ItemName)
                .Select(g => new ItemSummaryViewModel
                {
                    ItemName = g.Key,
                    TotalOrdered = g.Sum(x => x.OrderedQty),
                    TotalReceived = g.Sum(x => x.ReceivedQty),
                    TotalReturned = g.Sum(x => x.ReturnedQty),
                    TotalNetReceived = g.Sum(x => x.NetReceivedQty),
                    Balance = g.Sum(x => x.PendingQty),
                    PsdLines = g.OrderBy(x => x.SupplierName).ThenByDescending(x => x.PsdDate).ToList()
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            // Totals
            TotalOrdered = Rows.Sum(r => r.OrderedQty);
            TotalReceived = Rows.Sum(r => r.ReceivedQty);
            TotalReturned = Rows.Sum(r => r.ReturnedQty);
            TotalNetReceived = Rows.Sum(r => r.NetReceivedQty);
            TotalPending = Rows.Sum(r => r.PendingQty);
        }

        // -------------------------------------------------------
        // AJAX: PSD Detail for Modal
        // GET: /Reports/SupplierPendingBags?handler=PsdDetail&psdId=42
        // -------------------------------------------------------
        public async Task<JsonResult> OnGetPsdDetailAsync(long psdId)
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // Load PSD header
            var psd = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == psdId && e.EntryType == "PSD" && e.UcpId == activeUcpId);

            if (psd == null)
                return new JsonResult(new { success = false, message = "PSD not found." });

            // Supplier name
            string supplierName = "Unknown";
            if (long.TryParse(psd.EntryAttribute1, out long suppId))
            {
                var sup = await _context.SupplierMasters.FirstOrDefaultAsync(s => s.SupplierId == suppId && s.UcpId == activeUcpId);
                supplierName = sup?.SupplierName ?? "Unknown";
            }

            // PSDL line items with item names
            var psdlItems = await (from psdl in _context.Entries
                                   join item in _context.ItemMasters on psdl.ItemId equals item.ItemId into ij
                                   from item in ij.DefaultIfEmpty()
                                   where psdl.EntryType == "PSDL" && psdl.EntryRefId == psdId && psdl.UcpId == activeUcpId
                                   select new
                                   {
                                       psdl.ItemId,
                                       ItemName = item != null ? item.ItemName : "Unknown",
                                       psdl.Qty,
                                       psdl.Rate,
                                       psdl.Amount
                                   }).ToListAsync();

            // Received qty per item (via PUR → PURL)
            var purEntries = await _context.Entries
                .Where(pur => pur.EntryType == "PUR" && pur.EntryRefNo == psdId && pur.UcpId == activeUcpId)
                .Select(pur => new { pur.EntryId, pur.EntryNumber })
                .ToListAsync();

            var purEntryIds = purEntries.Select(x => x.EntryId).ToList();
            var purEntryNumbers = purEntries.Where(x => x.EntryNumber.HasValue).Select(x => x.EntryNumber!.Value).ToList();

            var purlData = await (from pur in _context.Entries
                                   join purl in _context.Entries on pur.EntryId equals purl.EntryRefId
                                   where pur.EntryType == "PUR" && purl.EntryType == "PURL"
                                         && pur.UcpId == activeUcpId && purl.UcpId == activeUcpId
                                         && pur.EntryRefNo == psdId
                                   group purl by purl.ItemId into g
                                   select new { ItemId = g.Key, Received = g.Sum(x => x.Qty ?? 0) })
                                  .ToListAsync();

            var returnedData = await (from dn in _context.Entries
                                      join dnl in _context.Entries on dn.EntryId equals dnl.EntryRefId
                                      where dn.EntryType == "DN" && dnl.EntryType == "DNL"
                                            && dn.UcpId == activeUcpId && dnl.UcpId == activeUcpId
                                            && ((dn.EntryRefId.HasValue && purEntryIds.Contains(dn.EntryRefId.Value)) ||
                                                (dn.EntryRefNo.HasValue && purEntryNumbers.Contains(dn.EntryRefNo.Value)))
                                      group dnl by dnl.ItemId into g
                                      select new { ItemId = g.Key, Returned = g.Sum(x => x.Qty ?? 0) })
                                     .ToListAsync();

            var receivedRawMap = purlData.ToDictionary(x => x.ItemId ?? 0, x => x.Received);
            var returnedModalMap = returnedData.ToDictionary(x => x.ItemId ?? 0, x => x.Returned);

            // Build detail lines
            var lines = psdlItems.Select(l =>
            {
                double ordered = l.Qty ?? 0;
                double received = receivedRawMap.TryGetValue(l.ItemId ?? 0, out double r) ? r : 0;
                double returned = returnedModalMap.TryGetValue(l.ItemId ?? 0, out double ret) ? ret : 0;
                double netReceived = Math.Max(0, received - returned);
                double pending = ordered - netReceived;
                return new
                {
                    itemName       = l.ItemName,
                    orderedQty     = ordered,
                    receivedQty    = received,
                    returnedQty    = returned,
                    netReceivedQty = netReceived,
                    pendingQty     = pending < 0 ? 0 : pending,
                    rate           = l.Rate ?? 0,
                    amount         = l.Amount ?? 0
                };
            }).ToList();

            return new JsonResult(new
            {
                success          = true,
                psdNumber        = psd.EntryNumber,
                psdDate          = psd.EntryDate?.ToString("dd-MM-yyyy"),
                supplierName     = supplierName,
                remarks          = psd.EntryRemarks ?? "-",
                lines            = lines,
                totalOrdered     = lines.Sum(l => l.orderedQty),
                totalReceived    = lines.Sum(l => l.receivedQty),
                totalReturned    = lines.Sum(l => l.returnedQty),
                totalNetReceived = lines.Sum(l => l.netReceivedQty),
                totalPending     = lines.Sum(l => l.pendingQty)
            });
        }
    }
}
