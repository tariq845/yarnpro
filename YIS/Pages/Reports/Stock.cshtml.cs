using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class StockModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public StockModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<StockItemViewModel> StockList { get; set; } = new();
        public double TotalStockItems { get; set; }
        public double TotalStockValue { get; set; }

        public class StockItemViewModel
        {
            public long ItemId { get; set; }
            public string ItemName { get; set; } = "";
            public string ItemCode { get; set; } = ""; // HS Code
            public double NetPurchase { get; set; }
            public double NetSale { get; set; }
            public double Quantity { get; set; }
            public double LastPurchaseRate { get; set; }
            public double StockValue { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // 1. Calculate Stock Levels
            // Net Purchase = PURL - DNL
            // Net Sale = SALL - CNL
            // Stock = Net Purchase - Net Sale
            var inventoryStats = await _context.Entries
                .Where(e => (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL") && (activeUcpId == null || e.UcpId == activeUcpId))
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    NetPurchase = g.Sum(e => e.EntryType == "PURL" ? (e.Qty ?? 0) : (e.EntryType == "DNL" ? -(e.Qty ?? 0) : 0)),
                    NetSale = g.Sum(e => e.EntryType == "SALL" ? (e.Qty ?? 0) : (e.EntryType == "CNL" ? -(e.Qty ?? 0) : 0))
                })
                .ToListAsync();

            TotalStockItems = inventoryStats.Sum(x => x.NetPurchase - x.NetSale);

            // 2. Fetch Last Purchase Rate for Standard Costing
            var lastPurchases = await _context.Entries
                .AsNoTracking()
                .Where(e => e.EntryType == "PURL" && (activeUcpId == null || e.UcpId == activeUcpId) && e.ItemId.HasValue && e.Rate.HasValue && e.Rate.Value > 0)
                .OrderByDescending(e => e.EntryDate)
                .ThenByDescending(e => e.EntryId)
                .Select(e => new { e.ItemId, e.Rate })
                .ToListAsync();

            var lastPurchaseRates = lastPurchases
                .GroupBy(e => e.ItemId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Rate ?? 0);

            // 3. Fetch Item Details for names and codes
            var itemList = await _context.ItemMasters
                .Where(i => activeUcpId == null || i.UcpId == activeUcpId)
                .Select(i => new { i.ItemId, i.ItemName, i.ItemHsCode })
                .AsNoTracking()
                .ToListAsync();

            var items = itemList.ToDictionary(i => (long)i.ItemId, i => new { ItemName = (string?)i.ItemName, ItemHsCode = (string?)i.ItemHsCode });

            // 4. Map to ViewModel with Standard Costing Valuation
            StockList = inventoryStats
                .Where(x => x.ItemId.HasValue && items.ContainsKey(x.ItemId.Value))
                .Select(x =>
                {
                    double qty = x.NetPurchase - x.NetSale;
                    double rate = lastPurchaseRates.TryGetValue(x.ItemId!.Value, out var r) ? r : 0;
                    return new StockItemViewModel
                    {
                        ItemId = x.ItemId!.Value,
                        ItemName = items[x.ItemId!.Value].ItemName ?? "Unknown",
                        ItemCode = items[x.ItemId!.Value].ItemHsCode ?? "N/A",
                        NetPurchase = x.NetPurchase,
                        NetSale = x.NetSale,
                        Quantity = qty,
                        LastPurchaseRate = rate,
                        StockValue = qty * rate
                    };
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            TotalStockValue = StockList.Sum(x => x.StockValue);
        }
    }
}
