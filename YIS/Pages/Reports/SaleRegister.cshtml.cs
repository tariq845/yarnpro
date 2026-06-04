using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class SaleRegisterModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SaleRegisterModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<SaleRecordViewModel> Records { get; set; } = new List<SaleRecordViewModel>();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        public class SaleRecordViewModel
        {
            public long LineId { get; set; }
            public string? Type { get; set; } // SAL or CN
            public string? SaleNo { get; set; }
            public DateTime? SaleDate { get; set; }
            public string? AgainstSsdNo { get; set; }
            public string? AgainstSaleNo { get; set; } // Linked SAL for CN
            public string? CustomerId { get; set; }
            public string? CustomerName { get; set; }
            public string? CustomerReference { get; set; } // Maybe EntryRefCode or similar
            public string? CommissionApplicable { get; set; }
            public string? CommissionAgent { get; set; }
            public double? CommissionPer { get; set; }
            public double? CommissionAmt { get; set; }
            public double? Sukri { get; set; }
            public int? PaymentTerms { get; set; }
            public string? Remarks { get; set; }
            public long? ItemId { get; set; }
            public string? ItemName { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPercent { get; set; }
            public double? GstAmount { get; set; }
            public double? NetAmount { get; set; }
        }

        public async Task OnGetAsync()
        {
            if (FromDate == null) FromDate = new DateTime(2020, 1, 1);
            if (ToDate == null) ToDate = DateTime.Now;

            Records = await GetSaleRecordsListAsync();
        }

        public async Task<IActionResult> OnGetDownloadAsync()
        {
            if (FromDate == null) FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (ToDate == null) ToDate = DateTime.Now;

            var records = await GetSaleRecordsListAsync();

            var csv = new StringBuilder();
            csv.AppendLine("ID,Type,Sale No,Date,Against SODA,Against Sale,Customer ID,Customer Name,Customer Reference,Comm. Applicable,Comm. Agent,Comm. %,Comm. Amount,SUKRI,Payment Terms,Remarks,Item ID,Item Name,Quantity,LBS,Rate,Amount,GST %,GST Amount,Net Amount");

            foreach (var r in records)
            {
                csv.AppendLine($"{r.LineId},{r.Type},{r.SaleNo},{r.SaleDate:yyyy-MM-dd},{r.AgainstSsdNo},{r.AgainstSaleNo},{r.CustomerId},\"{r.CustomerName}\",{r.CustomerReference},{r.CommissionApplicable},\"{r.CommissionAgent}\",{r.CommissionPer},{r.CommissionAmt},{r.Sukri},{r.PaymentTerms},\"{r.Remarks}\",{r.ItemId},\"{r.ItemName}\",{r.Qty},{r.Lbs},{r.Rate},{r.Amount},{r.GstPercent},{r.GstAmount},{r.NetAmount}");
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"SaleRegister_{DateTime.Now:yyyyMMdd}.csv");
        }

        private async Task<List<SaleRecordViewModel>> GetSaleRecordsListAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // 1. Fetch ALL entries first (simplest SQL possible) - Filtered by Company
            var allEntries = await _context.Entries
                .Where(e => e.UcpId == activeUcpId)
                .AsNoTracking()
                .ToListAsync();

            // 2. Filter and join in memory
            var data = (from header in allEntries
                        join line in allEntries on header.EntryId equals line.EntryRefId
                        where ((header.EntryType == "SAL" && line.EntryType == "SALL") ||
                               (header.EntryType == "CN" && line.EntryType == "CNL"))
                        && (!FromDate.HasValue || header.EntryDate >= FromDate.Value.Date)
                        && (!ToDate.HasValue || header.EntryDate <= ToDate.Value.Date)
                        select new { header, line })
                        .OrderByDescending(x => x.header.EntryDate)
                        .ThenByDescending(x => x.header.EntryNumber)
                        .ToList();

            if (!data.Any()) return new List<SaleRecordViewModel>();

            // 3. Fetch metadata using simple queries
            var customerIds = data.Select(x => x.header.EntryAttribute1)
                .Where(id => !string.IsNullOrEmpty(id) && long.TryParse(id, out _))
                .Select(id => long.Parse(id!))
                .Distinct()
                .ToList();

            var allCustomers = customerIds.Any()
                ? await _context.CustomerMasters.AsNoTracking().ToListAsync()
                : new List<CustomerMaster>();
            var customers = allCustomers
                .Where(c => customerIds.Contains(c.CustomerId))
                .ToDictionary(c => c.CustomerId.ToString(), c => c.CustomerName ?? "");

            var itemIds = data.Select(x => x.line.ItemId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var allItems = itemIds.Any()
                ? await _context.ItemMasters.AsNoTracking().ToListAsync()
                : new List<ItemMaster>();
            var items = allItems
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionary(i => i.ItemId, i => i.ItemName ?? "");

            var ssdIds = data.Where(x => x.header.EntryType == "SAL").Select(x => x.header.EntryRefNo).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var salIds = data.Where(x => x.header.EntryType == "CN").Select(x => x.header.EntryRefNo).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

            var refIds = ssdIds.Concat(salIds).Distinct().ToList();
            var refEntries = allEntries.Where(e => refIds.Contains(e.EntryId)).ToList();
            var refs = refEntries.ToDictionary(e => e.EntryId, e => e.EntryNumber);

            // 4. Transform to ViewModel
            var results = data.Select(x =>
            {
                string custName = (x.header.EntryAttribute1 != null && customers.ContainsKey(x.header.EntryAttribute1)) ? customers[x.header.EntryAttribute1] : "";
                string itemName = (x.line.ItemId.HasValue && items.ContainsKey(x.line.ItemId.Value)) ? items[x.line.ItemId.Value] : "";
                long? refNum = (x.header.EntryRefNo.HasValue && refs.ContainsKey(x.header.EntryRefNo.Value)) ? refs[x.header.EntryRefNo.Value] : null;

                return new SaleRecordViewModel
                {
                    LineId = x.line.EntryId,
                    Type = x.header.EntryType == "CN" ? "SRN" : "SAL",
                    SaleNo = (x.header.EntryType == "CN" ? "SRN-" : "SAL-") + x.header.EntryNumber,
                    SaleDate = x.header.EntryDate,
                    AgainstSsdNo = (x.header.EntryType == "SAL" && refNum.HasValue) ? "SSD-" + refNum : "",
                    AgainstSaleNo = (x.header.EntryType == "CN" && refNum.HasValue) ? "SAL-" + refNum : "",
                    CustomerId = x.header.EntryAttribute1,
                    CustomerName = custName,
                    CustomerReference = x.header.EntryRefCode?.ToString(),
                    CommissionApplicable = x.header.EntryAttribute5,
                    CommissionAgent = x.header.EntryAttribute4,
                    CommissionPer = x.header.EntryCommPer,
                    CommissionAmt = x.header.EntryCommAmt,
                    Sukri = x.header.EntryCommSukri,
                    PaymentTerms = x.header.EntryTermsDays,
                    Remarks = x.header.EntryRemarks,
                    ItemId = x.line.ItemId,
                    ItemName = itemName,
                    Qty = x.line.Qty,
                    Lbs = x.line.Lbs,
                    Rate = x.line.Rate,
                    Amount = x.line.Amount,
                    GstPercent = double.TryParse(x.line.EntryAttribute2, out var gp) ? gp : 0,
                    GstAmount = x.line.TaxAmount,
                    NetAmount = (x.line.Amount ?? 0) + (x.line.TaxAmount ?? 0)
                };
            }).ToList();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                results = results.Where(r =>
                    (r.SaleNo != null && r.SaleNo.ToLower().Contains(term)) ||
                    (r.CustomerName != null && r.CustomerName.ToLower().Contains(term)) ||
                    (r.ItemName != null && r.ItemName.ToLower().Contains(term)) ||
                    (r.Remarks != null && r.Remarks.ToLower().Contains(term))
                ).ToList();
            }

            return results;
        }
    }
}
