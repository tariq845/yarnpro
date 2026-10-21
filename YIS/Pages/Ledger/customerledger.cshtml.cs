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
    public class CustomerLedgerModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CustomerLedgerModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<CustomerMaster> Customers { get; set; } = new();
        public List<LedgerRow> LedgerRows { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public long? CustomerId { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            Customers = await _context.CustomerMasters
                .Where(c => c.UcpId == activeUcpId)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            // Default dates
            if (!StartDate.HasValue) StartDate = DateTime.Today.AddDays(-30);
            if (!EndDate.HasValue) EndDate = DateTime.Today;

            // Initial full page load
            if (CustomerId.HasValue)
            {
                await LoadLedgerAsync(activeUcpId.Value);
            }

            return Page();
        }

        private async Task LoadLedgerAsync(long ucpId)
        {
            if (!CustomerId.HasValue) return;

            var customer = await _context.CustomerMasters.FindAsync(CustomerId);
            if (customer == null) return;

            string customerIdStr = CustomerId.Value.ToString();

            // 1. Calculate Opening Balance (before StartDate)
            // Customer is an Asset (Receivable): Debit increases, Credit decreases
            // Balance = Debit - Credit
            var salPre = await (from t in _context.Transactions
                                join e in _context.Entries on t.EntryId equals e.EntryId
                                where t.UcpId == ucpId && t.TransType == "SAL"
                                   && e.EntryAttribute1 == customerIdStr
                                   && e.EntryDate < StartDate
                                select new { e.EntryId, t.Debit, t.Credit }).ToListAsync();

            double salPreTotal = salPre
                .GroupBy(x => x.EntryId)
                .Sum(g => g.Any(x => (x.Debit ?? 0) > 0)
                    ? g.Where(x => (x.Debit ?? 0) > 0).Sum(x => x.Debit ?? 0)
                    : g.Sum(x => x.Credit ?? 0));

            var rvPre = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "RV"
                                  && e.EntryAttribute2 == customerIdStr
                                  && e.EntryDate < StartDate
                               select new { e.EntryId, t.Debit, t.Credit }).ToListAsync();

            double rvPreTotal = rvPre
                .GroupBy(x => x.EntryId)
                .Sum(g => g.Any(x => (x.Credit ?? 0) > 0)
                    ? g.Where(x => (x.Credit ?? 0) > 0).Sum(x => x.Credit ?? 0)
                    : g.Sum(x => x.Debit ?? 0));

            double openingBalance = salPreTotal - rvPreTotal;

            // 2. Fetch Transactions within Range (Sum up Sales Revenue + Tax per invoice)
            var salRaw = await (from t in _context.Transactions
                                join e in _context.Entries on t.EntryId equals e.EntryId
                                where t.UcpId == ucpId && t.TransType == "SAL"
                                   && e.EntryAttribute1 == customerIdStr
                                   && e.EntryDate >= StartDate && e.EntryDate <= EndDate
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

            var salTrans = salRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    double totalInvoiceAmt = g.Any(x => (x.Debit ?? 0) > 0)
                        ? g.Where(x => (x.Debit ?? 0) > 0).Sum(x => x.Debit ?? 0)
                        : g.Sum(x => x.Credit ?? 0);

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = g.First().TransNo,
                        TransType = g.First().TransType,
                        Remarks = !string.IsNullOrEmpty(g.First().EntryRemarks)
                            ? g.First().EntryRemarks
                            : ("Sales Invoice # SAL-" + g.First().TransNo),
                        Debit = (double?)totalInvoiceAmt,
                        Credit = (double?)0,
                        EntryDate = g.First().EntryDate
                    };
                }).ToList();

            var rvRaw = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "RV"
                                  && e.EntryAttribute2 == customerIdStr
                                  && e.EntryDate >= StartDate && e.EntryDate <= EndDate
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

            var rvTrans = rvRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    double totalReceiptAmt = g.Any(x => (x.Credit ?? 0) > 0)
                        ? g.Where(x => (x.Credit ?? 0) > 0).Sum(x => x.Credit ?? 0)
                        : g.Sum(x => x.Debit ?? 0);

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = g.First().TransNo,
                        TransType = g.First().TransType,
                        Remarks = !string.IsNullOrEmpty(g.First().EntryRemarks)
                            ? g.First().EntryRemarks
                            : ("Receipt Voucher # RV-" + g.First().TransNo),
                        Debit = (double?)0,
                        Credit = (double?)totalReceiptAmt,
                        EntryDate = g.First().EntryDate
                    };
                }).ToList();

            // Combine and sort
            var transactions = salTrans.Concat(rvTrans)
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
            string partyName = customer.CustomerName ?? "N/A";

            foreach (var t in transactions)
            {
                double debit = t.Debit ?? 0;
                double credit = t.Credit ?? 0;
                // Customer Balance (Receivable): Previous + Debit - Credit
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
            if (!activeUcpId.HasValue || !CustomerId.HasValue) return RedirectToPage();

            // 1. Data Retrieval (Reusing logic from LoadLedgerAsync)
            await LoadLedgerAsync(activeUcpId.Value);

            var customer = await _context.CustomerMasters.FindAsync(CustomerId);
            string customerName = customer?.CustomerName ?? "Customer";

            // 2. Excel Generation
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Customer Ledger");

                // Header
                worksheet.Cell(1, 1).Value = "Customer:";
                worksheet.Cell(1, 2).Value = customerName;
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

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"CustomerLedger_{customerName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.xlsx");
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
