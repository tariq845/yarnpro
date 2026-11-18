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

        // Stats - Sales & Orders
        public double TotalSalesCurrentMonth { get; set; }
        public double SalesTrend { get; set; }
        public int TotalOrdersCurrentMonth { get; set; }
        public double OrdersTrend { get; set; }
        public double TotalPurchasesCurrentMonth { get; set; }
        public double PurchaseTrend { get; set; }
        public double TotalStockItems { get; set; }
        public double StockTrend { get; set; }
        public int ActiveCustomersCount { get; set; }
        public double CustomerTrend { get; set; }
        public int ActiveSuppliersCount { get; set; }

        // Financials & Balances
        public double TotalReceivables { get; set; }
        public double TotalPayables { get; set; }
        public double TotalReceiptsCurrentMonth { get; set; }
        public double TotalPaymentsCurrentMonth { get; set; }

        // Stock Valuation & Commission
        public double TotalStockValue { get; set; }
        public double TotalCommissionBalance { get; set; }
        public double CurrentMonthCommission { get; set; }

        // Chart Data
        public string ChartLabelsJson { get; set; } = "[]";
        public string SalesChartDataJson { get; set; } = "[]";
        public string PurchaseChartDataJson { get; set; } = "[]";
        public string CommissionChartDataJson { get; set; } = "[]";
        public string StockValuationLabelsJson { get; set; } = "[]";
        public string StockValuationDataJson { get; set; } = "[]";

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

            // 1. Net Sales Calculation (SAL minus CN)
            var currentMonthSales = await _context.Entries
                .Where(e => (e.EntryType == "SAL" || e.EntryType == "CN") && e.EntryDate >= firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.EntryType == "SAL" ? (e.Amount ?? 0) : -(e.Amount ?? 0));

            var lastMonthSales = await _context.Entries
                .Where(e => (e.EntryType == "SAL" || e.EntryType == "CN") && e.EntryDate >= firstDayofLastMonth && e.EntryDate < firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.EntryType == "SAL" ? (e.Amount ?? 0) : -(e.Amount ?? 0));

            TotalSalesCurrentMonth = Math.Max(0, currentMonthSales);
            SalesTrend = lastMonthSales == 0 ? 100 : Math.Round(((currentMonthSales - lastMonthSales) / lastMonthSales) * 100, 1);

            // 1b. Net Purchase Calculation (PUR minus DN)
            var currentMonthPurchases = await _context.Entries
                .Where(e => (e.EntryType == "PUR" || e.EntryType == "DN") && e.EntryDate >= firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.EntryType == "PUR" ? (e.Amount ?? 0) : -(e.Amount ?? 0));

            var lastMonthPurchases = await _context.Entries
                .Where(e => (e.EntryType == "PUR" || e.EntryType == "DN") && e.EntryDate >= firstDayofLastMonth && e.EntryDate < firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.EntryType == "PUR" ? (e.Amount ?? 0) : -(e.Amount ?? 0));

            TotalPurchasesCurrentMonth = Math.Max(0, currentMonthPurchases);
            PurchaseTrend = lastMonthPurchases == 0 ? 100 : Math.Round(((currentMonthPurchases - lastMonthPurchases) / lastMonthPurchases) * 100, 1);

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

            // 2b. Monthly Cash Inflow (RV) & Outflow (PV)
            TotalReceiptsCurrentMonth = await _context.Entries
                .Where(e => e.EntryType == "RV" && e.EntryDate >= firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);

            TotalPaymentsCurrentMonth = await _context.Entries
                .Where(e => e.EntryType == "PV" && e.EntryDate >= firstDayofMonth && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);

            // 2c. Outstanding Receivables (AR) & Payables (AP)
            var totalSalesAllTime = await _context.Entries
                .Where(e => (e.EntryType == "SAL" || e.EntryType == "DN") && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);
            var totalReceiptsAllTime = await _context.Entries
                .Where(e => (e.EntryType == "RV" || e.EntryType == "CN") && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);
            TotalReceivables = Math.Max(0, totalSalesAllTime - totalReceiptsAllTime);

            var totalPurAllTime = await _context.Entries
                .Where(e => (e.EntryType == "PUR" || e.EntryType == "CN") && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);
            var totalPaymentsAllTime = await _context.Entries
                .Where(e => (e.EntryType == "PV" || e.EntryType == "DN") && e.UcpId == activeUcpId)
                .SumAsync(e => e.Amount ?? 0);
            TotalPayables = Math.Max(0, totalPurAllTime - totalPaymentsAllTime);

            // 3. Stock Levels (Calculated from Net Purchase [PURL - DNL] vs Net Sale [SALL - CNL])
            var inventoryStats = await _context.Entries
                .Where(e => (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL") && e.UcpId == activeUcpId)
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Qty = g.Sum(e => (e.EntryType == "PURL" || e.EntryType == "CNL") ? (e.Qty ?? 0) : -(e.Qty ?? 0))
                })
                .ToListAsync();

            TotalStockItems = inventoryStats.Sum(x => x.Qty);
            StockTrend = 5.2;

            // 3b. Stock Valuation (Standard Costing using Last Purchase Rate)
            var lastPurchases = await _context.Entries
                .AsNoTracking()
                .Where(e => e.EntryType == "PURL" && e.UcpId == activeUcpId && e.ItemId.HasValue && e.Rate.HasValue && e.Rate.Value > 0)
                .OrderByDescending(e => e.EntryDate)
                .ThenByDescending(e => e.EntryId)
                .Select(e => new { e.ItemId, e.Rate })
                .ToListAsync();

            var lastPurchaseRates = lastPurchases
                .GroupBy(e => e.ItemId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Rate ?? 0);

            TotalStockValue = inventoryStats
                .Where(x => x.ItemId.HasValue && x.Qty > 0)
                .Sum(x => x.Qty * (lastPurchaseRates.TryGetValue(x.ItemId!.Value, out var r) ? r : 0));

            // 3c. Commission Balance Calculation
            // Check GL Commission Payable Accounts
            var commAccountIds = await _context.AccountMasters
                .Where(a => a.UcpId == activeUcpId &&
                           (a.AccountName.ToLower().Contains("commission payable") ||
                            a.AccountName.ToLower().Contains("comm payable")))
                .Select(a => a.AccountId)
                .ToListAsync();

            var custCommAccs = await _context.CustomerMasters
                .Where(c => c.UcpId == activeUcpId && c.CommPayableAccountId.HasValue)
                .Select(c => c.CommPayableAccountId!.Value)
                .ToListAsync();

            var suppCommAccs = await _context.SupplierMasters
                .Where(s => s.UcpId == activeUcpId && s.CommPayableAccountId.HasValue)
                .Select(s => s.CommPayableAccountId!.Value)
                .ToListAsync();

            var allCommPayableAccountIds = commAccountIds.Union(custCommAccs).Union(suppCommAccs).Distinct().ToList();

            double glCommBalance = 0;
            if (allCommPayableAccountIds.Any())
            {
                var glTrans = await _context.Transactions
                    .Where(t => t.UcpId == activeUcpId && t.AccountId.HasValue && allCommPayableAccountIds.Contains(t.AccountId.Value))
                    .Select(t => new { t.Debit, t.Credit })
                    .ToListAsync();

                // Liability Account Balance = Credit - Debit
                glCommBalance = glTrans.Sum(t => (t.Credit ?? 0) - (t.Debit ?? 0));
            }

            // Operational Commission from Invoices (SAL/CN/PUR/DN)
            var salesCommissionAllTime = await _context.Entries
                .Where(e => e.UcpId == activeUcpId && (e.EntryType == "SAL" || e.EntryType == "CN"))
                .SumAsync(e => e.EntryType == "SAL" ? (e.EntryCommAmt ?? 0) : -(e.EntryCommAmt ?? 0));

            var purCommissionAllTime = await _context.Entries
                .Where(e => e.UcpId == activeUcpId && (e.EntryType == "PUR" || e.EntryType == "DN"))
                .SumAsync(e => e.EntryType == "PUR" ? (e.EntryCommAmt ?? 0) : -(e.EntryCommAmt ?? 0));

            var totalCommissionBooked = Math.Max(0, salesCommissionAllTime + purCommissionAllTime);

            var commPaidViaPv = await _context.Entries
                .Where(e => e.UcpId == activeUcpId && e.EntryType == "PV" && e.EntryRemarks != null && e.EntryRemarks.ToLower().Contains("commission"))
                .SumAsync(e => e.Amount ?? 0);

            TotalCommissionBalance = glCommBalance > 0 ? glCommBalance : Math.Max(0, totalCommissionBooked - commPaidViaPv);

            // Current Month Commission
            var currentMonthSalesComm = await _context.Entries
                .Where(e => e.UcpId == activeUcpId && (e.EntryType == "SAL" || e.EntryType == "CN") && e.EntryDate >= firstDayofMonth)
                .SumAsync(e => e.EntryType == "SAL" ? (e.EntryCommAmt ?? 0) : -(e.EntryCommAmt ?? 0));

            var currentMonthPurComm = await _context.Entries
                .Where(e => e.UcpId == activeUcpId && (e.EntryType == "PUR" || e.EntryType == "DN") && e.EntryDate >= firstDayofMonth)
                .SumAsync(e => e.EntryType == "PUR" ? (e.EntryCommAmt ?? 0) : -(e.EntryCommAmt ?? 0));

            CurrentMonthCommission = Math.Max(0, currentMonthSalesComm + currentMonthPurComm);

            // 4. Active Customers & Suppliers
            ActiveCustomersCount = await _context.CustomerMasters
                .Where(c => (c.CustomerDeactiveOn == null || c.CustomerDeactiveOn > today) && c.UcpId == activeUcpId)
                .CountAsync();
            CustomerTrend = 3.4;

            ActiveSuppliersCount = await _context.SupplierMasters
                .Where(s => (s.SupplierDeactiveOn == null || s.SupplierDeactiveOn > today) && s.UcpId == activeUcpId)
                .CountAsync();

            // 5. Recent Transactions
            RecentOrders = await _context.Entries
                .Where(e => new[] { "SAL", "PUR", "JV", "RV", "PV", "DN", "CN" }.Contains(e.EntryType) && e.UcpId == activeUcpId)
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

            // 7. Chart Data (Last 6 Months Net Sales & Net Purchases)
            var last6Months = Enumerable.Range(0, 6)
                .Select(i => firstDayofMonth.AddMonths(-i))
                .OrderBy(d => d)
                .ToList();

            var monthlySales = await _context.Entries
                .Where(e => (e.EntryType == "SAL" || e.EntryType == "CN") && e.EntryDate >= last6Months.First() && e.UcpId == activeUcpId)
                .GroupBy(e => new { e.EntryDate!.Value.Year, e.EntryDate!.Value.Month })
                .Select(g => new
                {
                    Key = g.Key.Year + "-" + g.Key.Month.ToString("D2"),
                    Total = g.Sum(e => e.EntryType == "SAL" ? (e.Amount ?? 0) : -(e.Amount ?? 0))
                })
                .ToDictionaryAsync(x => x.Key, x => Math.Max(0, x.Total));

            var monthlyPurchases = await _context.Entries
                .Where(e => (e.EntryType == "PUR" || e.EntryType == "DN") && e.EntryDate >= last6Months.First() && e.UcpId == activeUcpId)
                .GroupBy(e => new { e.EntryDate!.Value.Year, e.EntryDate!.Value.Month })
                .Select(g => new
                {
                    Key = g.Key.Year + "-" + g.Key.Month.ToString("D2"),
                    Total = g.Sum(e => e.EntryType == "PUR" ? (e.Amount ?? 0) : -(e.Amount ?? 0))
                })
                .ToDictionaryAsync(x => x.Key, x => Math.Max(0, x.Total));

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

            // 7b. Commission Monthly Chart Data (Last 6 Months)
            var monthlyCommRaw = await _context.Entries
                .Where(e => (e.EntryType == "SAL" || e.EntryType == "CN" || e.EntryType == "PUR" || e.EntryType == "DN") &&
                            e.EntryDate >= last6Months.First() && e.UcpId == activeUcpId)
                .GroupBy(e => new { e.EntryDate!.Value.Year, e.EntryDate!.Value.Month, e.EntryType })
                .Select(g => new
                {
                    Key = g.Key.Year + "-" + g.Key.Month.ToString("D2"),
                    Type = g.Key.EntryType,
                    TotalComm = g.Sum(e => e.EntryCommAmt ?? 0)
                })
                .ToListAsync();

            var commissionChartData = last6Months.Select(d =>
            {
                var key = d.Year + "-" + d.Month.ToString("D2");
                var salComm = monthlyCommRaw.Where(x => x.Key == key && x.Type == "SAL").Sum(x => x.TotalComm);
                var cnComm = monthlyCommRaw.Where(x => x.Key == key && x.Type == "CN").Sum(x => x.TotalComm);
                var purComm = monthlyCommRaw.Where(x => x.Key == key && x.Type == "PUR").Sum(x => x.TotalComm);
                var dnComm = monthlyCommRaw.Where(x => x.Key == key && x.Type == "DN").Sum(x => x.TotalComm);
                return Math.Max(0, (salComm - cnComm) + (purComm - dnComm));
            }).ToList();

            CommissionChartDataJson = System.Text.Json.JsonSerializer.Serialize(commissionChartData);

            // 7c. Top 5 Items by Stock Valuation (for Donut / Bar Chart)
            var topStockValuation = inventoryStats
                .Where(x => x.ItemId.HasValue && x.Qty > 0)
                .Select(x => new
                {
                    ItemName = itemNames.ContainsKey(x.ItemId!.Value) ? itemNames[x.ItemId.Value] ?? "Unknown" : "Unknown",
                    Value = Math.Round(x.Qty * (lastPurchaseRates.TryGetValue(x.ItemId!.Value, out var r) ? r : 0), 0)
                })
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .Take(5)
                .ToList();

            StockValuationLabelsJson = System.Text.Json.JsonSerializer.Serialize(topStockValuation.Select(x => x.ItemName).ToList());
            StockValuationDataJson = System.Text.Json.JsonSerializer.Serialize(topStockValuation.Select(x => x.Value).ToList());

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
