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

            // Initial load
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

            var dnPre = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "DN"
                                  && (e.EntryAttribute1 == supIdStr || e.EntryAttribute2 == supIdStr)
                                  && e.EntryDate < StartDate
                               select new { e.EntryId, t.Debit, t.Credit }).ToListAsync();

            double dnPreTotal = dnPre
                .GroupBy(x => x.EntryId)
                .Sum(g => g.Any(x => (x.Credit ?? 0) > 0)
                    ? g.Where(x => (x.Credit ?? 0) > 0).Sum(x => x.Credit ?? 0)
                    : g.Sum(x => x.Debit ?? 0));

            double openingBalance = purPre.Sum(x => (x.Debit ?? 0)) -
                                    pvPre.Sum(x => (x.Credit ?? 0)) -
                                    dnPreTotal;

            // 2. Fetch Transactions within Range
            // 2a. Purchase Invoices
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
                                    e.EntryId,
                                    e.ItemId,
                                    e.Qty,
                                    e.Lbs,
                                    e.Rate,
                                    e.EntryRefNo,
                                    e.EntryRefId
                                }).ToListAsync();

            // 2b. Payment Vouchers
            var pvRaw = await (from t in _context.Transactions
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
                                   EntryRemarks = e.EntryRemarks,
                                   TransRemarks = t.Remarks,
                                   t.Debit,
                                   t.Credit,
                                   EntryDate = e.EntryDate,
                                   e.EntryId
                               }).ToListAsync();

            // 2c. Debit Notes / Purchase Returns
            var dnRaw = await (from t in _context.Transactions
                               join e in _context.Entries on t.EntryId equals e.EntryId
                               where t.UcpId == ucpId && t.TransType == "DN"
                                  && (e.EntryAttribute1 == supIdStr || e.EntryAttribute2 == supIdStr)
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
                                   e.EntryRefNo,
                                   e.EntryRefId
                               }).ToListAsync();

            // Collect Entry IDs for child line items
            var purEntryIds = purRaw.Select(x => x.EntryId).Distinct().ToList();
            var dnEntryIds = dnRaw.Select(x => x.EntryId).Distinct().ToList();
            var parentEntryIds = purEntryIds.Concat(dnEntryIds).Distinct().ToList();

            var childLines = await _context.Entries
                .Where(e => (e.EntryType == "PURL" || e.EntryType == "DNL") && parentEntryIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            // Item IDs for mapping
            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(purRaw.Where(p => p.ItemId.HasValue).Select(p => p.ItemId!.Value))
                .Concat(dnRaw.Where(d => d.ItemId.HasValue).Select(d => d.ItemId!.Value))
                .Distinct().ToList();

            var itemMap = await _context.ItemMasters
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "");

            // Soda (PSD Contracts) lookups
            var psdRefIds = purRaw.Where(p => p.EntryRefNo.HasValue).Select(p => p.EntryRefNo!.Value)
                .Concat(purRaw.Where(p => p.EntryRefId.HasValue).Select(p => p.EntryRefId!.Value))
                .Distinct().ToList();

            var psdEntries = await _context.Entries
                .Where(e => e.EntryType == "PSD" && (psdRefIds.Contains(e.EntryId) || psdRefIds.Contains(e.EntryNumber ?? 0)))
                .ToListAsync();

            var psdById = psdEntries.ToDictionary(e => e.EntryId, e => e);
            var psdByNumber = psdEntries.Where(e => e.EntryNumber.HasValue).ToDictionary(e => e.EntryNumber!.Value, e => e);

            // 3. Process PUR Transactions
            var purTrans = purRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    var first = g.First();
                    double totalInvoiceAmt = g.Sum(x => x.Debit ?? 0);

                    // Find PSD contract
                    Entry? psd = null;
                    if (first.EntryRefNo.HasValue && psdById.TryGetValue(first.EntryRefNo.Value, out var p1))
                        psd = p1;
                    else if (first.EntryRefNo.HasValue && psdByNumber.TryGetValue(first.EntryRefNo.Value, out var p2))
                        psd = p2;
                    else if (first.EntryRefId.HasValue && psdById.TryGetValue(first.EntryRefId.Value, out var p3))
                        psd = p3;

                    string? sodaNo = null;
                    long? sodaEntryId = null;
                    string? sodaUrl = null;
                    if (psd != null)
                    {
                        sodaNo = $"PSD-{psd.EntryNumber}";
                        sodaEntryId = psd.EntryId;
                        sodaUrl = $"/Purchase/Soda?id={psd.EntryId}";
                    }
                    else if (first.EntryRefNo.HasValue && first.EntryRefNo > 0)
                    {
                        sodaNo = $"PSD-{first.EntryRefNo}";
                        sodaUrl = $"/Purchase/Soda?id={first.EntryRefNo}";
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

                    // Build compiled plain text narration
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
                    if (!string.IsNullOrWhiteSpace(first.EntryRemarks) && first.EntryRemarks != "Purchase Invoice")
                    {
                        narration = string.IsNullOrEmpty(narration) ? first.EntryRemarks : $"{narration} - {first.EntryRemarks}";
                    }
                    if (string.IsNullOrEmpty(narration))
                    {
                        narration = $"Purchase Invoice # PUR-{first.TransNo}";
                    }

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = first.TransNo?.ToString() ?? "-",
                        TransType = first.TransType ?? "PUR",
                        EntryId = (long?)first.EntryId,
                        TransactionUrl = $"/Purchase/Entry?id={first.EntryId}",
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

            // 4. Process PV Transactions
            var pvTrans = pvRaw
                .GroupBy(x => x.EntryId)
                .Select(g =>
                {
                    var first = g.First();
                    double totalPaymentAmt = g.Sum(x => x.Credit ?? 0);

                    string narration = !string.IsNullOrEmpty(first.EntryRemarks)
                        ? first.EntryRemarks
                        : (!string.IsNullOrEmpty(first.TransRemarks) ? first.TransRemarks : $"Payment Voucher # PV-{first.TransNo}");

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = first.TransNo?.ToString() ?? "-",
                        TransType = first.TransType ?? "PV",
                        EntryId = (long?)first.EntryId,
                        TransactionUrl = $"/Vouchers/PaymentVoucher?id={first.EntryId}",
                        Remarks = narration,
                        OriginalRemarks = first.EntryRemarks,
                        SodaNo = (string?)null,
                        SodaEntryId = (long?)null,
                        SodaUrl = (string?)null,
                        Items = new List<LedgerItemDetail>(),
                        Debit = (double?)0,
                        Credit = (double?)totalPaymentAmt,
                        EntryDate = first.EntryDate
                    };
                }).ToList();

            // 5. Process DN Transactions
            var dnTrans = dnRaw
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
                        : $"Debit Note (Purchase Return) # DN-{first.TransNo}";

                    return new
                    {
                        TransId = g.Min(x => x.TransId),
                        TransNo = first.TransNo?.ToString() ?? "-",
                        TransType = first.TransType ?? "DN",
                        EntryId = (long?)first.EntryId,
                        TransactionUrl = $"/Purchase/PurchaseReturn?id={first.EntryId}",
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
            var transactions = purTrans.Concat(pvTrans).Concat(dnTrans)
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
            string partyName = supplier.SupplierName ?? "N/A";

            foreach (var t in transactions)
            {
                double credit = t.Credit ?? 0;
                double debit = t.Debit ?? 0;
                // Supplier Ledger: Previous + Debit - Credit
                // PUR (Debit) increases balance, PV/DN (Credit) decreases balance
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
                        $"SupplierLedger_{supplierName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.xlsx");
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
            public List<LedgerItemDetail> Items { get; set; } = new();
        }
    }
}
