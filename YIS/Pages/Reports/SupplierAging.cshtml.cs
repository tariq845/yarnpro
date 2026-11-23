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
        public string BillStatus { get; set; } = "UnCleared"; // "UnCleared", "Cleared", "All"

        [BindProperty(SupportsGet = true)]
        public bool UnClearedOnly { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SodaNo { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SlabFilter { get; set; }

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
        public double GrandTotalAmountWithoutGst { get; set; }
        public double GrandTotalGst { get; set; }
        public double GrandTotalNetAmount { get; set; }
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

        public class SettledInvoiceRecord
        {
            public long EntryId { get; set; }
            public string InvoiceNumber { get; set; } = "";
            public DateTime? Date { get; set; }
            public double AmountApplied { get; set; }
        }

        public class VoucherSettlementInfo
        {
            public long EntryId { get; set; }
            public string VoucherNumber { get; set; } = "";
            public string VoucherType { get; set; } = "";
            public DateTime? VoucherDate { get; set; }
            public double TotalVoucherAmount { get; set; }
            public double ConsumedBeforeThis { get; set; }
            public double AvailableForThis { get; set; }
            public double AmountApplied { get; set; }
            public double RemainingAfterThis { get; set; }
            public List<SettledInvoiceRecord> EarlierBills { get; set; } = new();
        }

        private class ActiveVoucherItem
        {
            public long EntryId { get; set; }
            public string VoucherNumber { get; set; } = "";
            public string VoucherType { get; set; } = "";
            public DateTime? VoucherDate { get; set; }
            public double TotalAmount { get; set; }
            public double RemainingAmount { get; set; }
            public List<SettledInvoiceRecord> SettledInvoices { get; set; } = new();
        }

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
            public string? GstNo { get; set; }
            public string? NtnNo { get; set; }
        }

        public class AgingItemRow
        {
            public long EntryId { get; set; }
            public string TransactionNumber { get; set; } = "";
            public string? TransactionUrl { get; set; }
            public DateTime? Date { get; set; }
            public long? ContractId { get; set; }
            public string ContractNumber { get; set; } = "";
            public string? ContractUrl { get; set; }
            public string InvoiceNumber { get; set; } = "";
            public string SupplierPurchaseNo { get; set; } = "";
            public double Qty { get; set; }
            public string Description { get; set; } = "";
            public double Rate { get; set; }
            public double AmountWithoutGst { get; set; }
            public double GstAmount { get; set; }
            public double NetAmount { get; set; }
            public double Amount { get; set; }
            public double ReceivedPaid { get; set; }
            public double Balance { get; set; }
            public int ContDays { get; set; }
            public int TotalDays { get; set; }
            public double PdcAmount { get; set; }
            public DateTime? PdcDate { get; set; }
            public string? BuyerName { get; set; } // Supplier / Seller
            public string? GstNo { get; set; }
            public string? NtnNo { get; set; }
            public string? BrokerName { get; set; }
            public List<VoucherSettlementInfo> AppliedVouchers { get; set; } = new();
        }

        public class BuyerAgingGroup
        {
            public string BuyerName { get; set; } = "";
            public string? GstNo { get; set; }
            public string? NtnNo { get; set; }
            public List<AgingItemRow> Items { get; set; } = new();
            public double SubTotalAmountWithoutGst => Items.Sum(i => i.AmountWithoutGst);
            public double SubTotalGst => Items.Sum(i => i.GstAmount);
            public double SubTotalNetAmount => Items.Sum(i => i.NetAmount);
            public double SubTotalAmount => Items.Sum(i => i.Amount);
            public double SubTotalReceived => Items.Sum(i => i.ReceivedPaid);
            public double SubTotalBalance => Items.Sum(i => i.Balance);
            public double SubTotalPdc => Items.Sum(i => i.PdcAmount);
        }

        public class BrokerAgingGroup
        {
            public string BrokerName { get; set; } = "";
            public List<BuyerAgingGroup> Buyers { get; set; } = new();
            public double SubTotalAmountWithoutGst => Buyers.Sum(b => b.SubTotalAmountWithoutGst);
            public double SubTotalGst => Buyers.Sum(b => b.SubTotalGst);
            public double SubTotalNetAmount => Buyers.Sum(b => b.SubTotalNetAmount);
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

            // Synchronize BillStatus and UnClearedOnly
            if (!string.IsNullOrEmpty(BillStatus))
            {
                if (BillStatus == "Cleared") UnClearedOnly = false;
                else if (BillStatus == "All") UnClearedOnly = false;
                else if (BillStatus == "UnCleared") UnClearedOnly = true;
            }
            else
            {
                BillStatus = UnClearedOnly ? "UnCleared" : "All";
            }

            // 4. PSD Mapping (Contracts)
            var psdRefIds = purchaseInvoices.Where(p => p.EntryRefNo.HasValue).Select(p => p.EntryRefNo!.Value)
                .Concat(purchaseInvoices.Where(p => p.EntryRefId.HasValue).Select(p => p.EntryRefId!.Value))
                .Distinct().ToList();

            var psdEntries = await _context.Entries
                .Where(e => e.EntryType == "PSD" && (psdRefIds.Contains(e.EntryId) || psdRefIds.Contains(e.EntryNumber ?? 0)))
                .ToListAsync();

            var psdMap = psdEntries
                .GroupBy(e => e.EntryId)
                .ToDictionary(g => g.Key, g => g.First().EntryNumber ?? 0);

            var psdIdByNumber = psdEntries
                .Where(e => e.EntryNumber.HasValue)
                .GroupBy(e => e.EntryNumber!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EntryId).First().EntryId);

            // 5. Total Payments per Supplier up to targetDate (PV, DN - Only GL-posted)
            var suppIdStrings = purchaseInvoices.Select(p => p.EntryAttribute1).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();

            var allPvEntries = await _context.Entries
                .Where(e => (e.EntryType == "PV" || e.EntryType == "DN") && e.UcpId == ucpId && e.EntryDate <= targetDate)
                .OrderBy(e => e.EntryDate)
                .ThenBy(e => e.EntryId)
                .ToListAsync();

            Func<Entry, string> getVoucherSuppId = (v) =>
                v.EntryType == "DN"
                    ? (!string.IsNullOrEmpty(v.EntryAttribute1) ? v.EntryAttribute1 : (v.EntryAttribute2 ?? ""))
                    : (v.EntryAttribute2 ?? "");

            var pvEntries = allPvEntries
                .Where(e => postedEntryIds.Contains(e.EntryId) && suppIdStrings.Contains(getVoucherSuppId(e)))
                .ToList();

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
                long.TryParse(suppKey, out long parsedSuppId);
                var sMaster = supplierDict.ContainsKey(parsedSuppId) ? supplierDict[parsedSuppId] : null;
                string suppName = sMaster?.SupplierName ?? "Supplier #" + suppKey;

                var supplierVouchers = pvEntries
                    .Where(p => getVoucherSuppId(p) == suppKey)
                    .Select(p => new ActiveVoucherItem
                    {
                        EntryId = p.EntryId,
                        VoucherNumber = p.EntryNumber.HasValue ? p.EntryNumber.Value.ToString() : p.EntryId.ToString(),
                        VoucherType = p.EntryType ?? "PV",
                        VoucherDate = p.EntryDate,
                        TotalAmount = p.Amount ?? 0,
                        RemainingAmount = p.Amount ?? 0
                    })
                    .ToList();

                foreach (var inv in suppGroup.OrderBy(i => i.EntryDate).ThenBy(i => i.EntryId))
                {
                    double.TryParse(inv.EntryAttribute3, out double netAmt);
                    double invAmount = netAmt > 0 ? netAmt : (inv.Amount ?? 0);
                    double needed = invAmount;
                    var appliedList = new List<VoucherSettlementInfo>();

                    foreach (var v in supplierVouchers)
                    {
                        if (needed <= 0.001) break;
                        if (v.RemainingAmount <= 0.001) continue;

                        double apply = Math.Min(needed, v.RemainingAmount);
                        double availableForThis = v.RemainingAmount;
                        double consumedBefore = v.TotalAmount - availableForThis;
                        double remainingAfter = availableForThis - apply;

                        var earlierList = v.SettledInvoices.Select(si => new SettledInvoiceRecord
                        {
                            EntryId = si.EntryId,
                            InvoiceNumber = si.InvoiceNumber,
                            Date = si.Date,
                            AmountApplied = si.AmountApplied
                        }).ToList();

                        appliedList.Add(new VoucherSettlementInfo
                        {
                            EntryId = v.EntryId,
                            VoucherNumber = v.VoucherNumber,
                            VoucherType = v.VoucherType,
                            VoucherDate = v.VoucherDate,
                            TotalVoucherAmount = v.TotalAmount,
                            ConsumedBeforeThis = consumedBefore,
                            AvailableForThis = availableForThis,
                            AmountApplied = apply,
                            RemainingAfterThis = remainingAfter,
                            EarlierBills = earlierList
                        });

                        v.RemainingAmount -= apply;
                        needed -= apply;

                        v.SettledInvoices.Add(new SettledInvoiceRecord
                        {
                            EntryId = inv.EntryId,
                            InvoiceNumber = string.IsNullOrWhiteSpace(inv.EntryAttribute2) ? (inv.EntryNumber?.ToString() ?? inv.EntryId.ToString()) : inv.EntryAttribute2.Trim(),
                            Date = inv.EntryDate,
                            AmountApplied = apply
                        });
                    }

                    double paidForThis = appliedList.Sum(a => a.AmountApplied);
                    double balance = invAmount - paidForThis;

                    if (BillStatus == "Cleared")
                    {
                        if (balance > 0.01) continue; // Only show cleared / paid invoices!
                    }
                    else if (BillStatus == "UnCleared" || UnClearedOnly)
                    {
                        if (balance <= 0.01) continue; // Only show uncleared / outstanding invoices!
                    }

                    var lines = childLines.Where(c => c.EntryRefId == inv.EntryId).ToList();
                    string desc = "";
                    double totalQty = 0;
                    double rate = inv.Rate ?? 0;
                    double invGst = inv.TaxAmount ?? 0;
                    double amountWithoutGst = 0;

                    if (lines.Any())
                    {
                        totalQty = lines.Sum(l => l.Qty ?? 0);
                        if (invGst == 0) invGst = lines.Sum(l => l.TaxAmount ?? 0);
                        amountWithoutGst = lines.Sum(l => l.Amount ?? 0);

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
                        amountWithoutGst = invAmount - invGst;
                        if (inv.ItemId.HasValue && itemsMap.ContainsKey(inv.ItemId.Value))
                        {
                            desc = itemsMap[inv.ItemId.Value];
                        }
                    }

                    if (amountWithoutGst <= 0)
                    {
                        amountWithoutGst = invAmount - invGst;
                    }
                    if (amountWithoutGst < 0) amountWithoutGst = 0;

                    double netAmount = amountWithoutGst + invGst;
                    if (netAmount <= 0) netAmount = invAmount;

                    if (!string.IsNullOrWhiteSpace(inv.EntryRemarks))
                    {
                        desc = string.IsNullOrWhiteSpace(desc) ? inv.EntryRemarks : desc + " (" + inv.EntryRemarks + ")";
                    }

                    int contDays = inv.EntryTermsDays ?? 0;
                    int totalDays = inv.EntryDate.HasValue ? (int)(targetDate - inv.EntryDate.Value.Date).TotalDays : 0;
                    if (totalDays < 0) totalDays = 0;

                    long contractNo = 0;
                    long? contractId = null;

                    if (inv.EntryRefNo.HasValue && psdMap.ContainsKey(inv.EntryRefNo.Value))
                    {
                        contractNo = psdMap[inv.EntryRefNo.Value];
                        contractId = inv.EntryRefNo.Value;
                    }
                    else if (inv.EntryRefId.HasValue && psdMap.ContainsKey(inv.EntryRefId.Value))
                    {
                        contractNo = psdMap[inv.EntryRefId.Value];
                        contractId = inv.EntryRefId.Value;
                    }
                    else if (inv.EntryRefNo.HasValue && psdIdByNumber.ContainsKey(inv.EntryRefNo.Value))
                    {
                        contractNo = inv.EntryRefNo.Value;
                        contractId = psdIdByNumber[inv.EntryRefNo.Value];
                    }
                    else if (inv.EntryRefNo.HasValue)
                    {
                        contractNo = inv.EntryRefNo.Value;
                        contractId = inv.EntryRefNo.Value;
                    }

                    string? contractUrl = contractId.HasValue && contractId.Value > 0 ? $"/Purchase/Soda?id={contractId.Value}" : null;
                    string contractNumberStr = contractNo > 0 ? contractNo.ToString("D7") : (contractId.HasValue && contractId.Value > 0 ? contractId.Value.ToString("D7") : "-");

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
                        TransactionUrl = $"/Purchase/Entry?id={inv.EntryId}",
                        Date = inv.EntryDate,
                        ContractId = contractId,
                        ContractUrl = contractUrl,
                        ContractNumber = contractNumberStr,
                        InvoiceNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString() : inv.EntryId.ToString(),
                        SupplierPurchaseNo = !string.IsNullOrWhiteSpace(inv.EntryRefCode) ? inv.EntryRefCode.Trim() : "-",
                        Qty = totalQty,
                        Description = desc,
                        Rate = rate,
                        AmountWithoutGst = amountWithoutGst,
                        GstAmount = invGst,
                        NetAmount = netAmount,
                        Amount = amountWithoutGst,
                        ReceivedPaid = paidForThis,
                        Balance = balance,
                        ContDays = contDays,
                        TotalDays = totalDays,
                        PdcAmount = 0,
                        BuyerName = suppName,
                        GstNo = sMaster?.SupplierStrNo,
                        NtnNo = sMaster?.SupplierNtnNo,
                        BrokerName = broker,
                        AppliedVouchers = appliedList
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
                    (!string.IsNullOrEmpty(r.GstNo) && r.GstNo.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(r.NtnNo) && r.NtnNo.ToLower().Contains(term)) ||
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
                        GstNo = matchSupp?.SupplierStrNo,
                        NtnNo = matchSupp?.SupplierNtnNo,
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
                                   GstNo = ug.FirstOrDefault(x => !string.IsNullOrEmpty(x.GstNo))?.GstNo,
                                   NtnNo = ug.FirstOrDefault(x => !string.IsNullOrEmpty(x.NtnNo))?.NtnNo,
                                   Items = ug.OrderBy(i => i.Date).ThenBy(i => i.EntryId).ToList()
                               }).ToList()
                })
                .OrderBy(bg => bg.BrokerName)
                .ToList();

            // Slabs
            var allRows = GroupedReport.SelectMany(g => g.Buyers.SelectMany(b => b.Items)).ToList();
            GrandTotalAmountWithoutGst = allRows.Sum(r => r.AmountWithoutGst);
            GrandTotalGst = allRows.Sum(r => r.GstAmount);
            GrandTotalNetAmount = allRows.Sum(r => r.NetAmount);
            GrandTotalAmount = GrandTotalAmountWithoutGst;
            GrandTotalReceived = allRows.Sum(r => r.ReceivedPaid);
            GrandTotalBalance = allRows.Sum(r => r.Balance);
            GrandTotalPdc = allRows.Sum(r => r.PdcAmount);
            TotalInvoicesCount = allRows.Count;

            Slab_0_30 = allRows.Where(r => r.TotalDays <= 30).Sum(r => r.Balance);
            Slab_31_60 = allRows.Where(r => r.TotalDays > 30 && r.TotalDays <= 60).Sum(r => r.Balance);
            Slab_61_90 = allRows.Where(r => r.TotalDays > 60 && r.TotalDays <= 90).Sum(r => r.Balance);
            Slab_91_120 = allRows.Where(r => r.TotalDays > 90 && r.TotalDays <= 120).Sum(r => r.Balance);
            Slab_120_Plus = allRows.Where(r => r.TotalDays > 120).Sum(r => r.Balance);

            // Apply SlabFilter if requested
            if (!string.IsNullOrWhiteSpace(SlabFilter))
            {
                GroupedReport = GroupedReport.Select(broker => new BrokerAgingGroup
                {
                    BrokerName = broker.BrokerName,
                    Buyers = broker.Buyers.Select(buyer => new BuyerAgingGroup
                    {
                        BuyerName = buyer.BuyerName,
                        Items = buyer.Items.Where(r =>
                            (SlabFilter == "0-30" && r.TotalDays <= 30) ||
                            (SlabFilter == "31-60" && r.TotalDays > 30 && r.TotalDays <= 60) ||
                            (SlabFilter == "61-90" && r.TotalDays > 60 && r.TotalDays <= 90) ||
                            (SlabFilter == "90+" && r.TotalDays > 90)
                        ).ToList()
                    }).Where(b => b.Items.Any()).ToList()
                }).Where(g => g.Buyers.Any()).ToList();

                SupplierSummaries = SupplierSummaries.Where(s =>
                    (SlabFilter == "0-30" && s.Slab0_30 > 0) ||
                    (SlabFilter == "31-60" && s.Slab31_60 > 0) ||
                    (SlabFilter == "61-90" && s.Slab61_90 > 0) ||
                    (SlabFilter == "90+" && s.Slab90Plus > 0)
                ).ToList();
            }
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
            ws.Range("A1:Q1").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A2").Value = $"AGING REPORT (PAYABLES ONLY) UPTO {targetDate:dd-MM-yyyy}";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:Q2").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A3").Value = (BillStatus == "Cleared") ? "Cleared / Paid Bills Only" : (BillStatus == "All" || !UnClearedOnly ? "All Bills" : "UnCleared Bills Only");
            ws.Cell("Q3").Value = $"Date: {GeneratedOn:dd-MM-yyyy hh:mm tt}";

            string[] headers = {
                "TRANSACTION NUMBER", "DATE", "CONTRACT NUMBER", "INVOICE NUMBER", "SUPPLIER INV NO",
                "QTY", "DESCRIPTION", "RATE", "AMOUNT (EXCL. GST)", "GST", "NET AMOUNT",
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
                ws.Range(rowIdx, 1, rowIdx, 17).Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f4f8");
                rowIdx++;

                foreach (var buyer in broker.Buyers)
                {
                    string buyerTitle = buyer.BuyerName;
                    if (!string.IsNullOrEmpty(buyer.GstNo)) buyerTitle += $" | GST: {buyer.GstNo}";
                    if (!string.IsNullOrEmpty(buyer.NtnNo)) buyerTitle += $" | NTN: {buyer.NtnNo}";
                    ws.Cell(rowIdx, 1).Value = buyerTitle;
                    ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 1).Style.Font.FontColor = XLColor.Red;
                    ws.Range(rowIdx, 1, rowIdx, 17).Merge();
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
                        ws.Cell(rowIdx, 9).SetValue(item.AmountWithoutGst).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 10).SetValue(item.GstAmount).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 11).SetValue(item.NetAmount).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 12).SetValue(item.ReceivedPaid > 0 ? $"({item.ReceivedPaid:N2})" : "0.00");
                        ws.Cell(rowIdx, 13).SetValue(item.Balance).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 14).SetValue(item.ContDays);
                        ws.Cell(rowIdx, 15).SetValue(item.TotalDays);
                        ws.Cell(rowIdx, 16).SetValue(item.PdcAmount > 0 ? item.PdcAmount : 0).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 17).SetValue(item.PdcDate?.ToString("dd-MM-yyyy") ?? "");

                        for (int col = 1; col <= 17; col++)
                        {
                            ws.Cell(rowIdx, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                        }
                        rowIdx++;
                    }

                    ws.Cell(rowIdx, 1).Value = "SUPPLIER WISE SUB - TOTAL :";
                    ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                    ws.Range(rowIdx, 1, rowIdx, 8).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    
                    ws.Cell(rowIdx, 9).SetValue(buyer.SubTotalAmountWithoutGst).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 10).SetValue(buyer.SubTotalGst).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 10).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 11).SetValue(buyer.SubTotalNetAmount).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 12).SetValue(buyer.SubTotalReceived > 0 ? $"({buyer.SubTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
                    ws.Cell(rowIdx, 13).SetValue(buyer.SubTotalBalance).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 13).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 16).SetValue(buyer.SubTotalPdc).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 16).Style.NumberFormat.Format = "#,##0.00";

                    ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    rowIdx++;
                }

                ws.Cell(rowIdx, 1).Value = "SUB - TOTAL OF BUYER / BROKER :";
                ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                ws.Range(rowIdx, 1, rowIdx, 8).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(rowIdx, 9).SetValue(broker.SubTotalAmountWithoutGst).Style.Font.Bold = true;
                ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(rowIdx, 10).SetValue(broker.SubTotalGst).Style.Font.Bold = true;
                ws.Cell(rowIdx, 10).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(rowIdx, 11).SetValue(broker.SubTotalNetAmount).Style.Font.Bold = true;
                ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(rowIdx, 12).SetValue(broker.SubTotalReceived > 0 ? $"({broker.SubTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
                ws.Cell(rowIdx, 13).SetValue(broker.SubTotalBalance).Style.Font.Bold = true;
                ws.Cell(rowIdx, 13).Style.NumberFormat.Format = "#,##0.00";

                ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                rowIdx += 2;
            }

            ws.Cell(rowIdx, 1).Value = "GRAND - TOTAL :";
            ws.Cell(rowIdx, 1).Style.Font.Bold = true;
            ws.Range(rowIdx, 1, rowIdx, 8).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(rowIdx, 9).SetValue(GrandTotalAmountWithoutGst).Style.Font.Bold = true;
            ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(rowIdx, 10).SetValue(GrandTotalGst).Style.Font.Bold = true;
            ws.Cell(rowIdx, 10).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(rowIdx, 11).SetValue(GrandTotalNetAmount).Style.Font.Bold = true;
            ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(rowIdx, 12).SetValue(GrandTotalReceived > 0 ? $"({GrandTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
            ws.Cell(rowIdx, 13).SetValue(GrandTotalBalance).Style.Font.Bold = true;
            ws.Cell(rowIdx, 13).Style.NumberFormat.Format = "#,##0.00";
            ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range(rowIdx, 1, rowIdx, 17).Style.Border.BottomBorder = XLBorderStyleValues.Double;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            string fileName = $"Supplier_Aging_{targetDate:yyyyMMdd}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
