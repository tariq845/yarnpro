using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

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
        public double TotalPending { get; set; }

        public class ItemSummaryViewModel
        {
            public string ItemName { get; set; } = "Unknown";
            public double TotalDispatched { get; set; }
            public double TotalInvoiced { get; set; }
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
            public double PendingQty { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
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
            // SAL.EntryRefId = SSD.EntryId
            var salItems = await (from sal in _context.Entries
                                  join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                  where sal.EntryType == "SAL"
                                        && sall.EntryType == "SALL"
                                        && sal.UcpId == activeUcpId
                                        && sall.UcpId == activeUcpId
                                        && ssdIds.Contains(sal.EntryRefId ?? 0)
                                  select new
                                  {
                                      SsdId = sal.EntryRefId,  // SSD EntryId
                                      sall.ItemId,
                                      sall.Qty
                                  }).ToListAsync();

            // Build invoiced map: SsdId + ItemId → total invoiced qty
            var invoicedMap = salItems
                .GroupBy(x => (x.SsdId, x.ItemId))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty ?? 0));

            // --- Step 4: Build rows ---
            var ssdMap = ssds.ToDictionary(s => s.EntryId);
            var rows = new List<PendingRowViewModel>();

            foreach (var line in ssdlItems)
            {
                double dispatched = line.Qty ?? 0;
                double invoiced = invoicedMap.TryGetValue((line.EntryRefId, line.ItemId), out double inv) ? inv : 0;
                double pending = dispatched - invoiced;
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
                    Balance = g.Sum(x => x.PendingQty),
                    SsdLines = g.OrderBy(x => x.CustomerName).ThenByDescending(x => x.SsdDate).ToList()
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            // Totals
            TotalDispatched = Rows.Sum(r => r.DispatchedQty);
            TotalInvoiced = Rows.Sum(r => r.InvoicedQty);
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
            var sallData = await (from sal in _context.Entries
                                  join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                  where sal.EntryType == "SAL" && sall.EntryType == "SALL"
                                        && sal.UcpId == activeUcpId && sall.UcpId == activeUcpId
                                        && sal.EntryRefId == ssdId
                                  group sall by sall.ItemId into g
                                  select new { ItemId = g.Key, Invoiced = g.Sum(x => x.Qty ?? 0) })
                                 .ToListAsync();

            var invoicedMap = sallData.ToDictionary(x => x.ItemId ?? 0, x => x.Invoiced);

            // Build detail lines
            var lines = ssdlItems.Select(l =>
            {
                double dispatched = l.Qty ?? 0;
                double invoiced   = invoicedMap.TryGetValue(l.ItemId ?? 0, out double inv) ? inv : 0;
                double pending    = dispatched - invoiced;
                return new
                {
                    itemName      = l.ItemName,
                    dispatchedQty = dispatched,
                    invoicedQty   = invoiced,
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
                totalPending    = lines.Sum(l => l.pendingQty)
            });
        }
    }
}
