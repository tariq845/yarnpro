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
                            t.EntryId,
                            TransRemarks = t.Remarks,
                            EntryRemarks = entry != null ? entry.EntryRemarks : null,
                            EntryAttribute1 = entry != null ? entry.EntryAttribute1 : null,
                            EntryAttribute2 = entry != null ? entry.EntryAttribute2 : null,
                            EntryAttribute3 = entry != null ? entry.EntryAttribute3 : null,
                            EntryAttribute4 = entry != null ? entry.EntryAttribute4 : null,
                            EntryRefId = entry != null ? entry.EntryRefId : null,
                            EntryRefNo = entry != null ? entry.EntryRefNo : null,
                            ItemId = entry != null ? entry.ItemId : null,
                            Qty = entry != null ? entry.Qty : null,
                            Lbs = entry != null ? entry.Lbs : null,
                            Rate = entry != null ? entry.Rate : null,
                            EntryAmount = entry != null ? entry.Amount : null,
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

            // Collect Entry IDs for batch lookup of lines, items, sodas, and parties
            var entryIds = transactions.Where(x => x.EntryId.HasValue).Select(x => x.EntryId!.Value).Distinct().ToList();

            // Load child lines (SALL, PURL, CNL, DNL - trading inventory lines only)
            var childLines = await _context.Entries
                .Where(e => (e.EntryType == "SALL" || e.EntryType == "PURL" || e.EntryType == "CNL" || e.EntryType == "DNL") &&
                            entryIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            // Collect Item IDs
            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(transactions.Where(t => t.ItemId.HasValue).Select(t => t.ItemId!.Value))
                .Distinct().ToList();

            var itemMap = await _context.ItemMasters
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "");

            // Look up linked Soda contracts (SSD or PSD)
            var sodaRefIds = transactions.Where(t => t.EntryRefId.HasValue).Select(t => t.EntryRefId!.Value)
                .Concat(transactions.Where(t => t.EntryRefNo.HasValue).Select(t => t.EntryRefNo!.Value))
                .Distinct().ToList();

            var sodaEntries = await _context.Entries
                .Where(e => (e.EntryType == "SSD" || e.EntryType == "PSD") &&
                            (e.UcpId == ucpId || e.UcpId == null) &&
                            (sodaRefIds.Contains(e.EntryId) || sodaRefIds.Contains(e.EntryNumber ?? 0)))
                .ToListAsync();

            var sodaById = sodaEntries
                .GroupBy(e => e.EntryId)
                .ToDictionary(g => g.Key, g => g.First());

            var sodaByNumber = sodaEntries
                .Where(e => e.EntryNumber.HasValue)
                .GroupBy(e => e.EntryNumber!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // Customers and Suppliers for party names
            var customers = await _context.CustomerMasters
                .Where(c => c.UcpId == ucpId)
                .ToDictionaryAsync(c => c.CustomerId.ToString(), c => c.CustomerName ?? "");

            var suppliers = await _context.SupplierMasters
                .Where(s => s.UcpId == ucpId)
                .ToDictionaryAsync(s => s.SupplierId.ToString(), s => s.SupplierName ?? "");

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

                // Determine transaction URL & invoice view URL
                string? transUrl = null;
                string? invoiceViewUrl = null;
                string typeCode = t.TransType ?? "";

                if (t.EntryId.HasValue && t.EntryId.Value > 0)
                {
                    long eid = t.EntryId.Value;
                    switch (typeCode.ToUpper())
                    {
                        case "SAL":
                            transUrl = $"/Sales/Entry?id={eid}";
                            invoiceViewUrl = $"/Sales/InvoiceView/{eid}";
                            break;
                        case "PUR":
                            transUrl = $"/Purchase/Entry?id={eid}";
                            break;
                        case "RV":
                            transUrl = $"/Vouchers/ReceiptVoucher?id={eid}";
                            break;
                        case "PV":
                            transUrl = $"/Vouchers/PaymentVoucher?id={eid}";
                            break;
                        case "JV":
                            transUrl = $"/Vouchers/JournalVoucher?id={eid}";
                            break;
                        case "CN":
                            transUrl = $"/Sales/SaleReturn?id={eid}";
                            break;
                        case "DN":
                            transUrl = $"/Purchase/PurchaseReturn?id={eid}";
                            break;
                    }
                }

                // Determine linked Soda
                string? sodaNo = null;
                string? sodaUrl = null;
                long? sodaEntryId = null;

                if (t.EntryRefId.HasValue && sodaById.TryGetValue(t.EntryRefId.Value, out var s1))
                {
                    sodaNo = $"{s1.EntryType}-{s1.EntryNumber}";
                    sodaEntryId = s1.EntryId;
                    sodaUrl = s1.EntryType == "SSD" ? $"/Sales/Soda?id={s1.EntryId}" : $"/Purchase/Soda?id={s1.EntryId}";
                }
                else if (t.EntryRefNo.HasValue && sodaById.TryGetValue(t.EntryRefNo.Value, out var s2))
                {
                    sodaNo = $"{s2.EntryType}-{s2.EntryNumber}";
                    sodaEntryId = s2.EntryId;
                    sodaUrl = s2.EntryType == "SSD" ? $"/Sales/Soda?id={s2.EntryId}" : $"/Purchase/Soda?id={s2.EntryId}";
                }
                else if (t.EntryRefNo.HasValue && sodaByNumber.TryGetValue(t.EntryRefNo.Value, out var s3))
                {
                    sodaNo = $"{s3.EntryType}-{s3.EntryNumber}";
                    sodaEntryId = s3.EntryId;
                    sodaUrl = s3.EntryType == "SSD" ? $"/Sales/Soda?id={s3.EntryId}" : $"/Purchase/Soda?id={s3.EntryId}";
                }
                else if (t.EntryRefNo.HasValue && t.EntryRefNo > 0)
                {
                    if (typeCode == "SAL")
                    {
                        sodaNo = $"SSD-{t.EntryRefNo}";
                        sodaUrl = $"/Sales/Soda?id={t.EntryRefNo}";
                    }
                    else if (typeCode == "PUR")
                    {
                        sodaNo = $"PSD-{t.EntryRefNo}";
                        sodaUrl = $"/Purchase/Soda?id={t.EntryRefNo}";
                    }
                }

                // Resolve line items (only for inventory/trading entries: SAL, PUR, CN, DN)
                var items = new List<LedgerItemDetail>();
                bool isInventoryEntry = typeCode == "SAL" || typeCode == "PUR" || typeCode == "CN" || typeCode == "DN";
                if (isInventoryEntry && t.EntryId.HasValue)
                {
                    var lines = childLines.Where(c => c.EntryRefId == t.EntryId.Value).ToList();
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
                                Rate = l.Rate,
                                Amount = l.Amount ?? ((l.Lbs ?? (l.Qty ?? 0)) * (l.Rate ?? 0))
                            });
                        }
                    }
                    else if (t.ItemId.HasValue || (t.Qty ?? 0) > 0 || (t.Rate ?? 0) > 0)
                    {
                        string name = t.ItemId.HasValue && itemMap.ContainsKey(t.ItemId.Value)
                            ? itemMap[t.ItemId.Value]
                            : "Item";

                        items.Add(new LedgerItemDetail
                        {
                            ItemName = name,
                            Qty = t.Qty,
                            Lbs = t.Lbs,
                            Rate = t.Rate,
                            Amount = t.EntryAmount ?? ((t.Lbs ?? (t.Qty ?? 0)) * (t.Rate ?? 0))
                        });
                    }
                }

                // Determine party name accurately based on transaction type
                string? partyName = null;
                if (typeCode == "PUR" || typeCode == "DN")
                {
                    // Purchase / Debit Note: Party is always a Supplier
                    if (!string.IsNullOrEmpty(t.EntryAttribute1) && suppliers.TryGetValue(t.EntryAttribute1, out var supName))
                    {
                        partyName = supName;
                    }
                    else if (!string.IsNullOrEmpty(t.EntryAttribute2) && suppliers.TryGetValue(t.EntryAttribute2, out var supName2))
                    {
                        partyName = supName2;
                    }
                }
                else if (typeCode == "SAL" || typeCode == "CN")
                {
                    // Sales / Credit Note: Party is always a Customer
                    if (!string.IsNullOrEmpty(t.EntryAttribute1) && customers.TryGetValue(t.EntryAttribute1, out var cName))
                    {
                        partyName = cName;
                    }
                    else if (!string.IsNullOrEmpty(t.EntryAttribute2) && customers.TryGetValue(t.EntryAttribute2, out var cName2))
                    {
                        partyName = cName2;
                    }
                }
                else
                {
                    // Vouchers or other entries: check Customer then Supplier
                    if (!string.IsNullOrEmpty(t.EntryAttribute1))
                    {
                        if (customers.TryGetValue(t.EntryAttribute1, out var cName)) partyName = cName;
                        else if (suppliers.TryGetValue(t.EntryAttribute1, out var supName)) partyName = supName;
                    }
                    if (string.IsNullOrEmpty(partyName) && !string.IsNullOrEmpty(t.EntryAttribute2))
                    {
                        if (customers.TryGetValue(t.EntryAttribute2, out var cName2)) partyName = cName2;
                        else if (suppliers.TryGetValue(t.EntryAttribute2, out var supName2)) partyName = supName2;
                    }
                }

                string? agentName = !string.IsNullOrWhiteSpace(t.EntryAttribute4) ? t.EntryAttribute4.Trim() : null;

                string displayRemarks = !string.IsNullOrWhiteSpace(t.TransRemarks)
                    ? t.TransRemarks.Trim()
                    : (!string.IsNullOrWhiteSpace(t.EntryRemarks) ? t.EntryRemarks.Trim() : "");

                // Check if this row represents a commission transaction
                bool isCommission = (!string.IsNullOrEmpty(t.AccountName) && t.AccountName.Contains("COMMISSION", StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrEmpty(displayRemarks) && displayRemarks.Contains("Commission", StringComparison.OrdinalIgnoreCase));

                // Normalise Purchase commission remarks to "Commission Receivable" if applicable
                if (isCommission && (typeCode == "PUR" || typeCode == "DN"))
                {
                    displayRemarks = displayRemarks.Replace("Commission Payable to", "Commission Receivable from", StringComparison.OrdinalIgnoreCase);
                    displayRemarks = displayRemarks.Replace("Commission Payable", "Commission Receivable", StringComparison.OrdinalIgnoreCase);
                    displayRemarks = displayRemarks.Replace("Commission Expense against Purchase", "Commission Receivable against Purchase", StringComparison.OrdinalIgnoreCase);
                    displayRemarks = displayRemarks.Replace("Commission Expense", "Commission Receivable", StringComparison.OrdinalIgnoreCase);
                }
                else if (isCommission && (typeCode == "SAL" || typeCode == "CN"))
                {
                    displayRemarks = displayRemarks.Replace("Commission Receivable from", "Commission Payable to", StringComparison.OrdinalIgnoreCase);
                    displayRemarks = displayRemarks.Replace("Commission Receivable", "Commission Payable", StringComparison.OrdinalIgnoreCase);
                }

                string partyRoleLabel = (typeCode == "PUR" || typeCode == "DN") ? "Supplier" :
                                         ((typeCode == "SAL" || typeCode == "CN") ? "Customer" : "Party");

                string narration;
                List<LedgerItemDetail> rowItems = items;
                string? displayPartyName = null;

                if (!string.IsNullOrEmpty(agentName))
                {
                    displayPartyName = !string.IsNullOrEmpty(partyName)
                        ? $"Agent: {agentName} | {partyRoleLabel}: {partyName}"
                        : $"Agent: {agentName}";
                }
                else if (!string.IsNullOrEmpty(partyName))
                {
                    displayPartyName = $"{partyRoleLabel}: {partyName}";
                }

                if (isCommission)
                {
                    // For commission entries: clean, intelligent narration including bags, lbs, rate, and amount
                    var commQtyParts = new List<string>();
                    double totalCommQty = items.Where(i => i.Qty.HasValue).Sum(i => i.Qty!.Value);
                    double totalCommLbs = items.Where(i => i.Lbs.HasValue).Sum(i => i.Lbs!.Value);
                    double? commRate = items.FirstOrDefault(i => i.Rate.HasValue && i.Rate > 0)?.Rate ?? t.Rate;
                    double totalCommAmt = items.Where(i => i.Amount.HasValue).Sum(i => i.Amount!.Value);
                    if (totalCommAmt <= 0 && t.EntryAmount.HasValue && t.EntryAmount > 0) totalCommAmt = t.EntryAmount.Value;
                    if (totalCommAmt <= 0 && commRate.HasValue && totalCommLbs > 0) totalCommAmt = totalCommLbs * commRate.Value;

                    if (totalCommQty > 0) commQtyParts.Add($"{totalCommQty:N0} Bags");
                    if (totalCommLbs > 0) commQtyParts.Add($"{totalCommLbs:N2} Lbs");
                    if (commRate.HasValue && commRate > 0) commQtyParts.Add($"@{commRate:N2}");
                    if (totalCommAmt > 0) commQtyParts.Add($"Amt: {totalCommAmt:N2}");
                    string commQtyStr = commQtyParts.Any() ? $"({string.Join(", ", commQtyParts)})" : "";

                    narration = commQtyStr;

                    if (!string.IsNullOrEmpty(sodaNo))
                    {
                        narration = string.IsNullOrEmpty(narration) ? $"Soda: {sodaNo}" : $"{narration} | Soda: {sodaNo}";
                    }

                    if (!string.IsNullOrEmpty(displayPartyName))
                    {
                        narration = string.IsNullOrEmpty(narration) ? displayPartyName : $"{narration} | {displayPartyName}";
                    }

                    if (!string.IsNullOrWhiteSpace(displayRemarks))
                    {
                        if (string.IsNullOrEmpty(narration))
                        {
                            narration = displayRemarks;
                        }
                        else if (!narration.Contains(displayRemarks, StringComparison.OrdinalIgnoreCase))
                        {
                            narration = $"{narration} - {displayRemarks}";
                        }
                    }

                    // For the items column in commission view, show summary bags, lbs, rate, and amount
                    if (totalCommQty > 0 || totalCommLbs > 0 || (commRate.HasValue && commRate > 0) || totalCommAmt > 0)
                    {
                        rowItems = new List<LedgerItemDetail>
                        {
                            new LedgerItemDetail
                            {
                                ItemName = !string.IsNullOrEmpty(agentName) ? $"Commission ({agentName})" : "Commission",
                                Qty = totalCommQty > 0 ? totalCommQty : null,
                                Lbs = totalCommLbs > 0 ? totalCommLbs : null,
                                Rate = commRate > 0 ? commRate : null,
                                Amount = totalCommAmt > 0 ? totalCommAmt : null
                            }
                        };
                    }
                    else
                    {
                        rowItems = new List<LedgerItemDetail>();
                    }
                }
                else
                {
                    // Build detailed compiled narration for Excel and tooltips (Standard Customer / Supplier view)
                    var itemParts = new List<string>();
                    foreach (var itm in items)
                    {
                        var sub = new List<string>();
                        if (itm.Qty.HasValue && itm.Qty > 0) sub.Add($"{itm.Qty:N0} Bags");
                        if (itm.Lbs.HasValue && itm.Lbs > 0) sub.Add($"{itm.Lbs:N2} Lbs");
                        if (itm.Rate.HasValue && itm.Rate > 0) sub.Add($"@{itm.Rate:N2}");
                        if (itm.Amount.HasValue && itm.Amount > 0) sub.Add($"Amt: {itm.Amount:N2}");
                        string p = itm.ItemName;
                        if (sub.Any()) p += $" ({string.Join(", ", sub)})";
                        itemParts.Add(p);
                    }

                    itemParts = itemParts.Distinct().ToList();
                    string compiledItems = string.Join("; ", itemParts);
                    narration = compiledItems;

                    if (!string.IsNullOrEmpty(sodaNo))
                    {
                        narration = string.IsNullOrEmpty(narration) ? $"Soda: {sodaNo}" : $"{narration} | Soda: {sodaNo}";
                    }
                    if (!string.IsNullOrEmpty(displayPartyName))
                    {
                        narration = string.IsNullOrEmpty(narration) ? displayPartyName : $"{narration} | {displayPartyName}";
                    }

                    if (!string.IsNullOrWhiteSpace(displayRemarks) && 
                        displayRemarks != "Sales Invoice" && 
                        displayRemarks != "Purchase Invoice" && 
                        displayRemarks != "Journal Voucher" &&
                        displayRemarks != "Payment Voucher" &&
                        displayRemarks != "Receipt Voucher")
                    {
                        if (string.IsNullOrEmpty(narration))
                        {
                            narration = displayRemarks;
                        }
                        else if (!narration.Contains(displayRemarks, StringComparison.OrdinalIgnoreCase))
                        {
                            narration = $"{narration} - {displayRemarks}";
                        }
                    }
                }

                if (string.IsNullOrEmpty(narration))
                {
                    narration = $"{typeCode} #{t.TransNo}";
                }

                LedgerRows.Add(new GeneralLedgerRow
                {
                    Date = t.EntryDate,
                    TransNo = t.TransNo?.ToString() ?? "-",
                    Type = typeCode,
                    AccountName = t.AccountName ?? "-",
                    Remarks = narration,
                    OriginalRemarks = displayRemarks,
                    PartyName = displayPartyName,
                    AgentName = agentName,
                    EntryId = t.EntryId,
                    TransactionUrl = transUrl,
                    InvoiceViewUrl = invoiceViewUrl,
                    SodaNo = sodaNo,
                    SodaUrl = sodaUrl,
                    Items = rowItems,
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
                worksheet.Cell(4, 5).Value = "Narration / Details";
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

        public class LedgerItemDetail
        {
            public string ItemName { get; set; } = "Item";
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
        }

        public class GeneralLedgerRow
        {
            public long? EntryId { get; set; }
            public DateTime? Date { get; set; }
            public string TransNo { get; set; } = "";
            public string Type { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string Remarks { get; set; } = "";
            public string? OriginalRemarks { get; set; }
            public string? PartyName { get; set; }
            public string? AgentName { get; set; }
            public string? TransactionUrl { get; set; }
            public string? InvoiceViewUrl { get; set; }
            public string? SodaNo { get; set; }
            public string? SodaUrl { get; set; }
            public List<LedgerItemDetail> Items { get; set; } = new();
            public double Debit { get; set; }
            public double Credit { get; set; }
            public double Balance { get; set; }
        }
    }
}
