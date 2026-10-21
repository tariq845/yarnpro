using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Helpers;
using YIS.Models;
using System.IO;
using ClosedXML.Excel;

namespace YIS.Pages.Reports
{
    public class SupplierAgingModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SupplierAgingModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public DateTime? AsOfDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? SupplierId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? BrokerFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool UnClearedOnly { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SodaNo { get; set; }

        public string CompanyName { get; set; } = "YARN PRO";
        public string UserName { get; set; } = "ADMIN";
        public DateTime GeneratedOn { get; set; } = DateTime.Now;

        public List<SelectListItem> SupplierDropdown { get; set; } = new();
        public List<SelectListItem> BrokerDropdown { get; set; } = new();

        // Index / Stock-style Summary list
        public List<SupplierSummaryItem> SupplierSummaries { get; set; } = new();

        // Detailed Drill-down report
        public List<BrokerAgingGroup> GroupedReport { get; set; } = new();

        // Selected Supplier info when viewing detail
        public SupplierMaster? SelectedSupplier { get; set; }

        // Metrics
        public double GrandTotalAmount { get; set; }
        public double GrandTotalReceived { get; set; }
        public double GrandTotalBalance { get; set; }
        public double GrandTotalPdc { get; set; }
        public int TotalInvoicesCount { get; set; }

        // Slabs
        public double Slab_0_30 { get; set; }
        public double Slab_31_60 { get; set; }
        public double Slab_61_90 { get; set; }
        public double Slab_91_120 { get; set; }
        public double Slab_120_Plus { get; set; }

        public class SupplierSummaryItem
        {
            public long SupplierId { get; set; }
            public string SupplierName { get; set; } = "";
            public string? Contact { get; set; }
            public string? City { get; set; }
            public string? BrokerName { get; set; }
            public int InvoicesCount { get; set; }
            public double TotalBalance { get; set; }
            public double Slab0_30 { get; set; }
            public double Slab31_60 { get; set; }
            public double Slab61_90 { get; set; }
            public double Slab90Plus { get; set; }
        }

        public class AgingItemRow
        {
            public long EntryId { get; set; }
            public string TransactionNumber { get; set; } = "";
            public DateTime? Date { get; set; }
            public string ContractNumber { get; set; } = "";
            public string InvoiceNumber { get; set; } = "";
            public string SupplierPurchaseNo { get; set; } = "";
            public double Qty { get; set; }
            public string Description { get; set; } = "";
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double ReceivedPaid { get; set; }
            public double Balance { get; set; }
            public int ContDays { get; set; }
            public int TotalDays { get; set; }
            public double PdcAmount { get; set; }
            public DateTime? PdcDate { get; set; }
            public string? BuyerName { get; set; } // Supplier / Seller
            public string? BrokerName { get; set; }
        }

        public class BuyerAgingGroup
        {
            public string BuyerName { get; set; } = "";
            public List<AgingItemRow> Items { get; set; } = new();
            public double SubTotalAmount => Items.Sum(i => i.Amount);
            public double SubTotalReceived => Items.Sum(i => i.ReceivedPaid);
            public double SubTotalBalance => Items.Sum(i => i.Balance);
            public double SubTotalPdc => Items.Sum(i => i.PdcAmount);
        }

        public class BrokerAgingGroup
        {
            public string BrokerName { get; set; } = "";
            public List<BuyerAgingGroup> Buyers { get; set; } = new();
            public double SubTotalAmount => Buyers.Sum(b => b.SubTotalAmount);
            public double SubTotalReceived => Buyers.Sum(b => b.SubTotalReceived);
            public double SubTotalBalance => Buyers.Sum(b => b.SubTotalBalance);
            public double SubTotalPdc => Buyers.Sum(b => b.SubTotalPdc);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            if (!AsOfDate.HasValue)
            {
                AsOfDate = DateTime.Today;
            }

            await LoadCompanyAndUserDataAsync(activeUcpId.Value);
            await LoadReportDataAsync(activeUcpId.Value);

            return Page();
        }

        private async Task LoadCompanyAndUserDataAsync(long ucpId)
        {
            var company = await _context.UserCompanyProfiles.FindAsync(ucpId);
            if (company != null && !string.IsNullOrEmpty(company.UcpCompanyName))
            {
                CompanyName = company.UcpCompanyName;
            }

            var user = HttpContext.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(user))
            {
                UserName = user.ToUpper();
            }
        }

        private async Task LoadReportDataAsync(long ucpId)
        {
            DateTime targetDate = AsOfDate?.Date ?? DateTime.Today;

            // 1. Get Suppliers
            var suppliers = await _context.SupplierMasters
                .Where(s => s.UcpId == ucpId)
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            SupplierDropdown = suppliers.Select(s => new SelectListItem
            {
                Value = s.SupplierId.ToString(),
                Text = s.SupplierName ?? "Unknown"
            }).ToList();

            var supplierDict = suppliers.ToDictionary(s => s.SupplierId, s => s);

            if (SupplierId.HasValue && supplierDict.ContainsKey(SupplierId.Value))
            {
                SelectedSupplier = supplierDict[SupplierId.Value];
            }

            // Only include entries that are POSTED to General Ledger (TRANS table)
            var postedEntryIds = (await _context.Transactions
                .Where(t => t.UcpId == ucpId && t.EntryId.HasValue)
                .Select(t => t.EntryId!.Value)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            // 2. Get Purchase Invoices (PUR) up to targetDate (Only GL-posted)
            var purQuery = _context.Entries
                .Where(e => e.EntryType == "PUR" && e.UcpId == ucpId && e.EntryDate <= targetDate);

            if (SupplierId.HasValue)
            {
                string partyStr = SupplierId.Value.ToString();
                purQuery = purQuery.Where(e => e.EntryAttribute1 == partyStr);
            }

            var allPurchaseInvoices = await purQuery
                .OrderBy(e => e.EntryDate)
                .ThenBy(e => e.EntryId)
                .ToListAsync();

            var purchaseInvoices = allPurchaseInvoices
                .Where(e => postedEntryIds.Contains(e.EntryId))
                .ToList();

            var purIds = purchaseInvoices.Select(p => p.EntryId).ToList();

            // 3. Child Line Items (PURL)
            var childLines = await _context.Entries
                .Where(e => e.EntryType == "PURL" && purIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(purchaseInvoices.Where(p => p.ItemId.HasValue).Select(p => p.ItemId!.Value))
                .Distinct().ToList();

            var itemsMap = await _context.ItemMasters
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "");

            // 4. PSD Mapping (Contracts)
            var psdIds = purchaseInvoices.Where(p => p.EntryRefNo.HasValue).Select(p => p.EntryRefNo!.Value).Distinct().ToList();
            var psdMap = await _context.Entries
                .Where(e => e.EntryType == "PSD" && psdIds.Contains(e.EntryId))
                .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);

            // 5. Total Payments per Supplier up to targetDate (PV, DN - Only GL-posted)
            var suppIdStrings = purchaseInvoices.Select(p => p.EntryAttribute1).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
            var paymentsBySupplier = new Dictionary<string, double>();

            var allPvEntries = await _context.Entries
                .Where(e => (e.EntryType == "PV" || e.EntryType == "DN") && e.UcpId == ucpId && e.EntryDate <= targetDate && suppIdStrings.Contains(e.EntryAttribute2!))
                .ToListAsync();

            var pvEntries = allPvEntries
                .Where(e => postedEntryIds.Contains(e.EntryId))
                .ToList();

            foreach (var suppId in suppIdStrings)
            {
                paymentsBySupplier[suppId] = pvEntries.Where(p => p.EntryAttribute2 == suppId).Sum(p => p.Amount ?? 0);
            }

            // Extract Brokers
            var brokers = purchaseInvoices
                .Select(p => string.IsNullOrWhiteSpace(p.EntryAttribute4) ? "DIRECT / GENERAL PURCHASE" : p.EntryAttribute4.Trim())
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            BrokerDropdown = brokers.Select(b => new SelectListItem { Value = b, Text = b }).ToList();

            if (!string.IsNullOrEmpty(BrokerFilter))
            {
                purchaseInvoices = purchaseInvoices.Where(p => (string.IsNullOrWhiteSpace(p.EntryAttribute4) ? "DIRECT / GENERAL PURCHASE" : p.EntryAttribute4.Trim()) == BrokerFilter).ToList();
            }

            // 6. FIFO Settlement
            var processedRows = new List<AgingItemRow>();
            var groupedBySupp = purchaseInvoices.GroupBy(p => p.EntryAttribute1 ?? "0");

            foreach (var suppGroup in groupedBySupp)
            {
                string suppKey = suppGroup.Key;
                double unallocatedPayments = paymentsBySupplier.ContainsKey(suppKey) ? paymentsBySupplier[suppKey] : 0;

                long.TryParse(suppKey, out long parsedSuppId);
                var sMaster = supplierDict.ContainsKey(parsedSuppId) ? supplierDict[parsedSuppId] : null;
                string suppName = sMaster?.SupplierName ?? "Supplier #" + suppKey;

                foreach (var inv in suppGroup.OrderBy(i => i.EntryDate).ThenBy(i => i.EntryId))
                {
                    double.TryParse(inv.EntryAttribute3, out double netAmt);
                    double invAmount = netAmt > 0 ? netAmt : (inv.Amount ?? 0);
                    double paidForThis = 0;

                    if (unallocatedPayments > 0)
                    {
                        if (unallocatedPayments >= invAmount)
                        {
                            paidForThis = invAmount;
                            unallocatedPayments -= invAmount;
                        }
                        else
                        {
                            paidForThis = unallocatedPayments;
                            unallocatedPayments = 0;
                        }
                    }

                    double balance = invAmount - paidForThis;

                    if (UnClearedOnly && balance <= 0.01)
                    {
                        continue;
                    }

                    var lines = childLines.Where(c => c.EntryRefId == inv.EntryId).ToList();
                    string desc = "";
                    double totalQty = 0;
                    double rate = inv.Rate ?? 0;

                    if (lines.Any())
                    {
                        totalQty = lines.Sum(l => l.Qty ?? 0);
                        var descList = lines.Select(l =>
                        {
                            string iName = (l.ItemId.HasValue && itemsMap.ContainsKey(l.ItemId.Value)) ? itemsMap[l.ItemId.Value] : "";
                            return iName;
                        }).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct();
                        desc = string.Join(" / ", descList);
                        if (lines.Any(l => l.Rate.HasValue && l.Rate > 0))
                        {
                            rate = lines.First(l => l.Rate.HasValue && l.Rate > 0).Rate ?? 0;
                        }
                    }
                    else
                    {
                        totalQty = inv.Qty ?? 0;
                        if (inv.ItemId.HasValue && itemsMap.ContainsKey(inv.ItemId.Value))
                        {
                            desc = itemsMap[inv.ItemId.Value];
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(inv.EntryRemarks))
                    {
                        desc = string.IsNullOrWhiteSpace(desc) ? inv.EntryRemarks : desc + " (" + inv.EntryRemarks + ")";
                    }

                    int contDays = inv.EntryTermsDays ?? 0;
                    int totalDays = inv.EntryDate.HasValue ? (int)(targetDate - inv.EntryDate.Value.Date).TotalDays : 0;
                    if (totalDays < 0) totalDays = 0;

                    long contractNo = (inv.EntryRefNo.HasValue && psdMap.ContainsKey(inv.EntryRefNo.Value)) ? psdMap[inv.EntryRefNo.Value] : (inv.EntryRefNo ?? 0);
                    string broker = string.IsNullOrWhiteSpace(inv.EntryAttribute4) ? "DIRECT / GENERAL PURCHASE" : inv.EntryAttribute4.Trim();

                    // Optional Soda filter
                    if (!string.IsNullOrWhiteSpace(SodaNo) && contractNo.ToString() != SodaNo.Trim() && $"PSD-{contractNo}" != SodaNo.Trim())
                    {
                        continue;
                    }

                    var row = new AgingItemRow
                    {
                        EntryId = inv.EntryId,
                        TransactionNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString("D7") : inv.EntryId.ToString("D7"),
                        Date = inv.EntryDate,
                        ContractNumber = contractNo > 0 ? contractNo.ToString("D7") : "-",
                        InvoiceNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString() : inv.EntryId.ToString(),
                        SupplierPurchaseNo = !string.IsNullOrWhiteSpace(inv.EntryRefCode) ? inv.EntryRefCode.Trim() : "-",
                        Qty = totalQty,
                        Description = desc,
                        Rate = rate,
                        Amount = invAmount,
                        ReceivedPaid = paidForThis,
                        Balance = balance,
                        ContDays = contDays,
                        TotalDays = totalDays,
                        PdcAmount = 0,
                        BuyerName = suppName,
                        BrokerName = broker
                    };

                    processedRows.Add(row);
                }
            }

            // Quick Search
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                processedRows = processedRows.Where(r =>
                    r.InvoiceNumber.Contains(term) ||
                    r.ContractNumber.Contains(term) ||
                    r.TransactionNumber.Contains(term) ||
                    (!string.IsNullOrEmpty(r.SupplierPurchaseNo) && r.SupplierPurchaseNo.ToLower().Contains(term)) ||
                    (r.BuyerName != null && r.BuyerName.ToLower().Contains(term)) ||
                    (r.BrokerName != null && r.BrokerName.ToLower().Contains(term)) ||
                    r.Description.ToLower().Contains(term)
                ).ToList();
            }

            // Build Stock-style Supplier Summaries
            SupplierSummaries = processedRows
                .GroupBy(r => r.BuyerName ?? "Unknown")
                .Select(sg =>
                {
                    var items = sg.ToList();
                    var first = items.First();
                    long sId = 0;
                    var matchSupp = suppliers.FirstOrDefault(s => (s.SupplierName ?? "") == sg.Key);
                    if (matchSupp != null) sId = matchSupp.SupplierId;

                    return new SupplierSummaryItem
                    {
                        SupplierId = sId,
                        SupplierName = sg.Key,
                        Contact = matchSupp?.SupplierContact,
                        City = matchSupp?.SupplierAddress,
                        BrokerName = first.BrokerName,
                        InvoicesCount = items.Count,
                        TotalBalance = items.Sum(i => i.Balance),
                        Slab0_30 = items.Where(i => i.TotalDays <= 30).Sum(i => i.Balance),
                        Slab31_60 = items.Where(i => i.TotalDays > 30 && i.TotalDays <= 60).Sum(i => i.Balance),
                        Slab61_90 = items.Where(i => i.TotalDays > 60 && i.TotalDays <= 90).Sum(i => i.Balance),
                        Slab90Plus = items.Where(i => i.TotalDays > 90).Sum(i => i.Balance)
                    };
                })
                .OrderByDescending(s => s.TotalBalance)
                .ToList();

            // Grouped Report for Detail View
            GroupedReport = processedRows
                .GroupBy(r => r.BrokerName ?? "DIRECT / GENERAL PURCHASE")
                .Select(bg => new BrokerAgingGroup
                {
                    BrokerName = bg.Key,
                    Buyers = bg.GroupBy(r => r.BuyerName ?? "Unknown")
                               .Select(ug => new BuyerAgingGroup
                               {
                                   BuyerName = ug.Key,
                                   Items = ug.OrderBy(i => i.Date).ThenBy(i => i.EntryId).ToList()
                               }).ToList()
                })
                .OrderBy(bg => bg.BrokerName)
                .ToList();

            // Slabs
            GrandTotalAmount = GroupedReport.Sum(g => g.SubTotalAmount);
            GrandTotalReceived = GroupedReport.Sum(g => g.SubTotalReceived);
            GrandTotalBalance = GroupedReport.Sum(g => g.SubTotalBalance);
            GrandTotalPdc = GroupedReport.Sum(g => g.SubTotalPdc);
            TotalInvoicesCount = GroupedReport.Sum(g => g.Buyers.Sum(b => b.Items.Count));

            var allRows = GroupedReport.SelectMany(g => g.Buyers.SelectMany(b => b.Items)).ToList();
            Slab_0_30 = allRows.Where(r => r.TotalDays <= 30).Sum(r => r.Balance);
            Slab_31_60 = allRows.Where(r => r.TotalDays > 30 && r.TotalDays <= 60).Sum(r => r.Balance);
            Slab_61_90 = allRows.Where(r => r.TotalDays > 60 && r.TotalDays <= 90).Sum(r => r.Balance);
            Slab_91_120 = allRows.Where(r => r.TotalDays > 90 && r.TotalDays <= 120).Sum(r => r.Balance);
            Slab_120_Plus = allRows.Where(r => r.TotalDays > 120).Sum(r => r.Balance);
        }

        public async Task<IActionResult> OnGetExportExcelAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            DateTime targetDate = AsOfDate?.Date ?? DateTime.Today;
            await LoadCompanyAndUserDataAsync(activeUcpId.Value);
            await LoadReportDataAsync(activeUcpId.Value);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Supplier Aging");

            ws.Cell("A1").Value = CompanyName;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:N1").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A2").Value = $"AGING REPORT (PAYABLES ONLY) UPTO {targetDate:dd-MM-yyyy}";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:N2").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A3").Value = UnClearedOnly ? "UnCleared Bills Only" : "All Bills";
            ws.Cell("O3").Value = $"Date: {GeneratedOn:dd-MM-yyyy hh:mm tt}";

            string[] headers = {
                "TRANSACTION NUMBER", "DATE", "CONTRACT NUMBER", "INVOICE NUMBER", "SUPPLIER PUR NO",
                "QTY", "DESCRIPTION", "RATE", "AMOUNT",
                "RECEIVED / PAID", "BALANCE", "CONT. DAYS", "TOTAL DAYS",
                "PDC AMOUNT", "PDC DT"
            };

            int rowIdx = 5;
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(rowIdx, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f2f2f2");
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            rowIdx++;

            foreach (var broker in GroupedReport)
            {
                ws.Cell(rowIdx, 1).Value = broker.BrokerName;
                ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                ws.Range(rowIdx, 1, rowIdx, 15).Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f4f8");
                rowIdx++;

                foreach (var buyer in broker.Buyers)
                {
                    ws.Cell(rowIdx, 1).Value = buyer.BuyerName;
                    ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 1).Style.Font.FontColor = XLColor.Red;
                    ws.Range(rowIdx, 1, rowIdx, 15).Merge();
                    rowIdx++;

                    foreach (var item in buyer.Items)
                    {
                        ws.Cell(rowIdx, 1).SetValue(item.TransactionNumber);
                        ws.Cell(rowIdx, 2).SetValue(item.Date?.ToString("dd-MM-yyyy") ?? "");
                        ws.Cell(rowIdx, 3).SetValue(item.ContractNumber);
                        ws.Cell(rowIdx, 4).SetValue(item.InvoiceNumber);
                        ws.Cell(rowIdx, 5).SetValue(item.SupplierPurchaseNo);
                        ws.Cell(rowIdx, 6).SetValue(item.Qty).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 7).SetValue(item.Description);
                        ws.Cell(rowIdx, 8).SetValue(item.Rate).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 9).SetValue(item.Amount).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 10).SetValue(item.ReceivedPaid > 0 ? $"({item.ReceivedPaid:N2})" : "0.00");
                        ws.Cell(rowIdx, 11).SetValue(item.Balance).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 12).SetValue(item.ContDays);
                        ws.Cell(rowIdx, 13).SetValue(item.TotalDays);
                        ws.Cell(rowIdx, 14).SetValue(item.PdcAmount > 0 ? item.PdcAmount : 0).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 15).SetValue(item.PdcDate?.ToString("dd-MM-yyyy") ?? "");

                        for (int col = 1; col <= 15; col++)
                        {
                            ws.Cell(rowIdx, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                        }
                        rowIdx++;
                    }

                    ws.Cell(rowIdx, 1).Value = "SUPPLIER WISE SUB - TOTAL :";
                    ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                    ws.Range(rowIdx, 1, rowIdx, 8).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    
                    ws.Cell(rowIdx, 9).SetValue(buyer.SubTotalAmount).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 10).SetValue(buyer.SubTotalReceived > 0 ? $"({buyer.SubTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
                    ws.Cell(rowIdx, 11).SetValue(buyer.SubTotalBalance).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 14).SetValue(buyer.SubTotalPdc).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 14).Style.NumberFormat.Format = "#,##0.00";

                    ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    rowIdx++;
                }

                ws.Cell(rowIdx, 1).Value = "SUB - TOTAL OF BUYER / BROKER :";
                ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                ws.Range(rowIdx, 1, rowIdx, 8).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(rowIdx, 9).SetValue(broker.SubTotalAmount).Style.Font.Bold = true;
                ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(rowIdx, 10).SetValue(broker.SubTotalReceived > 0 ? $"({broker.SubTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
                ws.Cell(rowIdx, 11).SetValue(broker.SubTotalBalance).Style.Font.Bold = true;
                ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";

                ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                rowIdx += 2;
            }

            ws.Cell(rowIdx, 1).Value = "GRAND - TOTAL :";
            ws.Cell(rowIdx, 1).Style.Font.Bold = true;
            ws.Range(rowIdx, 1, rowIdx, 10).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(rowIdx, 11).SetValue(GrandTotalBalance).Style.Font.Bold = true;
            ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
            ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.BottomBorder = XLBorderStyleValues.Double;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            string fileName = $"Supplier_Aging_{targetDate:yyyyMMdd}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
