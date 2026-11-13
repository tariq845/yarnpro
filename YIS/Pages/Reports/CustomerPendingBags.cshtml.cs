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
    public class CustomerPendingBagsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CustomerPendingBagsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // --- Filters ---
        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? CustomerId { get; set; }

        // --- Output ---
        public List<PendingRowViewModel> Rows { get; set; } = new();
        public List<ItemSummaryViewModel> ItemSummaries { get; set; } = new();
        public List<CustomerMaster> Customers { get; set; } = new();
        public double TotalDispatched { get; set; }
        public double TotalInvoiced { get; set; }
        public double TotalReturned { get; set; }
        public double TotalNetDelivered { get; set; }
        public double TotalPending { get; set; }

        public class ItemSummaryViewModel
        {
            public string ItemName { get; set; } = "Unknown";
            public double TotalDispatched { get; set; }
            public double TotalInvoiced { get; set; }
            public double TotalReturned { get; set; }
            public double TotalNetDelivered { get; set; }
            public double Balance { get; set; }
            public List<PendingRowViewModel> SsdLines { get; set; } = new();
        }

        public class PendingRowViewModel
        {
            public long SsdId { get; set; }
            public long? SsdNumber { get; set; }
            public DateTime? SsdDate { get; set; }
            public string CustomerName { get; set; } = "Unknown";
            public string ItemName { get; set; } = "Unknown";
            public double DispatchedQty { get; set; }
            public double InvoicedQty { get; set; }
            public double ReturnedQty { get; set; }
            public double NetDeliveredQty { get; set; }
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
            if (CustomerId.HasValue)
            {
                var custName = Customers.FirstOrDefault(c => c.CustomerId == CustomerId.Value)?.CustomerName;
                if (!string.IsNullOrEmpty(custName)) filterParts.Add($"Customer: {custName}");
            }
            string filterInfo = filterParts.Any() ? string.Join(" | ", filterParts) : "All Records";

            // Define Summary Sheet Columns
            var summaryColumns = new List<ExcelColumn<ItemSummaryViewModel>>
            {
                new ExcelColumn<ItemSummaryViewModel>("Item Name", x => x.ItemName, null, XLAlignmentHorizontalValues.Left),
                new ExcelColumn<ItemSummaryViewModel>("Total Dispatched (Bags)", x => x.TotalDispatched, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Invoiced (Bags)", x => x.TotalInvoiced, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Returned (Bags)", x => x.TotalReturned, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Net Delivered (Bags)", x => x.TotalNetDelivered, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Pending Bags", x => x.Balance, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<ItemSummaryViewModel>("Active SSD Count", x => x.SsdLines.Count, "#,##0", XLAlignmentHorizontalValues.Center)
            };

            // Define Detail Sheet Columns
            var detailColumns = new List<ExcelColumn<PendingRowViewModel>>
            {
                new ExcelColumn<PendingRowViewModel>("SSD #", x => $"SSD-{x.SsdNumber ?? x.SsdId}", null, XLAlignmentHorizontalValues.Center),
                new ExcelColumn<PendingRowViewModel>("SSD Date", x => x.SsdDate, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new ExcelColumn<PendingRowViewModel>("Customer Name", x => x.CustomerName, null, XLAlignmentHorizontalValues.Left),
                new ExcelColumn<PendingRowViewModel>("Item Name", x => x.ItemName, null, XLAlignmentHorizontalValues.Left),
                new ExcelColumn<PendingRowViewModel>("Dispatched Qty", x => x.DispatchedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Invoiced Qty", x => x.InvoicedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Returned Qty", x => x.ReturnedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Net Delivered Qty", x => x.NetDeliveredQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new ExcelColumn<PendingRowViewModel>("Pending Qty", x => x.PendingQty, "#,##0", XLAlignmentHorizontalValues.Right)
            };

            var bytes = ExcelExportHelper.CreateMultiSheetWorkbook(workbook =>
            {
                // Sheet 1: Summary
                var summarySheet = workbook.Worksheets.Add("Item Summary");
                ExcelExportHelper.PopulateWorksheet(summarySheet, "Customer Pending Bags Summary", summaryColumns, ItemSummaries, companyName, filterInfo, addTotalsRow: true);

                // Sheet 2: Detailed Drilldown
                var detailSheet = workbook.Worksheets.Add("Detailed Pending Dispatches");
                ExcelExportHelper.PopulateWorksheet(detailSheet, "Customer Pending Bags Detailed", detailColumns, Rows, companyName, filterInfo, addTotalsRow: true);
            });

            string fileName = $"Customer_Pending_Bags_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        private async Task LoadPendingBagsDataAsync(long activeUcpId)
        {
            if (_context.Entries == null) return;

            // Load customers for filter dropdown
            Customers = await _context.CustomerMasters
                .Where(c => c.UcpId == activeUcpId)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            // --- Step 1: Load all SSDs ---
            var ssdQuery = _context.Entries
                .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId);

            if (FromDate.HasValue)
                ssdQuery = ssdQuery.Where(e => e.EntryDate >= FromDate.Value);
            if (ToDate.HasValue)
                ssdQuery = ssdQuery.Where(e => e.EntryDate <= ToDate.Value);
            if (CustomerId.HasValue)
                ssdQuery = ssdQuery.Where(e => e.EntryAttribute1 == CustomerId.Value.ToString());

            var ssds = await ssdQuery.ToListAsync();
            if (!ssds.Any()) return;

            var ssdIds = ssds.Select(s => s.EntryId).ToList();

            // Customer name map
            var customerIds = ssds
                .Where(s => long.TryParse(s.EntryAttribute1, out _))
                .Select(s => long.Parse(s.EntryAttribute1!))
                .Distinct().ToList();

            var customerMap = await _context.CustomerMasters
                .Where(c => customerIds.Contains(c.CustomerId) && c.UcpId == activeUcpId)
                .ToDictionaryAsync(c => c.CustomerId, c => c.CustomerName ?? "Unknown");

            // --- Step 2: SSDL line items (dispatched qty per SSD per item) ---
            var ssdlItems = await (from ssdl in _context.Entries
                                   join item in _context.ItemMasters on ssdl.ItemId equals item.ItemId into itemJoin
                                   from item in itemJoin.DefaultIfEmpty()
                                   where ssdl.EntryType == "SSDL"
                                         && ssdl.UcpId == activeUcpId
                                         && ssdIds.Contains(ssdl.EntryRefId ?? 0)
                                   select new
                                   {
                                       ssdl.EntryRefId,  // = SSD EntryId
                                       ssdl.ItemId,
                                       ItemName = item != null ? item.ItemName : "Unknown",
                                       ssdl.Qty
                                   }).ToListAsync();

            // --- Step 3: SAL entries linked to SSDs (invoiced qty per SSD per item) ---
            var salItems = await (from sal in _context.Entries
                                  join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                  where sal.EntryType == "SAL"
                                        && sall.EntryType == "SALL"
                                        && sal.UcpId == activeUcpId
                                        && sall.UcpId == activeUcpId
                                        && ssdIds.Contains(sal.EntryRefId ?? 0)
                                  select new
                                  {
                                      SalId = sal.EntryId,
                                      SsdId = sal.EntryRefId,  // SSD EntryId
                                      sall.ItemId,
                                      sall.Qty
                                  }).ToListAsync();

            // --- Step 3b: CN entries linked to these SAL invoices (returned qty per SSD per item) ---
            var salIds = salItems.Select(x => x.SalId).Distinct().ToList();
            var cnItems = await (from cn in _context.Entries
                                 join cnl in _context.Entries on cn.EntryId equals cnl.EntryRefId
                                 where cn.EntryType == "CN"
                                       && cnl.EntryType == "CNL"
                                       && cn.UcpId == activeUcpId
                                       && cnl.UcpId == activeUcpId
                                       && cn.EntryRefId.HasValue
                                       && salIds.Contains(cn.EntryRefId.Value)
                                 select new
                                 {
                                     SalId = cn.EntryRefId.Value,
                                     cnl.ItemId,
                                     cnl.Qty
                                 }).ToListAsync();

            var salToSsd = salItems.GroupBy(x => x.SalId).ToDictionary(g => g.Key, g => g.First().SsdId);
            var returnedMap = new Dictionary<(long?, long?), double>();
            foreach (var cnItem in cnItems)
            {
                if (salToSsd.TryGetValue(cnItem.SalId, out long? sId))
                {
                    var key = (sId, cnItem.ItemId);
                    returnedMap[key] = (returnedMap.TryGetValue(key, out double r) ? r : 0) + (cnItem.Qty ?? 0);
                }
            }

            // Build gross invoiced map: SsdId + ItemId → gross invoiced qty
            var grossInvoicedMap = salItems
                .GroupBy(x => (x.SsdId, x.ItemId))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty ?? 0));

            // --- Step 4: Build rows ---
            var ssdMap = ssds.ToDictionary(s => s.EntryId);
            var rows = new List<PendingRowViewModel>();

            foreach (var line in ssdlItems)
            {
                double dispatched = line.Qty ?? 0;
                double invoiced = grossInvoicedMap.TryGetValue((line.EntryRefId, line.ItemId), out double inv) ? inv : 0;
                double returned = returnedMap.TryGetValue((line.EntryRefId, line.ItemId), out double ret) ? ret : 0;
                double netDelivered = Math.Max(0, invoiced - returned);
                double pending = dispatched - netDelivered;
                if (pending <= 0) continue; // Only show pending

                var ssd = ssdMap.TryGetValue(line.EntryRefId ?? 0, out var s) ? s : null;
                if (ssd == null) continue;

                long custId = long.TryParse(ssd.EntryAttribute1, out long cid) ? cid : 0;

                rows.Add(new PendingRowViewModel
                {
                    SsdId = ssd.EntryId,
                    SsdNumber = ssd.EntryNumber,
                    SsdDate = ssd.EntryDate,
                    CustomerName = customerMap.TryGetValue(custId, out string? cn) ? cn : "Unknown",
                    ItemName = line.ItemName ?? "Unknown",
                    DispatchedQty = dispatched,
                    InvoicedQty = invoiced,
                    ReturnedQty = returned,
                    NetDeliveredQty = netDelivered,
                    PendingQty = pending
                });
            }

            // Sort by customer name then SSD date desc
            Rows = rows.OrderBy(r => r.CustomerName).ThenByDescending(r => r.SsdDate).ToList();

            // Item-wise Summary
            ItemSummaries = Rows
                .GroupBy(r => r.ItemName)
                .Select(g => new ItemSummaryViewModel
                {
                    ItemName = g.Key,
                    TotalDispatched = g.Sum(x => x.DispatchedQty),
                    TotalInvoiced = g.Sum(x => x.InvoicedQty),
                    TotalReturned = g.Sum(x => x.ReturnedQty),
                    TotalNetDelivered = g.Sum(x => x.NetDeliveredQty),
                    Balance = g.Sum(x => x.PendingQty),
                    SsdLines = g.OrderBy(x => x.CustomerName).ThenByDescending(x => x.SsdDate).ToList()
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            // Totals
            TotalDispatched = Rows.Sum(r => r.DispatchedQty);
            TotalInvoiced = Rows.Sum(r => r.InvoicedQty);
            TotalReturned = Rows.Sum(r => r.ReturnedQty);
            TotalNetDelivered = Rows.Sum(r => r.NetDeliveredQty);
            TotalPending = Rows.Sum(r => r.PendingQty);
        }

        // -------------------------------------------------------
        // AJAX: SSD Detail for Modal
        // GET: /Reports/CustomerPendingBags?handler=SsdDetail&ssdId=55
        // -------------------------------------------------------
        public async Task<JsonResult> OnGetSsdDetailAsync(long ssdId)
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // Load SSD header
            var ssd = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == ssdId && e.EntryType == "SSD" && e.UcpId == activeUcpId);

            if (ssd == null)
                return new JsonResult(new { success = false, message = "SSD not found." });

            // Customer name
            string customerName = "Unknown";
            if (long.TryParse(ssd.EntryAttribute1, out long custId))
            {
                var cust = await _context.CustomerMasters.FirstOrDefaultAsync(c => c.CustomerId == custId && c.UcpId == activeUcpId);
                customerName = cust?.CustomerName ?? "Unknown";
            }

            // SSDL line items with item names
            var ssdlItems = await (from ssdl in _context.Entries
                                   join item in _context.ItemMasters on ssdl.ItemId equals item.ItemId into ij
                                   from item in ij.DefaultIfEmpty()
                                   where ssdl.EntryType == "SSDL" && ssdl.EntryRefId == ssdId && ssdl.UcpId == activeUcpId
                                   select new
                                   {
                                       ssdl.ItemId,
                                       ItemName = item != null ? item.ItemName : "Unknown",
                                       ssdl.Qty,
                                       ssdl.Rate,
                                       ssdl.Amount
                                   }).ToListAsync();

            // Invoiced qty per item (via SAL → SALL)
            var salEntries = await _context.Entries
                .Where(sal => sal.EntryType == "SAL" && sal.EntryRefId == ssdId && sal.UcpId == activeUcpId)
                .Select(sal => sal.EntryId)
                .ToListAsync();

            var sallData = await (from sal in _context.Entries
                                  join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                  where sal.EntryType == "SAL" && sall.EntryType == "SALL"
                                        && sal.UcpId == activeUcpId && sall.UcpId == activeUcpId
                                        && sal.EntryRefId == ssdId
                                  group sall by sall.ItemId into g
                                  select new { ItemId = g.Key, Invoiced = g.Sum(x => x.Qty ?? 0) })
                                 .ToListAsync();

            var returnedData = await (from cn in _context.Entries
                                      join cnl in _context.Entries on cn.EntryId equals cnl.EntryRefId
                                      where cn.EntryType == "CN" && cnl.EntryType == "CNL"
                                            && cn.UcpId == activeUcpId && cnl.UcpId == activeUcpId
                                            && cn.EntryRefId.HasValue && salEntries.Contains(cn.EntryRefId.Value)
                                      group cnl by cnl.ItemId into g
                                      select new { ItemId = g.Key, Returned = g.Sum(x => x.Qty ?? 0) })
                                     .ToListAsync();

            var invoicedRawMap = sallData.ToDictionary(x => x.ItemId ?? 0, x => x.Invoiced);
            var returnedModalMap = returnedData.ToDictionary(x => x.ItemId ?? 0, x => x.Returned);
            var invoicedMap = invoicedRawMap.ToDictionary(
                k => k.Key, 
                k => Math.Max(0, k.Value - (returnedModalMap.TryGetValue(k.Key, out double r) ? r : 0))
            );

            // Build detail lines
            var lines = ssdlItems.Select(l =>
            {
                double dispatched = l.Qty ?? 0;
                double invoiced   = invoicedRawMap.TryGetValue(l.ItemId ?? 0, out double inv) ? inv : 0;
                double returned   = returnedModalMap.TryGetValue(l.ItemId ?? 0, out double ret) ? ret : 0;
                double netDelivered = Math.Max(0, invoiced - returned);
                double pending    = dispatched - netDelivered;
                return new
                {
                    itemName      = l.ItemName,
                    dispatchedQty = dispatched,
                    invoicedQty   = invoiced,
                    returnedQty   = returned,
                    netDeliveredQty = netDelivered,
                    pendingQty    = pending < 0 ? 0 : pending,
                    rate          = l.Rate ?? 0,
                    amount        = l.Amount ?? 0
                };
            }).ToList();

            return new JsonResult(new
            {
                success       = true,
                ssdNumber     = ssd.EntryNumber,
                ssdDate       = ssd.EntryDate?.ToString("dd-MM-yyyy"),
                customerName  = customerName,
                remarks       = ssd.EntryRemarks ?? "-",
                lines         = lines,
                totalDispatched = lines.Sum(l => l.dispatchedQty),
                totalInvoiced   = lines.Sum(l => l.invoicedQty),
                totalReturned   = lines.Sum(l => l.returnedQty),
                totalNetDelivered = lines.Sum(l => l.netDeliveredQty),
                totalPending    = lines.Sum(l => l.pendingQty)
            });
        }
    }
}
