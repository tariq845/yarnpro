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

        public class StockItemViewModel
        {
            public long ItemId { get; set; }
            public string ItemName { get; set; } = "";
            public string ItemCode { get; set; } = ""; // HS Code
            public double NetPurchase { get; set; }
            public double NetSale { get; set; }
            public double Quantity { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // 1. Calculate Stock Levels
            // Net Purchase = PURL - DNL
            // Net Sale = SALL - CNL
            // Stock = Net Purchase - Net Sale
            var inventoryStats = await _context.Entries
                .Where(e => (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL") && e.UcpId == activeUcpId)
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    NetPurchase = g.Sum(e => e.EntryType == "PURL" ? (e.Qty ?? 0) : (e.EntryType == "DNL" ? -(e.Qty ?? 0) : 0)),
                    NetSale = g.Sum(e => e.EntryType == "SALL" ? (e.Qty ?? 0) : (e.EntryType == "CNL" ? -(e.Qty ?? 0) : 0))
                })
                .ToListAsync();

            TotalStockItems = inventoryStats.Sum(x => x.NetPurchase - x.NetSale);

            // 2. Fetch Item Details for names and codes
            var itemList = await _context.ItemMasters
                .Where(i => i.UcpId == activeUcpId)
                .Select(i => new { i.ItemId, i.ItemName, i.ItemHsCode })
                .AsNoTracking()
                .ToListAsync();

            var items = itemList.ToDictionary(i => (long)i.ItemId, i => new { ItemName = (string?)i.ItemName, ItemHsCode = (string?)i.ItemHsCode });

            // 3. Map to ViewModel
            StockList = inventoryStats
                .Where(x => x.ItemId.HasValue && items.ContainsKey(x.ItemId.Value))
                .Select(x => new StockItemViewModel
                {
                    ItemId = x.ItemId!.Value,
                    ItemName = items[x.ItemId!.Value].ItemName ?? "Unknown",
                    ItemCode = items[x.ItemId!.Value].ItemHsCode ?? "N/A",
                    NetPurchase = x.NetPurchase,
                    NetSale = x.NetSale,
                    Quantity = x.NetPurchase - x.NetSale
                })
                .OrderBy(x => x.ItemName)
                .ToList();
        }
    }
}
