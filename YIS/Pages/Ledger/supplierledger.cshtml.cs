using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Helpers;
using YIS.Models;
using System.IO;
using ClosedXML.Excel;

namespace YIS.Pages.Ledger
{
    public class SupplierLedgerModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SupplierLedgerModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<SupplierMaster> Suppliers { get; set; } = new();
        public List<LedgerRow> LedgerRows { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public long? SupplierId { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            Suppliers = await _context.SupplierMasters
                .Where(s => s.UcpId == activeUcpId)
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            // Default dates
            if (!StartDate.HasValue) StartDate = DateTime.Today.AddDays(-30);
            if (!EndDate.HasValue) EndDate = DateTime.Today;

            // Initial load (optional: could be empty or load default)
            if (SupplierId.HasValue)
            {
                await LoadLedgerAsync(activeUcpId.Value);
            }

            return Page();
        }

        private async Task LoadLedgerAsync(long ucpId)
        {
            if (!SupplierId.HasValue) return;

            var supplier = await _context.SupplierMasters.FindAsync(SupplierId);
            if (supplier == null) return;

            var supIdStr = SupplierId.ToString();

            // 1. Calculate Opening Balance
            // For PUR: Show Debit (Purchase side)
            // For PV: Show Credit (Bank side)
            // Logic: Balance = Previous + Debit - Credit
            var purPre = await (from t in _context.Transactions
                                join e in _context.Entries on t.EntryId equals e.EntryId
                                where t.UcpId == ucpId && t.TransType == "PUR"
                                   && e.EntryAttribute1 == supIdStr
                                   && e.EntryDate < StartDate
                                   && (t.Debit ?? 0) > 0
                                select new { t.Debit, t.Credit }).ToListAsync();

            var pvPre = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "PV"
                                  && e.EntryAttribute2 == supIdStr
                                  && e.EntryDate < StartDate
                                  && (t.Credit ?? 0) > 0
                               select new { t.Debit, t.Credit }).ToListAsync();

            double openingBalance = purPre.Sum(x => (x.Debit ?? 0)) -
                                    pvPre.Sum(x => (x.Credit ?? 0));

            // 2. Fetch Transactions within Range (Sum up Inventory + Tax per invoice)
            var purRaw = await (from t in _context.Transactions
                                join e in _context.Entries on t.EntryId equals e.EntryId
                                where t.UcpId == ucpId && t.TransType == "PUR"
                                   && e.EntryAttribute1 == supIdStr
                                   && e.EntryDate >= StartDate && e.EntryDate <= EndDate
                                   && (t.Debit ?? 0) > 0
                                select new
                                {
                                    t.TransId,
                                    TransNo = e.EntryNumber,
                                    t.TransType,
                                    EntryRemarks = e.EntryRemarks,
                                    TransRemarks = t.Remarks,
                                    t.Debit,
                                    t.Credit,
                                    EntryDate = e.EntryDate,
                                    e.EntryId
                                }).ToListAsync();

            var purTrans = purRaw
                .GroupBy(x => x.EntryId)
                .Select(g => new
                {
                    TransId = g.Min(x => x.TransId),
                    TransNo = g.First().TransNo,
                    TransType = g.First().TransType,
                    Remarks = !string.IsNullOrEmpty(g.First().EntryRemarks)
                        ? g.First().EntryRemarks
                        : ("Purchase Invoice # PUR-" + g.First().TransNo),
                    Debit = (double?)g.Sum(x => x.Debit ?? 0),
                    Credit = (double?)0,
                    EntryDate = g.First().EntryDate
                }).ToList();

            var pvTrans = await (from t in _context.Transactions
                                 join e in _context.Entries on t.EntryId equals e.EntryId
                                 where t.UcpId == ucpId && t.TransType == "PV"
                                    && e.EntryAttribute2 == supIdStr
                                    && e.EntryDate >= StartDate && e.EntryDate <= EndDate
                                    && (t.Credit ?? 0) > 0
                                 select new
                                 {
                                     t.TransId,
                                     TransNo = e.EntryNumber,
                                     t.TransType,
                                     t.Remarks,
                                     t.Debit,
                                     t.Credit,
                                     EntryDate = e.EntryDate
                                 }).ToListAsync();

            // Combine and sort
            var transactions = purTrans.Concat(pvTrans)
                                      .OrderBy(t => t.EntryDate)
                                      .ThenBy(t => t.TransId)
                                      .ToList();

            // 3. Build Ledger Rows
            LedgerRows.Add(new LedgerRow
            {
                Date = null,
                TransNo = "",
                Type = "",
                Remarks = "Opening Balance",
                Debit = 0,
                Credit = 0,
                Balance = openingBalance
            });

            double currentBalance = openingBalance;
            string partyName = supplier.SupplierName ?? "N/A";

            foreach (var t in transactions)
            {
                double credit = t.Credit ?? 0;
                double debit = t.Debit ?? 0;
                // Supplier Ledger (per user data): Previous + Debit - Credit
                // PUR (Debit) increases balance, PV (Credit) decreases balance
                currentBalance = currentBalance + debit - credit;

                LedgerRows.Add(new LedgerRow
                {
                    Date = t.EntryDate,
                    TransNo = t.TransNo?.ToString() ?? "-",
                    Type = t.TransType ?? "-",
                    PartyNameString = partyName,
                    Remarks = t.Remarks ?? "",
                    Debit = debit,
                    Credit = credit,
                    Balance = currentBalance
                });
            }
        }

        public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || !SupplierId.HasValue) return RedirectToPage();

            // 1. Data Retrieval
            await LoadLedgerAsync(activeUcpId.Value);

            var supplier = await _context.SupplierMasters.FindAsync(SupplierId);
            string supplierName = supplier?.SupplierName ?? "Supplier";

            // 2. Excel Generation
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Supplier Ledger");

                // Header
                worksheet.Cell(1, 1).Value = "Supplier:";
                worksheet.Cell(1, 2).Value = supplierName;
                worksheet.Cell(2, 1).Value = "Period:";
                worksheet.Cell(2, 2).Value = $"{StartDate?.ToString("dd-MM-yyyy")} to {EndDate?.ToString("dd-MM-yyyy")}";

                // Table Header
                var tableHeader = worksheet.Range("A4:G4");
                tableHeader.Style.Font.Bold = true;
                tableHeader.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                worksheet.Cell(4, 1).Value = "Date";
                worksheet.Cell(4, 2).Value = "Trans No";
                worksheet.Cell(4, 3).Value = "Type";
                worksheet.Cell(4, 4).Value = "Remarks";
                worksheet.Cell(4, 5).Value = "Debit";
                worksheet.Cell(4, 6).Value = "Credit";
                worksheet.Cell(4, 7).Value = "Balance";

                // Table Rows
                int row = 5;
                foreach (var item in LedgerRows)
                {
                    worksheet.Cell(row, 1).Value = item.Date?.ToString("dd-MM-yyyy") ?? "-";
                    worksheet.Cell(row, 2).Value = item.TransNo;
                    worksheet.Cell(row, 3).Value = item.Type;
                    worksheet.Cell(row, 4).Value = item.Remarks;
                    worksheet.Cell(row, 5).Value = item.Debit;
                    worksheet.Cell(row, 6).Value = item.Credit;
                    worksheet.Cell(row, 7).Value = item.Balance;
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"SupplierLedger_{supplierName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.xlsx");
                }
            }
        }

        public class LedgerRow
        {
            public DateTime? Date { get; set; }
            public string TransNo { get; set; } = "";
            public string Type { get; set; } = "";
            public string PartyNameString { get; set; } = "";
            public string Remarks { get; set; } = "";
            public double Debit { get; set; }
            public double Credit { get; set; }
            public double Balance { get; set; }
        }
    }
}
