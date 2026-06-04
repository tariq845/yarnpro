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

            // Try AccountId first; if null, fallback to matching by name
            long? accountId = customer.AccountId;
            if (accountId == null && !string.IsNullOrEmpty(customer.CustomerName))
            {
                var matchedAccount = await _context.AccountMasters
                    .FirstOrDefaultAsync(a => a.AccountName == customer.CustomerName && a.UcpId == ucpId);
                accountId = matchedAccount?.AccountId;
            }

            if (accountId == null) return;

            // Collect all ENTRY IDs that belong to THIS specific customer:
            //   SAL entries: EntryAttribute1 = CustomerId (string)
            //   RV  entries: EntryAttribute2 = CustomerId (string)
            var customerEntryIds = await _context.Entries
                .Where(e => e.UcpId == ucpId &&
                            ((e.EntryType == "SAL" && e.EntryAttribute1 == customerIdStr) ||
                             (e.EntryType == "RV" && e.EntryAttribute2 == customerIdStr)))
                .Select(e => e.EntryId)
                .ToListAsync();

            // 1. Opening Balance (before StartDate)
            // Customer is an Asset (Receivable): Debit increases, Credit decreases
            // Balance = Debit - Credit

            // All transactions for this customer's account before StartDate,
            // limited to entries that belong to this customer
            var preTrans = await (from t in _context.Transactions
                                  join e in _context.Entries on t.EntryId equals e.EntryId
                                  where t.UcpId == ucpId
                                     && t.AccountId == accountId
                                     && customerEntryIds.Contains(t.EntryId ?? 0)
                                     && e.EntryDate < StartDate
                                  select new { t.Debit, t.Credit }).ToListAsync();

            double openingBalance = preTrans.Sum(x => (x.Debit ?? 0)) -
                                    preTrans.Sum(x => (x.Credit ?? 0));

            // 2. Transactions within date range
            var transactionsData = await (from t in _context.Transactions
                                          join e in _context.Entries on t.EntryId equals e.EntryId
                                          where t.UcpId == ucpId
                                             && t.AccountId == accountId
                                             && customerEntryIds.Contains(t.EntryId ?? 0)
                                             && e.EntryDate >= StartDate && e.EntryDate <= EndDate
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

            var transactions = transactionsData
                                     .OrderBy(x => x.EntryDate)
                                     .ThenBy(x => x.TransId)
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
                worksheet.Cell(2, 2).Value = $"{StartDate?.ToShortDateString()} to {EndDate?.ToShortDateString()}";

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
                    worksheet.Cell(row, 1).Value = item.Date?.ToShortDateString() ?? "-";
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
