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
    public class AccountLedgerModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public AccountLedgerModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<GeneralLedgerRow> LedgerRows { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public long? AccountId { get; set; }

        public List<AccountMaster> Accounts { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        public double TotalDebit { get; set; }
        public double TotalCredit { get; set; }
        public double ClosingBalance { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            Accounts = await _context.AccountMasters
                .Where(a => a.UcpId == activeUcpId)
                .OrderBy(a => a.AccountName)
                .ToListAsync();

            if (!StartDate.HasValue) StartDate = DateTime.Today;
            if (!EndDate.HasValue) EndDate = DateTime.Today;

            if (AccountId.HasValue)
            {
                await LoadLedgerAsync(activeUcpId.Value);
            }

            return Page();
        }

        private async Task LoadLedgerAsync(long ucpId)
        {
            // 1. Calculate Opening Balance (Net Debit - Credit before Start Date)
            var preQuery = from t in _context.Transactions
                           join e in _context.Entries on t.EntryId equals e.EntryId into entryGroup
                           from entry in entryGroup.DefaultIfEmpty()
                           where t.UcpId == ucpId
                           select new
                           {
                               t.AccountId,
                               t.Debit,
                               t.Credit,
                               EntryDate = entry != null ? entry.EntryDate : (DateTime?)null
                           };

            if (AccountId.HasValue)
            {
                preQuery = preQuery.Where(x => x.AccountId == AccountId.Value);
            }

            // Filter by date < StartDate
            // Note: If EntryDate is null, we assume it's NOT before StartDate unless we have other logic.
            // For now, only include valid dates.
            var preTransactions = await preQuery
                .Where(x => x.EntryDate < StartDate)
                .ToListAsync();

            double openingBalance = preTransactions.Sum(x => (x.Debit ?? 0) - (x.Credit ?? 0));

            // 2. Fetch Transactions within Range
            var query = from t in _context.Transactions
                        join a in _context.AccountMasters on t.AccountId equals a.AccountId
                        join e in _context.Entries on t.EntryId equals e.EntryId into entryGroup
                        from entry in entryGroup.DefaultIfEmpty()
                        where t.UcpId == ucpId
                        select new
                        {
                            t.TransId,
                            TransNo = entry != null ? entry.EntryNumber : t.TransNo,
                            t.TransType,
                            t.AccountId,
                            t.Remarks,
                            t.Debit,
                            t.Credit,
                            AccountName = a.AccountName,
                            EntryDate = entry != null ? entry.EntryDate : (DateTime?)null
                        };

            if (AccountId.HasValue)
            {
                query = query.Where(x => x.AccountId == AccountId.Value);
            }

            if (StartDate.HasValue)
            {
                query = query.Where(x => x.EntryDate >= StartDate.Value);
            }
            if (EndDate.HasValue)
            {
                query = query.Where(x => x.EntryDate <= EndDate.Value);
            }

            var transactions = await query
                .OrderBy(x => x.EntryDate)
                .ThenBy(x => x.TransNo)
                .ThenBy(x => x.TransId)
                .ToListAsync();

            // Add Opening Balance Row
            LedgerRows.Add(new GeneralLedgerRow
            {
                Date = null,
                TransNo = "",
                Type = "",
                AccountName = "Opening Balance",
                Remarks = "B/F",
                Debit = 0,
                Credit = 0,
                Balance = openingBalance
            });

            double currentBalance = openingBalance;
            TotalDebit = 0;
            TotalCredit = 0;

            foreach (var t in transactions)
            {
                double debit = t.Debit ?? 0;
                double credit = t.Credit ?? 0;
                currentBalance = currentBalance + debit - credit;

                TotalDebit += debit;
                TotalCredit += credit;

                LedgerRows.Add(new GeneralLedgerRow
                {
                    Date = t.EntryDate,
                    TransNo = t.TransNo?.ToString() ?? "-",
                    Type = t.TransType ?? "-",
                    AccountName = t.AccountName ?? "-",
                    Remarks = t.Remarks ?? "",
                    Debit = debit,
                    Credit = credit,
                    Balance = currentBalance
                });
            }
            ClosingBalance = currentBalance;
        }

        public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage();

            // 1. Data Retrieval
            await LoadLedgerAsync(activeUcpId.Value);

            string accountNameString = "General Ledger";
            if (AccountId.HasValue)
            {
                var account = await _context.AccountMasters.FindAsync(AccountId);
                accountNameString = account?.AccountName ?? "Account";
            }

            // 2. Excel Generation
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("General Ledger");

                // Header
                worksheet.Cell(1, 1).Value = "Account:";
                worksheet.Cell(1, 2).Value = accountNameString;
                worksheet.Cell(2, 1).Value = "Period:";
                worksheet.Cell(2, 2).Value = $"{StartDate?.ToShortDateString()} to {EndDate?.ToShortDateString()}";

                // Table Header
                var tableHeader = worksheet.Range("A4:H4");
                tableHeader.Style.Font.Bold = true;
                tableHeader.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                worksheet.Cell(4, 1).Value = "Date";
                worksheet.Cell(4, 2).Value = "Trans No";
                worksheet.Cell(4, 3).Value = "Type";
                worksheet.Cell(4, 4).Value = "Account";
                worksheet.Cell(4, 5).Value = "Remarks";
                worksheet.Cell(4, 6).Value = "Debit";
                worksheet.Cell(4, 7).Value = "Credit";
                worksheet.Cell(4, 8).Value = "Balance";

                // Table Rows
                int row = 5;
                foreach (var item in LedgerRows)
                {
                    worksheet.Cell(row, 1).Value = item.Date?.ToString("dd-MM-yyyy") ?? "-";
                    worksheet.Cell(row, 2).Value = item.TransNo;
                    worksheet.Cell(row, 3).Value = item.Type;
                    worksheet.Cell(row, 4).Value = item.AccountName;
                    worksheet.Cell(row, 5).Value = item.Remarks;
                    worksheet.Cell(row, 6).Value = item.Debit;
                    worksheet.Cell(row, 7).Value = item.Credit;
                    worksheet.Cell(row, 8).Value = item.Balance;
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"AccountLedger_{accountNameString.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.xlsx");
                }
            }
        }

        public class GeneralLedgerRow
        {
            public DateTime? Date { get; set; }
            public string TransNo { get; set; } = "";
            public string Type { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string Remarks { get; set; } = "";
            public double Debit { get; set; }
            public double Credit { get; set; }
            public double Balance { get; set; }
        }
    }
}
