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
            public double GrossPurchase { get; set; }
            public double PurchaseReturn { get; set; }
            public double NetPurchase { get; set; }
            public double GrossSale { get; set; }
            public double SaleReturn { get; set; }
            public double NetSale { get; set; }
            public double Quantity { get; set; }
            public double LastPurchaseRate { get; set; }
            public double StockValue { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // 1. Calculate Stock Levels
            // Gross Purchase = PURL
            // Purchase Return = DNL
            // Gross Sale = SALL
            // Sale Return = CNL
            // Net Purchase = Gross Purchase - Purchase Return
            // Net Sale = Gross Sale - Sale Return
            // Stock = Net Purchase - Net Sale
            var inventoryStats = await _context.Entries
                .Where(e => (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL") && (activeUcpId == null || e.UcpId == activeUcpId))
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    GrossPurchase = g.Sum(e => e.EntryType == "PURL" ? (e.Qty ?? 0) : 0),
                    PurchaseReturn = g.Sum(e => e.EntryType == "DNL" ? (e.Qty ?? 0) : 0),
                    GrossSale = g.Sum(e => e.EntryType == "SALL" ? (e.Qty ?? 0) : 0),
                    SaleReturn = g.Sum(e => e.EntryType == "CNL" ? (e.Qty ?? 0) : 0)
                })
                .ToListAsync();

            TotalStockItems = inventoryStats.Sum(x => (x.GrossPurchase - x.PurchaseReturn) - (x.GrossSale - x.SaleReturn));

            // 2. Fetch Last Purchase Rate for Standard Costing
            // Per-bag cost = Amount / Qty (not Rate per kg, because Amount already = Qty x Lbs x Rate)
            var lastPurchases = await _context.Entries
                .AsNoTracking()
                .Where(e => e.EntryType == "PURL" && (activeUcpId == null || e.UcpId == activeUcpId)
                         && e.ItemId.HasValue && e.Qty.HasValue && e.Qty.Value > 0
                         && e.Amount.HasValue && e.Amount.Value > 0)
                .OrderByDescending(e => e.EntryDate)
                .ThenByDescending(e => e.EntryId)
                .Select(e => new { e.ItemId, e.Qty, e.Amount })
                .ToListAsync();

            // Per-bag cost = Amount / Qty from the last purchase line for each item
            var lastPurchaseRates = lastPurchases
                .GroupBy(e => e.ItemId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => {
                        var last = g.First();
                        return (last.Qty.HasValue && last.Qty.Value > 0)
                            ? (last.Amount ?? 0) / last.Qty.Value
                            : 0;
                    });

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
                    double netPurchase = x.GrossPurchase - x.PurchaseReturn;
                    double netSale = x.GrossSale - x.SaleReturn;
                    double qty = netPurchase - netSale;
                    double rate = lastPurchaseRates.TryGetValue(x.ItemId!.Value, out var r) ? r : 0;
                    return new StockItemViewModel
                    {
                        ItemId = x.ItemId!.Value,
                        ItemName = items[x.ItemId!.Value].ItemName ?? "Unknown",
                        ItemCode = items[x.ItemId!.Value].ItemHsCode ?? "N/A",
                        GrossPurchase = x.GrossPurchase,
                        PurchaseReturn = x.PurchaseReturn,
                        NetPurchase = netPurchase,
                        GrossSale = x.GrossSale,
                        SaleReturn = x.SaleReturn,
                        NetSale = netSale,
                        Quantity = qty,
                        LastPurchaseRate = rate,
                        StockValue = qty * rate
                    };
                })
                .OrderBy(x => x.ItemName)
                .ToList();

            TotalStockValue = StockList.Sum(x => x.StockValue);
        }

        public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            await OnGetAsync();
            var columns = new List<ExcelColumn<StockItemViewModel>>
            {
                new("Item Name", x => x.ItemName, null, ClosedXML.Excel.XLAlignmentHorizontalValues.Left),
                new("HS Code", x => x.ItemCode, null, ClosedXML.Excel.XLAlignmentHorizontalValues.Center),
                new("Gross Purchase", x => x.GrossPurchase, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right),
                new("Purchase Return (DN)", x => x.PurchaseReturn, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right),
                new("Net Purchase", x => x.NetPurchase, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right),
                new("Gross Sale", x => x.GrossSale, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right),
                new("Sale Return (CN)", x => x.SaleReturn, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right),
                new("Net Sale", x => x.NetSale, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right),
                new("Stock in Hand", x => x.Quantity, "#,##0", ClosedXML.Excel.XLAlignmentHorizontalValues.Right)
            };

            var bytes = ExcelExportHelper.ExportToExcel("Stock in Hand Report", columns, StockList);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Stock_Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
