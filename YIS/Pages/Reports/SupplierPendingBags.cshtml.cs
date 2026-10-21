using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

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
        public double TotalPending { get; set; }

        public class ItemSummaryViewModel
        {
            public string ItemName { get; set; } = "Unknown";
            public double TotalOrdered { get; set; }
            public double TotalReceived { get; set; }
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
            public double PendingQty { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
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
                                       PsdId = pur.EntryRefNo,   // PSD EntryId
                                       purl.ItemId,
                                       purl.Qty
                                   }).ToListAsync();

            // Build received map: PsdId + ItemId → total received
            var receivedMap = purlItems
                .GroupBy(x => (x.PsdId, x.ItemId))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty ?? 0));

            // --- Step 4: Build rows ---
            var psdMap = psds.ToDictionary(p => p.EntryId);

            var rows = new List<PendingRowViewModel>();

            foreach (var line in psdlItems)
            {
                double ordered = line.Qty ?? 0;
                double received = receivedMap.TryGetValue((line.EntryRefId, line.ItemId), out double r) ? r : 0;
                double pending = ordered - received;
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
                    Balance = g.Sum(x => x.PendingQty),
                    PsdLines = g.OrderBy(x => x.SupplierName).ThenByDescending(x => x.PsdDate).ToList()
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            // Totals
            TotalOrdered = Rows.Sum(r => r.OrderedQty);
            TotalReceived = Rows.Sum(r => r.ReceivedQty);
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
            var purlData = await (from pur in _context.Entries
                                  join purl in _context.Entries on pur.EntryId equals purl.EntryRefId
                                  where pur.EntryType == "PUR" && purl.EntryType == "PURL"
                                        && pur.UcpId == activeUcpId && purl.UcpId == activeUcpId
                                        && pur.EntryRefNo == psdId
                                  group purl by purl.ItemId into g
                                  select new { ItemId = g.Key, Received = g.Sum(x => x.Qty ?? 0) })
                                 .ToListAsync();

            var receivedMap = purlData.ToDictionary(x => x.ItemId ?? 0, x => x.Received);

            // Build detail lines
            var lines = psdlItems.Select(l =>
            {
                double ordered = l.Qty ?? 0;
                double received = receivedMap.TryGetValue(l.ItemId ?? 0, out double r) ? r : 0;
                double pending = ordered - received;
                return new
                {
                    itemName   = l.ItemName,
                    orderedQty = ordered,
                    receivedQty = received,
                    pendingQty = pending < 0 ? 0 : pending,
                    rate       = l.Rate ?? 0,
                    amount     = l.Amount ?? 0
                };
            }).ToList();

            return new JsonResult(new
            {
                success      = true,
                psdNumber    = psd.EntryNumber,
                psdDate      = psd.EntryDate?.ToString("dd-MM-yyyy"),
                supplierName = supplierName,
                remarks      = psd.EntryRemarks ?? "-",
                lines        = lines,
                totalOrdered  = lines.Sum(l => l.orderedQty),
                totalReceived = lines.Sum(l => l.receivedQty),
                totalPending  = lines.Sum(l => l.pendingQty)
            });
        }
    }
}
