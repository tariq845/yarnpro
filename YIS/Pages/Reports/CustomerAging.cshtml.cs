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
    public class CustomerAgingModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CustomerAgingModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public DateTime? AsOfDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? CustomerId { get; set; }

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

        public List<SelectListItem> CustomerDropdown { get; set; } = new();
        public List<SelectListItem> BrokerDropdown { get; set; } = new();

        // Index / Stock-style Summary list
        public List<CustomerSummaryItem> CustomerSummaries { get; set; } = new();

        // Detailed Drill-down report
        public List<BrokerAgingGroup> GroupedReport { get; set; } = new();

        // Selected Customer info when viewing detail
        public CustomerMaster? SelectedCustomer { get; set; }

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

        public class CustomerSummaryItem
        {
            public long CustomerId { get; set; }
            public string CustomerName { get; set; } = "";
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
            public string? TransactionUrl { get; set; }
            public DateTime? Date { get; set; }
            public long? ContractId { get; set; }
            public string ContractNumber { get; set; } = "";
            public string? ContractUrl { get; set; }
            public string InvoiceNumber { get; set; } = "";
            public string? InvoiceViewUrl { get; set; }
            public string RefCode { get; set; } = "";
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
            public string? BuyerName { get; set; }
            public string? BrokerName { get; set; }
            public List<VoucherSettlementInfo> AppliedVouchers { get; set; } = new();
        }

        public class BuyerAgingGroup
        {
            public string BuyerName { get; set; } = "";
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

            // 1. Get Customers
            var customers = await _context.CustomerMasters
                .Where(c => c.UcpId == ucpId)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            CustomerDropdown = customers.Select(c => new SelectListItem
            {
                Value = c.CustomerId.ToString(),
                Text = c.CustomerName ?? "Unknown"
            }).ToList();

            var customerDict = customers.ToDictionary(c => c.CustomerId, c => c);

            if (CustomerId.HasValue && customerDict.ContainsKey(CustomerId.Value))
            {
                SelectedCustomer = customerDict[CustomerId.Value];
            }

            // Only include entries that are POSTED to General Ledger (TRANS table)
            var postedEntryIds = (await _context.Transactions
                .Where(t => t.UcpId == ucpId && t.EntryId.HasValue)
                .Select(t => t.EntryId!.Value)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            // 2. Get Sales Invoices (SAL) up to targetDate (Only GL-posted)
            var salesQuery = _context.Entries
                .Where(e => e.EntryType == "SAL" && e.UcpId == ucpId && e.EntryDate <= targetDate);

            if (CustomerId.HasValue)
            {
                string partyStr = CustomerId.Value.ToString();
                salesQuery = salesQuery.Where(e => e.EntryAttribute1 == partyStr);
            }

            var allSalesInvoices = await salesQuery
                .OrderBy(e => e.EntryDate)
                .ThenBy(e => e.EntryId)
                .ToListAsync();

            var salesInvoices = allSalesInvoices
                .Where(e => postedEntryIds.Contains(e.EntryId))
                .ToList();

            var salesIds = salesInvoices.Select(s => s.EntryId).ToList();

            // 3. Child Line Items (SALL)
            var childLines = await _context.Entries
                .Where(e => e.EntryType == "SALL" && salesIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(salesInvoices.Where(s => s.ItemId.HasValue).Select(s => s.ItemId!.Value))
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

            // 4. SSD Mapping (Contracts)
            var ssdIds = salesInvoices.Where(s => s.EntryRefId.HasValue).Select(s => s.EntryRefId!.Value).Distinct().ToList();
            var ssdRefNos = salesInvoices.Where(s => s.EntryRefNo.HasValue).Select(s => s.EntryRefNo!.Value).Distinct().ToList();

            var ssdEntries = await _context.Entries
                .Where(e => e.EntryType == "SSD" && (ssdIds.Contains(e.EntryId) || ssdRefNos.Contains(e.EntryNumber ?? 0) || ssdRefNos.Contains(e.EntryId)))
                .ToListAsync();

            var ssdMap = ssdEntries
                .GroupBy(e => e.EntryId)
                .ToDictionary(g => g.Key, g => g.First().EntryNumber ?? 0);

            var ssdIdByNumber = ssdEntries
                .Where(e => e.EntryNumber.HasValue)
                .GroupBy(e => e.EntryNumber!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EntryId).First().EntryId);

            // 5. Total Receipts per Customer up to targetDate (RV, CN - Only GL-posted)
            var custIdStrings = salesInvoices.Select(s => s.EntryAttribute1).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();

            var allRvEntries = await _context.Entries
                .Where(e => (e.EntryType == "RV" || e.EntryType == "CN") && e.UcpId == ucpId && e.EntryDate <= targetDate)
                .OrderBy(e => e.EntryDate)
                .ThenBy(e => e.EntryId)
                .ToListAsync();

            var rvEntries = allRvEntries
                .Where(e => postedEntryIds.Contains(e.EntryId))
                .Where(e =>
                {
                    string? party = e.EntryType == "CN"
                        ? (!string.IsNullOrEmpty(e.EntryAttribute1) ? e.EntryAttribute1 : e.EntryAttribute2)
                        : e.EntryAttribute2;
                    return !string.IsNullOrEmpty(party) && custIdStrings.Contains(party);
                })
                .ToList();

            var vouchersByCustomer = new Dictionary<string, List<ActiveVoucherItem>>();
            foreach (var v in rvEntries)
            {
                string? party = v.EntryType == "CN"
                    ? (!string.IsNullOrEmpty(v.EntryAttribute1) ? v.EntryAttribute1 : v.EntryAttribute2)
                    : v.EntryAttribute2;
                if (string.IsNullOrEmpty(party)) continue;

                if (!vouchersByCustomer.ContainsKey(party))
                    vouchersByCustomer[party] = new List<ActiveVoucherItem>();

                vouchersByCustomer[party].Add(new ActiveVoucherItem
                {
                    EntryId = v.EntryId,
                    VoucherNumber = v.EntryNumber?.ToString() ?? v.EntryId.ToString(),
                    VoucherType = v.EntryType ?? "RV",
                    VoucherDate = v.EntryDate,
                    TotalAmount = v.Amount ?? 0,
                    RemainingAmount = v.Amount ?? 0
                });
            }

            // Extract Brokers
            var brokers = salesInvoices
                .Select(s => string.IsNullOrWhiteSpace(s.EntryAttribute4) ? "DIRECT / GENERAL SALE" : s.EntryAttribute4.Trim())
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            BrokerDropdown = brokers.Select(b => new SelectListItem { Value = b, Text = b }).ToList();

            if (!string.IsNullOrEmpty(BrokerFilter))
            {
                salesInvoices = salesInvoices.Where(s => (string.IsNullOrWhiteSpace(s.EntryAttribute4) ? "DIRECT / GENERAL SALE" : s.EntryAttribute4.Trim()) == BrokerFilter).ToList();
            }

            // 6. FIFO Settlement
            var processedRows = new List<AgingItemRow>();
            var groupedByCust = salesInvoices.GroupBy(s => s.EntryAttribute1 ?? "0");

            foreach (var custGroup in groupedByCust)
            {
                string custKey = custGroup.Key;
                var custVouchers = vouchersByCustomer.ContainsKey(custKey) ? vouchersByCustomer[custKey] : new List<ActiveVoucherItem>();

                long.TryParse(custKey, out long parsedCustId);
                var cMaster = customerDict.ContainsKey(parsedCustId) ? customerDict[parsedCustId] : null;
                string custName = cMaster?.CustomerName ?? "Customer #" + custKey;

                foreach (var inv in custGroup.OrderBy(i => i.EntryDate).ThenBy(i => i.EntryId))
                {
                    double invAmount = inv.Amount ?? 0;
                    double paidForThis = 0;
                    var appliedVouchers = new List<VoucherSettlementInfo>();

                    double needed = invAmount;
                    foreach (var v in custVouchers)
                    {
                        if (v.RemainingAmount <= 0) continue;
                        if (needed <= 0) break;

                        double availableForThis = v.RemainingAmount;
                        double consumedBefore = v.TotalAmount - availableForThis;

                        var earlierList = v.SettledInvoices.Select(si => new SettledInvoiceRecord
                        {
                            EntryId = si.EntryId,
                            InvoiceNumber = si.InvoiceNumber,
                            Date = si.Date,
                            AmountApplied = si.AmountApplied
                        }).ToList();

                        if (v.RemainingAmount >= needed)
                        {
                            double applied = needed;
                            paidForThis += applied;
                            v.RemainingAmount -= applied;
                            appliedVouchers.Add(new VoucherSettlementInfo
                            {
                                EntryId = v.EntryId,
                                VoucherNumber = v.VoucherNumber,
                                VoucherType = v.VoucherType,
                                VoucherDate = v.VoucherDate,
                                TotalVoucherAmount = v.TotalAmount,
                                ConsumedBeforeThis = consumedBefore,
                                AvailableForThis = availableForThis,
                                AmountApplied = applied,
                                RemainingAfterThis = v.RemainingAmount,
                                EarlierBills = earlierList
                            });

                            v.SettledInvoices.Add(new SettledInvoiceRecord
                            {
                                EntryId = inv.EntryId,
                                InvoiceNumber = string.IsNullOrWhiteSpace(inv.EntryAttribute2) ? (inv.EntryNumber?.ToString() ?? inv.EntryId.ToString()) : inv.EntryAttribute2.Trim(),
                                Date = inv.EntryDate,
                                AmountApplied = applied
                            });

                            needed = 0;
                            break;
                        }
                        else
                        {
                            double apply = v.RemainingAmount;
                            paidForThis += apply;
                            needed -= apply;
                            appliedVouchers.Add(new VoucherSettlementInfo
                            {
                                EntryId = v.EntryId,
                                VoucherNumber = v.VoucherNumber,
                                VoucherType = v.VoucherType,
                                VoucherDate = v.VoucherDate,
                                TotalVoucherAmount = v.TotalAmount,
                                ConsumedBeforeThis = consumedBefore,
                                AvailableForThis = availableForThis,
                                AmountApplied = apply,
                                RemainingAfterThis = 0,
                                EarlierBills = earlierList
                            });

                            v.SettledInvoices.Add(new SettledInvoiceRecord
                            {
                                EntryId = inv.EntryId,
                                InvoiceNumber = string.IsNullOrWhiteSpace(inv.EntryAttribute2) ? (inv.EntryNumber?.ToString() ?? inv.EntryId.ToString()) : inv.EntryAttribute2.Trim(),
                                Date = inv.EntryDate,
                                AmountApplied = apply
                            });

                            v.RemainingAmount = 0;
                        }
                    }

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
                    double gstAmount = 0;
                    double amountWithoutGst = 0;

                    if (lines.Any())
                    {
                        totalQty = lines.Sum(l => l.Qty ?? 0);
                        gstAmount = lines.Sum(l => l.TaxAmount ?? 0);
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
                        gstAmount = inv.TaxAmount ?? 0;
                        amountWithoutGst = invAmount - gstAmount;
                        if (inv.ItemId.HasValue && itemsMap.ContainsKey(inv.ItemId.Value))
                        {
                            desc = itemsMap[inv.ItemId.Value];
                        }
                    }

                    if (gstAmount <= 0 && inv.TaxAmount.HasValue && inv.TaxAmount > 0)
                    {
                        gstAmount = inv.TaxAmount.Value;
                    }

                    if (amountWithoutGst <= 0)
                    {
                        amountWithoutGst = invAmount - gstAmount;
                    }
                    if (amountWithoutGst < 0) amountWithoutGst = 0;

                    double netAmount = amountWithoutGst + gstAmount;
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

                    if (inv.EntryRefId.HasValue && ssdMap.ContainsKey(inv.EntryRefId.Value))
                    {
                        contractNo = ssdMap[inv.EntryRefId.Value];
                        contractId = inv.EntryRefId.Value;
                    }
                    else if (inv.EntryRefNo.HasValue && ssdMap.ContainsKey(inv.EntryRefNo.Value))
                    {
                        contractNo = ssdMap[inv.EntryRefNo.Value];
                        contractId = inv.EntryRefNo.Value;
                    }
                    else if (inv.EntryRefNo.HasValue && ssdIdByNumber.ContainsKey(inv.EntryRefNo.Value))
                    {
                        contractNo = inv.EntryRefNo.Value;
                        contractId = ssdIdByNumber[inv.EntryRefNo.Value];
                    }
                    else if (inv.EntryRefNo.HasValue)
                    {
                        contractNo = inv.EntryRefNo.Value;
                        contractId = inv.EntryRefNo.Value;
                    }

                    string? contractUrl = contractId.HasValue && contractId.Value > 0 ? $"/Sales/Soda?id={contractId.Value}" : null;
                    string contractNumberStr = contractNo > 0 ? contractNo.ToString("D7") : (contractId.HasValue && contractId.Value > 0 ? contractId.Value.ToString("D7") : "-");

                    string broker = string.IsNullOrWhiteSpace(inv.EntryAttribute4) ? "DIRECT / GENERAL SALE" : inv.EntryAttribute4.Trim();

                    // Optional Soda filter
                    if (!string.IsNullOrWhiteSpace(SodaNo) && contractNo.ToString() != SodaNo.Trim() && $"SSD-{contractNo}" != SodaNo.Trim())
                    {
                        continue;
                    }

                    var row = new AgingItemRow
                    {
                        EntryId = inv.EntryId,
                        TransactionNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString("D7") : inv.EntryId.ToString("D7"),
                        TransactionUrl = $"/Sales/Entry?id={inv.EntryId}",
                        Date = inv.EntryDate,
                        ContractId = contractId,
                        ContractUrl = contractUrl,
                        ContractNumber = contractNumberStr,
                        InvoiceNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString() : inv.EntryId.ToString(),
                        InvoiceViewUrl = $"/Sales/InvoiceView/{inv.EntryId}",
                        RefCode = !string.IsNullOrWhiteSpace(inv.EntryRefCode) ? inv.EntryRefCode.Trim() : "-",
                        Qty = totalQty,
                        Description = desc,
                        Rate = rate,
                        AmountWithoutGst = amountWithoutGst,
                        GstAmount = gstAmount,
                        NetAmount = netAmount,
                        Amount = amountWithoutGst,
                        ReceivedPaid = paidForThis,
                        Balance = balance,
                        ContDays = contDays,
                        TotalDays = totalDays,
                        PdcAmount = 0,
                        BuyerName = custName,
                        BrokerName = broker,
                        AppliedVouchers = appliedVouchers
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
                    (!string.IsNullOrEmpty(r.RefCode) && r.RefCode.ToLower().Contains(term)) ||
                    (r.BuyerName != null && r.BuyerName.ToLower().Contains(term)) ||
                    (r.BrokerName != null && r.BrokerName.ToLower().Contains(term)) ||
                    r.Description.ToLower().Contains(term)
                ).ToList();
            }

            // Calculate Slabs across all rows (unaffected by slab filter for top KPI cards)
            Slab_0_30 = processedRows.Where(r => r.TotalDays <= 30).Sum(r => r.Balance);
            Slab_31_60 = processedRows.Where(r => r.TotalDays > 30 && r.TotalDays <= 60).Sum(r => r.Balance);
            Slab_61_90 = processedRows.Where(r => r.TotalDays > 60 && r.TotalDays <= 90).Sum(r => r.Balance);
            Slab_91_120 = processedRows.Where(r => r.TotalDays > 90 && r.TotalDays <= 120).Sum(r => r.Balance);
            Slab_120_Plus = processedRows.Where(r => r.TotalDays > 120).Sum(r => r.Balance);

            // Apply Slab Filter if selected
            if (!string.IsNullOrEmpty(SlabFilter))
            {
                switch (SlabFilter)
                {
                    case "0-30":
                        processedRows = processedRows.Where(r => r.TotalDays <= 30).ToList();
                        break;
                    case "31-60":
                        processedRows = processedRows.Where(r => r.TotalDays > 30 && r.TotalDays <= 60).ToList();
                        break;
                    case "61-90":
                        processedRows = processedRows.Where(r => r.TotalDays > 60 && r.TotalDays <= 90).ToList();
                        break;
                    case "90+":
                        processedRows = processedRows.Where(r => r.TotalDays > 90).ToList();
                        break;
                }
            }

            // Build Stock-style Customer Summaries
            CustomerSummaries = processedRows
                .GroupBy(r => r.BuyerName ?? "Unknown")
                .Select(cg =>
                {
                    var items = cg.ToList();
                    var first = items.First();
                    long cId = 0;
                    var matchCust = customers.FirstOrDefault(c => (c.CustomerName ?? "") == cg.Key);
                    if (matchCust != null) cId = matchCust.CustomerId;

                    return new CustomerSummaryItem
                    {
                        CustomerId = cId,
                        CustomerName = cg.Key,
                        Contact = matchCust?.CustomerContact,
                        City = matchCust?.Province,
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
                .GroupBy(r => r.BrokerName ?? "DIRECT / GENERAL SALE")
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

            // Metrics from currently filtered GroupedReport
            GrandTotalAmountWithoutGst = GroupedReport.Sum(g => g.SubTotalAmountWithoutGst);
            GrandTotalGst = GroupedReport.Sum(g => g.SubTotalGst);
            GrandTotalNetAmount = GroupedReport.Sum(g => g.SubTotalNetAmount);
            GrandTotalAmount = GrandTotalAmountWithoutGst;
            GrandTotalReceived = GroupedReport.Sum(g => g.SubTotalReceived);
            GrandTotalBalance = GroupedReport.Sum(g => g.SubTotalBalance);
            TotalInvoicesCount = GroupedReport.Sum(g => g.Buyers.Sum(b => b.Items.Count));
        }

                public async Task<IActionResult> OnGetExportExcelAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            DateTime targetDate = AsOfDate?.Date ?? DateTime.Today;
            await LoadCompanyAndUserDataAsync(activeUcpId.Value);
            await LoadReportDataAsync(activeUcpId.Value);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Customer Aging");

            ws.Cell("A1").Value = CompanyName;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:Q1").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A2").Value = $"AGING REPORT (RECEIVABLES ONLY) UPTO {targetDate:dd-MM-yyyy}";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:Q2").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A3").Value = (BillStatus == "Cleared") ? "Cleared / Paid Bills Only" : (BillStatus == "All" || !UnClearedOnly ? "All Bills" : "UnCleared Bills Only");
            ws.Cell("Q3").Value = $"Date: {GeneratedOn:dd-MM-yyyy hh:mm tt}";

            string[] headers = {
                "TRANSACTION NUMBER", "DATE", "CONTRACT NUMBER", "INVOICE NUMBER", "REF CODE",
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
                    ws.Cell(rowIdx, 1).Value = buyer.BuyerName;
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
                        ws.Cell(rowIdx, 5).SetValue(item.RefCode);
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

                    ws.Cell(rowIdx, 1).Value = "BUYER WISE SUB - TOTAL :";
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

                ws.Cell(rowIdx, 1).Value = "SUB - TOTAL OF SELLER / BROKER :";
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
            string fileName = $"Customer_Aging_{targetDate:yyyyMMdd}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
