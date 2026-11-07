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

            var cnPre = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "CN"
                                  && (e.EntryAttribute1 == customerIdStr || e.EntryAttribute2 == customerIdStr)
                                  && e.EntryDate < StartDate
                               select new { e.EntryId, t.Debit, t.Credit }).ToListAsync();

            double cnPreTotal = cnPre
                .GroupBy(x => x.EntryId)
                .Sum(g => g.Any(x => (x.Credit ?? 0) > 0)
                    ? g.Where(x => (x.Credit ?? 0) > 0).Sum(x => x.Credit ?? 0)
                    : g.Sum(x => x.Debit ?? 0));

            double openingBalance = salPreTotal - rvPreTotal - cnPreTotal;

            // 2. Fetch Transactions within Range
            // 2a. Sales Invoices
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
                                    e.EntryId,
                                    e.ItemId,
                                    e.Qty,
                                    e.Lbs,
                                    e.Rate,
                                    e.EntryRefId,
                                    e.EntryRefNo
                                }).ToListAsync();

            // 2b. Receipt Vouchers
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
                                   e.EntryId,
                                   e.ItemId,
                                   e.Qty,
                                   e.Lbs,
                                   e.Rate,
                                   e.EntryRefId,
                                   e.EntryRefNo
                               }).ToListAsync();

            // 2c. Credit Notes / Sales Returns
            var cnRaw = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "CN"
                                  && (e.EntryAttribute1 == customerIdStr || e.EntryAttribute2 == customerIdStr)
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
                                   e.EntryId,
                                   e.ItemId,
                                   e.Qty,
                                   e.Lbs,
                                   e.Rate,
                                   e.EntryRefId,
                                   e.EntryRefNo
                               }).ToListAsync();

            // Collect Entry IDs to load child line items and Soda (contracts)
            var salEntryIds = salRaw.Select(x => x.EntryId).Distinct().ToList();
            var cnEntryIds = cnRaw.Select(x => x.EntryId).Distinct().ToList();
            var parentEntryIds = salEntryIds.Concat(cnEntryIds).Distinct().ToList();

            var childLines = await _context.Entries
                .Where(e => (e.EntryType == "SALL" || e.EntryType == "CNL") && parentEntryIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            // Item IDs for mapping
            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(salRaw.Where(s => s.ItemId.HasValue).Select(s => s.ItemId!.Value))
                .Concat(cnRaw.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value))
                .Distinct().ToList();

            var itemMap = await _context.ItemMasters
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "");

            // Soda (SSD Contracts) lookups
            var ssdRefIds = salRaw.Where(s => s.EntryRefId.HasValue).Select(s => s.EntryRefId!.Value).Distinct().ToList();
            var ssdRefNos = salRaw.Where(s => s.EntryRefNo.HasValue).Select(s => s.EntryRefNo!.Value).Distinct().ToList();

            var ssdEntries = await _context.Entries
                .Where(e => e.EntryType == "SSD" && (e.UcpId == ucpId || e.UcpId == null) && (ssdRefIds.Contains(e.EntryId) || ssdRefNos.Contains(e.EntryNumber ?? 0) || ssdRefNos.Contains(e.EntryId)))
                .ToListAsync();

            var ssdById = ssdEntries
                .GroupBy(e => e.EntryId)
                .ToDictionary(g => g.Key, g => g.First());

            var ssdByNumber = ssdEntries
                .Where(e => e.EntryNumber.HasValue)
                .GroupBy(e => e.EntryNumber!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // 3. Process SAL Transactions
            var salTrans = salRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    var first = g.First();
                    double totalInvoiceAmt = g.Any(x => (x.Debit ?? 0) > 0)
                        ? g.Where(x => (x.Debit ?? 0) > 0).Sum(x => x.Debit ?? 0)
                        : g.Sum(x => x.Credit ?? 0);

                    // Find SSD contract
                    Entry? ssd = null;
                    if (first.EntryRefId.HasValue && ssdById.TryGetValue(first.EntryRefId.Value, out var s1))
                        ssd = s1;
                    else if (first.EntryRefNo.HasValue && ssdById.TryGetValue(first.EntryRefNo.Value, out var s2))
                        ssd = s2;
                    else if (first.EntryRefNo.HasValue && ssdByNumber.TryGetValue(first.EntryRefNo.Value, out var s3))
                        ssd = s3;

                    string? sodaNo = null;
                    long? sodaEntryId = null;
                    string? sodaUrl = null;
                    if (ssd != null)
                    {
                        sodaNo = $"SSD-{ssd.EntryNumber}";
                        sodaEntryId = ssd.EntryId;
                        sodaUrl = $"/Sales/Soda?id={ssd.EntryId}";
                    }
                    else if (first.EntryRefNo.HasValue && first.EntryRefNo > 0)
                    {
                        sodaNo = $"SSD-{first.EntryRefNo}";
                        sodaUrl = $"/Sales/Soda?id={first.EntryRefNo}";
                    }

                    // Line Items
                    var lines = childLines.Where(c => c.EntryRefId == g.Key).ToList();
                    var items = new List<LedgerItemDetail>();
                    if (lines.Any())
                    {
                        foreach (var l in lines)
                        {
                            string name = l.ItemId.HasValue && itemMap.ContainsKey(l.ItemId.Value)
                                ? itemMap[l.ItemId.Value]
                                : (!string.IsNullOrWhiteSpace(l.EntryRemarks) ? l.EntryRemarks : "Item");

                            items.Add(new LedgerItemDetail
                            {
                                ItemName = name,
                                Qty = l.Qty,
                                Lbs = l.Lbs,
                                Rate = l.Rate
                            });
                        }
                    }
                    else if (first.ItemId.HasValue || (first.Qty ?? 0) > 0 || (first.Rate ?? 0) > 0)
                    {
                        string name = first.ItemId.HasValue && itemMap.ContainsKey(first.ItemId.Value)
                            ? itemMap[first.ItemId.Value]
                            : "Item";

                        items.Add(new LedgerItemDetail
                        {
                            ItemName = name,
                            Qty = first.Qty,
                            Lbs = first.Lbs,
                            Rate = first.Rate
                        });
                    }

                    // Build compiled plain text narration for Excel and tooltips
                    var itemParts = new List<string>();
                    foreach (var itm in items)
                    {
                        var sub = new List<string>();
                        if (itm.Qty.HasValue && itm.Qty > 0) sub.Add($"{itm.Qty:N0} Bags");
                        if (itm.Lbs.HasValue && itm.Lbs > 0) sub.Add($"{itm.Lbs:N2} Lbs");
                        if (itm.Rate.HasValue && itm.Rate > 0) sub.Add($"@{itm.Rate:N2}");
                        string p = itm.ItemName;
                        if (sub.Any()) p += $" ({string.Join(", ", sub)})";
                        itemParts.Add(p);
                    }

                    string narration = string.Join("; ", itemParts);
                    if (!string.IsNullOrEmpty(sodaNo))
                    {
                        narration = string.IsNullOrEmpty(narration) ? $"Soda: {sodaNo}" : $"{narration} | Soda: {sodaNo}";
                    }
                    if (!string.IsNullOrWhiteSpace(first.EntryRemarks) && first.EntryRemarks != "Sales Invoice")
                    {
                        narration = string.IsNullOrEmpty(narration) ? first.EntryRemarks : $"{narration} - {first.EntryRemarks}";
                    }
                    if (string.IsNullOrEmpty(narration))
                    {
                        narration = $"Sales Invoice # SAL-{first.TransNo}";
                    }

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = first.TransNo?.ToString() ?? "-",
                        TransType = first.TransType ?? "SAL",
                        EntryId = (long?)first.EntryId,
                        TransactionUrl = $"/Sales/Entry?id={first.EntryId}",
                        InvoiceViewUrl = $"/Sales/InvoiceView/{first.EntryId}",
                        Remarks = narration,
                        OriginalRemarks = first.EntryRemarks,
                        SodaNo = sodaNo,
                        SodaEntryId = sodaEntryId,
                        SodaUrl = sodaUrl,
                        Items = items,
                        Debit = (double?)totalInvoiceAmt,
                        Credit = (double?)0,
                        EntryDate = first.EntryDate
                    };
                }).ToList();

            // 4. Process RV Transactions
            var rvTrans = rvRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    var first = g.First();
                    double totalReceiptAmt = g.Any(x => (x.Credit ?? 0) > 0)
                        ? g.Where(x => (x.Credit ?? 0) > 0).Sum(x => x.Credit ?? 0)
                        : g.Sum(x => x.Debit ?? 0);

                    string narration = !string.IsNullOrEmpty(first.EntryRemarks)
                        ? first.EntryRemarks
                        : (!string.IsNullOrEmpty(first.TransRemarks) ? first.TransRemarks : $"Receipt Voucher # RV-{first.TransNo}");

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = first.TransNo?.ToString() ?? "-",
                        TransType = first.TransType ?? "RV",
                        EntryId = (long?)first.EntryId,
                        TransactionUrl = $"/Vouchers/ReceiptVoucher?id={first.EntryId}",
                        InvoiceViewUrl = (string?)null,
                        Remarks = narration,
                        OriginalRemarks = first.EntryRemarks,
                        SodaNo = (string?)null,
                        SodaEntryId = (long?)null,
                        SodaUrl = (string?)null,
                        Items = new List<LedgerItemDetail>(),
                        Debit = (double?)0,
                        Credit = (double?)totalReceiptAmt,
                        EntryDate = first.EntryDate
                    };
                }).ToList();

            // 5. Process CN Transactions
            var cnTrans = cnRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    var first = g.First();
                    double totalReturnAmt = g.Any(x => (x.Credit ?? 0) > 0)
                        ? g.Where(x => (x.Credit ?? 0) > 0).Sum(x => x.Credit ?? 0)
                        : g.Sum(x => x.Debit ?? 0);

                    var lines = childLines.Where(c => c.EntryRefId == g.Key).ToList();
                    var items = new List<LedgerItemDetail>();
                    if (lines.Any())
                    {
                        foreach (var l in lines)
                        {
                            string name = l.ItemId.HasValue && itemMap.ContainsKey(l.ItemId.Value)
                                ? itemMap[l.ItemId.Value]
                                : "Item";

                            items.Add(new LedgerItemDetail
                            {
                                ItemName = name,
                                Qty = l.Qty,
                                Lbs = l.Lbs,
                                Rate = l.Rate
                            });
                        }
                    }

                    string narration = !string.IsNullOrEmpty(first.EntryRemarks)
                        ? first.EntryRemarks
                        : $"Credit Note (Sales Return) # CN-{first.TransNo}";

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = first.TransNo?.ToString() ?? "-",
                        TransType = first.TransType ?? "CN",
                        EntryId = (long?)first.EntryId,
                        TransactionUrl = $"/Sales/SaleReturn?id={first.EntryId}",
                        InvoiceViewUrl = (string?)null,
                        Remarks = narration,
                        OriginalRemarks = first.EntryRemarks,
                        SodaNo = (string?)null,
                        SodaEntryId = (long?)null,
                        SodaUrl = (string?)null,
                        Items = items,
                        Debit = (double?)0,
                        Credit = (double?)totalReturnAmt,
                        EntryDate = first.EntryDate
                    };
                }).ToList();

            // Combine and sort
            var transactions = salTrans.Concat(rvTrans).Concat(cnTrans)
                                      .OrderBy(t => t.EntryDate)
                                      .ThenBy(t => t.TransId)
                                      .ToList();

            // 6. Build Ledger Rows
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
                    EntryId = t.EntryId,
                    Date = t.EntryDate,
                    TransNo = t.TransNo,
                    Type = t.TransType,
                    PartyNameString = partyName,
                    Remarks = t.Remarks,
                    OriginalRemarks = t.OriginalRemarks,
                    TransactionUrl = t.TransactionUrl,
                    InvoiceViewUrl = t.InvoiceViewUrl,
                    SodaNo = t.SodaNo,
                    SodaEntryId = t.SodaEntryId,
                    SodaUrl = t.SodaUrl,
                    Items = t.Items,
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

            // 1. Data Retrieval
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
                worksheet.Cell(4, 4).Value = "Narration / Details";
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

        public class LedgerItemDetail
        {
            public string ItemName { get; set; } = "";
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
        }

        public class LedgerRow
        {
            public long? EntryId { get; set; }
            public DateTime? Date { get; set; }
            public string TransNo { get; set; } = "";
            public string Type { get; set; } = "";
            public string PartyNameString { get; set; } = "";
            public string Remarks { get; set; } = "";
            public string? OriginalRemarks { get; set; }
            public double Debit { get; set; }
            public double Credit { get; set; }
            public double Balance { get; set; }

            // Clarified details
            public string? SodaNo { get; set; }
            public long? SodaEntryId { get; set; }
            public string? SodaUrl { get; set; }
            public string? TransactionUrl { get; set; }
            public string? InvoiceViewUrl { get; set; }
            public List<LedgerItemDetail> Items { get; set; } = new();
        }
    }
}
