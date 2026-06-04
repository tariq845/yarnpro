using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class PurchaseRegisterModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PurchaseRegisterModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<PurchaseRecordViewModel> Records { get; set; } = new List<PurchaseRecordViewModel>();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        public class PurchaseRecordViewModel
        {
            public long LineId { get; set; }
            public string? Type { get; set; } // PUR or DN
            public string? PurchaseNo { get; set; }
            public DateTime? PurchaseDate { get; set; }
            public string? AgainstPsdNo { get; set; }
            public string? AgainstPurchaseNo { get; set; } // Linked PUR for DN
            public string? SupplierId { get; set; }
            public string? SupplierName { get; set; }
            public string? SupplierPurchaseNo { get; set; }
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

            Records = await GetPurchaseRecordsListAsync();
        }

        public async Task<IActionResult> OnGetDownloadAsync()
        {
            if (FromDate == null) FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (ToDate == null) ToDate = DateTime.Now;

            var records = await GetPurchaseRecordsListAsync();

            var csv = new StringBuilder();
            csv.AppendLine("ID,Type,Purchase No,Date,Against SODA,Against Purchase,Supplier ID,Supplier Name,Supplier Purchase No,Comm. Applicable,Comm. Agent,Comm. %,Comm. Amount,SUKRI,Payment Terms,Remarks,Item ID,Item Name,Quantity,LBS,Rate,Amount,GST %,GST Amount,Net Amount");

            foreach (var r in records)
            {
                csv.AppendLine($"{r.LineId},{r.Type},{r.PurchaseNo},{r.PurchaseDate:yyyy-MM-dd},{r.AgainstPsdNo},{r.AgainstPurchaseNo},{r.SupplierId},\"{r.SupplierName}\",{r.SupplierPurchaseNo},{r.CommissionApplicable},\"{r.CommissionAgent}\",{r.CommissionPer},{r.CommissionAmt},{r.Sukri},{r.PaymentTerms},\"{r.Remarks}\",{r.ItemId},\"{r.ItemName}\",{r.Qty},{r.Lbs},{r.Rate},{r.Amount},{r.GstPercent},{r.GstAmount},{r.NetAmount}");
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"PurchaseRegister_{DateTime.Now:yyyyMMdd}.csv");
        }

        private async Task<List<PurchaseRecordViewModel>> GetPurchaseRecordsListAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // 1. Fetch ALL entries first - Filtered by Company
            var allEntries = await _context.Entries
                .Where(e => e.UcpId == activeUcpId)
                .AsNoTracking()
                .ToListAsync();

            // 2. Filter and join in memory
            var data = (from header in allEntries
                        join line in allEntries on header.EntryId equals line.EntryRefId
                        where ((header.EntryType == "PUR" && line.EntryType == "PURL") ||
                               (header.EntryType == "DN" && line.EntryType == "DNL"))
                        && (!FromDate.HasValue || header.EntryDate >= FromDate.Value.Date)
                        && (!ToDate.HasValue || header.EntryDate <= ToDate.Value.Date)
                        select new { header, line })
                        .OrderByDescending(x => x.header.EntryDate)
                        .ThenByDescending(x => x.header.EntryNumber)
                        .ToList();

            if (!data.Any()) return new List<PurchaseRecordViewModel>();

            // 3. Fetch metadata using simple queries
            var supplierIds = data.Select(x => x.header.EntryAttribute1)
                .Where(id => !string.IsNullOrEmpty(id) && long.TryParse(id, out _))
                .Select(id => long.Parse(id!))
                .Distinct()
                .ToList();

            var allSuppliers = supplierIds.Any()
                ? await _context.SupplierMasters.AsNoTracking().ToListAsync()
                : new List<SupplierMaster>();
            var suppliers = allSuppliers
                .Where(s => supplierIds.Contains(s.SupplierId))
                .ToDictionary(s => s.SupplierId.ToString(), s => s.SupplierName ?? "");

            var itemIds = data.Select(x => x.line.ItemId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var allItems = itemIds.Any()
                ? await _context.ItemMasters.AsNoTracking().ToListAsync()
                : new List<ItemMaster>();
            var items = allItems
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionary(i => i.ItemId, i => i.ItemName ?? "");

            var psdIds = data.Select(x => x.header.EntryRefNo).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var psdEntries = allEntries.Where(e => psdIds.Contains(e.EntryId)).ToList();
            var psds = psdEntries.ToDictionary(e => e.EntryId, e => e.EntryNumber);

            // 4. Transform to ViewModel
            var results = data.Select(x =>
            {
                string supName = (x.header.EntryAttribute1 != null && suppliers.ContainsKey(x.header.EntryAttribute1)) ? suppliers[x.header.EntryAttribute1] : "";
                string itemName = (x.line.ItemId.HasValue && items.ContainsKey(x.line.ItemId.Value)) ? items[x.line.ItemId.Value] : "";
                long? psdNum = (x.header.EntryRefNo.HasValue && psds.ContainsKey(x.header.EntryRefNo.Value)) ? psds[x.header.EntryRefNo.Value] : null;

                return new PurchaseRecordViewModel
                {
                    LineId = x.line.EntryId,
                    Type = x.header.EntryType == "DN" ? "PRN" : "PUR",
                    PurchaseNo = (x.header.EntryType == "DN" ? "PRN-" : "PUR-") + x.header.EntryNumber,
                    PurchaseDate = x.header.EntryDate,
                    AgainstPsdNo = psdNum.HasValue ? "PSD-" + psdNum : "",
                    AgainstPurchaseNo = x.header.EntryType == "DN" && x.header.EntryRefNo.HasValue ? "PUR-" + x.header.EntryRefNo : "",
                    SupplierId = x.header.EntryAttribute1,
                    SupplierName = supName,
                    SupplierPurchaseNo = x.header.EntryRefCode?.ToString(),
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
                    (r.PurchaseNo != null && r.PurchaseNo.ToLower().Contains(term)) ||
                    (r.SupplierName != null && r.SupplierName.ToLower().Contains(term)) ||
                    (r.ItemName != null && r.ItemName.ToLower().Contains(term)) ||
                    (r.Remarks != null && r.Remarks.ToLower().Contains(term))
                ).ToList();
            }

            return results;
        }
    }
}
