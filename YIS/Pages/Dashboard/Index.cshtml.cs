using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using System.Linq;
using YIS.Helpers;

namespace YIS.Pages.Dashboard
{
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DashboardModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // Stats
        public double TotalSalesCurrentMonth { get; set; }
        public double SalesTrend { get; set; }
        public int TotalOrdersCurrentMonth { get; set; }
        public double OrdersTrend { get; set; }
        public double TotalStockItems { get; set; }
        public double StockTrend { get; set; }
        public int ActiveCustomersCount { get; set; }
        public double CustomerTrend { get; set; }

        // Chart Data
        public string ChartLabelsJson { get; set; } = "[]";
        public string SalesChartDataJson { get; set; } = "[]";
        public string PurchaseChartDataJson { get; set; } = "[]";

        // Sales Highlights
        public double AverageOrderValue { get; set; }
        public string BestPerformingMonth { get; set; } = "N/A";
        public double GrowthPercentage { get; set; }
        public double MonthlyGoal { get; set; } = 1000000; // Default fallback
        public double GoalAchievementPercentage { get; set; }

        // Lists
        public List<Entry> RecentOrders { get; set; } = new();
        public List<StockItemInfo> LowStockItems { get; set; } = new();

        public class StockItemInfo
        {
            public string ItemName { get; set; } = "";
            public string ItemCode { get; set; } = "";
            public double Quantity { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                return RedirectToPage("/SelectCompany");
            }

            var today = DateTime.Today;
            var firstDayofMonth = new DateTime(today.Year, today.Month, 1);
            var firstDayofLastMonth = firstDayofMonth.AddMonths(-1);

            // 1. Sales Calculation (ENTRY_TYPE = 'SAL')
            var currentMonthSales = await _context.Entries
                .Where(e => e.EntryType == "SAL" && e.EntryDate >= firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);

            var lastMonthSales = await _context.Entries
                .Where(e => e.EntryType == "SAL" && e.EntryDate >= firstDayofLastMonth && e.EntryDate < firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);

            TotalSalesCurrentMonth = currentMonthSales;
            SalesTrend = lastMonthSales == 0 ? 100 : Math.Round(((currentMonthSales - lastMonthSales) / lastMonthSales) * 100, 1);

            // 2. Orders Count (SAL entries)
            TotalOrdersCurrentMonth = await _context.Entries
                .Where(e => e.EntryType == "SAL" && e.EntryDate >= firstDayofMonth && e.UcpId == activeUcpId)
                .CountAsync();

            var lastMonthOrders = await _context.Entries
                .Where(e => e.EntryType == "SAL" && e.EntryDate >= firstDayofLastMonth && e.EntryDate < firstDayofMonth && e.UcpId == activeUcpId)
                .CountAsync();

            OrdersTrend = lastMonthOrders == 0 ? 100 : Math.Round(((double)(TotalOrdersCurrentMonth - lastMonthOrders) / lastMonthOrders) * 100, 1);

            // Average Order Value
            AverageOrderValue = TotalOrdersCurrentMonth == 0 ? 0 : Math.Round(TotalSalesCurrentMonth / TotalOrdersCurrentMonth, 0);

            // 3. Stock Levels (Calculated from PURL vs SALL)
            var inventoryStats = await _context.Entries
                .Where(e => (e.EntryType == "PURL" || e.EntryType == "SALL") && e.UcpId == activeUcpId)
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Qty = g.Sum(e => e.EntryType == "PURL" ? (e.Qty ?? 0) : -(e.Qty ?? 0))
                })
                .ToListAsync();

            TotalStockItems = inventoryStats.Sum(x => x.Qty);
            StockTrend = 5.2;

            // 4. Active Customers
            ActiveCustomersCount = await _context.CustomerMasters
                .Where(c => (c.CustomerDeactiveOn == null || c.CustomerDeactiveOn > today) && c.UcpId == activeUcpId)
                .CountAsync();
            CustomerTrend = 3.4;

            // 5. Recent Transactions
            RecentOrders = await _context.Entries
                .Where(e => new[] { "SAL", "PUR", "JV", "RV", "PV" }.Contains(e.EntryType) && e.UcpId == activeUcpId)
                .OrderByDescending(e => e.EntryId)
                .Take(7) // Increased to 7 for better fill
                .ToListAsync();

            // 6. Low Stock Items (Qty < 10)
            var itemNames = await _context.ItemMasters.Where(i => i.UcpId == activeUcpId).ToDictionaryAsync(i => i.ItemId, i => i.ItemName);
            var itemCodes = await _context.ItemMasters.Where(i => i.UcpId == activeUcpId).ToDictionaryAsync(i => i.ItemId, i => i.ItemHsCode ?? "N/A");

            LowStockItems = inventoryStats
                .Where(x => x.Qty < 20 && x.ItemId.HasValue)
                .OrderBy(x => x.Qty)
                .Take(5)
                .Select(x => new StockItemInfo
                {
                    ItemName = itemNames.ContainsKey(x.ItemId!.Value) ? itemNames[x.ItemId.Value] ?? "Unknown" : "Unknown",
                    ItemCode = itemCodes.ContainsKey(x.ItemId!.Value) ? itemCodes[x.ItemId.Value] ?? "N/A" : "N/A",
                    Quantity = x.Qty
                })
                .ToList();

            // 7. Chart Data (Last 6 Months Sales & Purchases)
            var last6Months = Enumerable.Range(0, 6)
                .Select(i => firstDayofMonth.AddMonths(-i))
                .OrderBy(d => d)
                .ToList();

            var monthlySales = await _context.Entries
                .Where(e => e.EntryType == "SAL" && e.EntryDate >= last6Months.First() && e.UcpId == activeUcpId)
                .GroupBy(e => new { e.EntryDate!.Value.Year, e.EntryDate!.Value.Month })
                .Select(g => new
                {
                    Key = g.Key.Year + "-" + g.Key.Month.ToString("D2"),
                    Total = g.Sum(e => e.Amount ?? 0)
                })
                .ToDictionaryAsync(x => x.Key, x => x.Total);

            var monthlyPurchases = await _context.Entries
                .Where(e => e.EntryType == "PUR" && e.EntryDate >= last6Months.First() && e.UcpId == activeUcpId)
                .GroupBy(e => new { e.EntryDate!.Value.Year, e.EntryDate!.Value.Month })
                .Select(g => new
                {
                    Key = g.Key.Year + "-" + g.Key.Month.ToString("D2"),
                    Total = g.Sum(e => e.Amount ?? 0)
                })
                .ToDictionaryAsync(x => x.Key, x => x.Total);

            var chartLabels = last6Months.Select(d => d.ToString("MMM yyyy")).ToList();
            var salesData = last6Months.Select(d =>
            {
                var key = d.Year + "-" + d.Month.ToString("D2");
                return monthlySales.ContainsKey(key) ? monthlySales[key] : 0;
            }).ToList();

            var purchaseData = last6Months.Select(d =>
            {
                var key = d.Year + "-" + d.Month.ToString("D2");
                return monthlyPurchases.ContainsKey(key) ? monthlyPurchases[key] : 0;
            }).ToList();

            ChartLabelsJson = System.Text.Json.JsonSerializer.Serialize(chartLabels);
            SalesChartDataJson = System.Text.Json.JsonSerializer.Serialize(salesData);
            PurchaseChartDataJson = System.Text.Json.JsonSerializer.Serialize(purchaseData);

            // Best Performing Month
            if (monthlySales.Any())
            {
                var bestMonthKey = monthlySales.OrderByDescending(x => x.Value).First().Key;
                // Parse key "2024-12" to "Dec 2024"
                if (DateTime.TryParse(bestMonthKey + "-01", out DateTime bestDate))
                {
                    BestPerformingMonth = bestDate.ToString("MMM yyyy");
                }
            }

            // 8. Monthly Goal Tracker Calculation
            // Setting goal as 1.25x of last month's performance, with a minimum floor
            MonthlyGoal = Math.Max(1000000, lastMonthSales * 1.25);
            GoalAchievementPercentage = MonthlyGoal > 0 ? Math.Min(100, (TotalSalesCurrentMonth / MonthlyGoal) * 100) : 0;
            GoalAchievementPercentage = Math.Round(GoalAchievementPercentage, 0);

            return Page();
        }
    }
}
