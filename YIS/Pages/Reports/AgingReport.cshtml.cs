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
    public class AgingReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public AgingReportModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string AgingType { get; set; } = "Receivables"; // "Receivables" or "Payables"

        [BindProperty(SupportsGet = true)]
        public DateTime? AsOfDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public long? PartyId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? BrokerFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool UnClearedOnly { get; set; } = true;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SlabFilter { get; set; } // "0-30", "31-60", "61-90", "90+", or null/all

        public string CompanyName { get; set; } = "YARN PRO";
        public string UserName { get; set; } = "ADMIN";
        public DateTime GeneratedOn { get; set; } = DateTime.Now;

        public List<SelectListItem> PartyDropdown { get; set; } = new();
        public List<SelectListItem> BrokerDropdown { get; set; } = new();

        public List<BrokerAgingGroup> GroupedReport { get; set; } = new();

        // High level KPI metrics
        public double GrandTotalAmount { get; set; }
        public double GrandTotalGst { get; set; }
        public double GrandTotalReceived { get; set; }
        public double GrandTotalBalance { get; set; }
        public double GrandTotalPdc { get; set; }
        public int TotalInvoicesCount { get; set; }

        // Aging Slabs KPI
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
            public string VoucherType { get; set; } = ""; // "RV", "PV", "CN", "DN"
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

        public class AgingItemRow
        {
            public long EntryId { get; set; }
            public string EntryType { get; set; } = ""; // "SAL" or "PUR"
            public string TransactionNumber { get; set; } = "";
            public DateTime? Date { get; set; }
            public string ContractNumber { get; set; } = "";
            public string InvoiceNumber { get; set; } = "";
            public double Qty { get; set; }
            public string Description { get; set; } = "";
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double GstAmount { get; set; }
            public double ReceivedPaid { get; set; }
            public double Balance { get; set; }
            public int ContDays { get; set; }
            public int TotalDays { get; set; }
            public double PdcAmount { get; set; }
            public DateTime? PdcDate { get; set; }
            public string? BuyerName { get; set; }
            public string? SellerName { get; set; }
            public string? BrokerName { get; set; }
            public List<VoucherSettlementInfo> AppliedVouchers { get; set; } = new();
        }

        public class BuyerAgingGroup
        {
            public string BuyerName { get; set; } = "";
            public List<AgingItemRow> Items { get; set; } = new();
            public double SubTotalAmount => Items.Sum(i => i.Amount);
            public double SubTotalGst => Items.Sum(i => i.GstAmount);
            public double SubTotalReceived => Items.Sum(i => i.ReceivedPaid);
            public double SubTotalBalance => Items.Sum(i => i.Balance);
            public double SubTotalPdc => Items.Sum(i => i.PdcAmount);
        }

        public class BrokerAgingGroup
        {
            public string BrokerName { get; set; } = "";
            public List<BuyerAgingGroup> Buyers { get; set; } = new();
            public double SubTotalAmount => Buyers.Sum(b => b.SubTotalAmount);
            public double SubTotalGst => Buyers.Sum(b => b.SubTotalGst);
            public double SubTotalReceived => Buyers.Sum(b => b.SubTotalReceived);
            public double SubTotalBalance => Buyers.Sum(b => b.SubTotalBalance);
            public double SubTotalPdc => Buyers.Sum(b => b.SubTotalPdc);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanRead(HttpContext, path))
            {
                // Fallback allowed for reports if user has general read access
            }

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

            if (AgingType == "Receivables")
            {
                await LoadReceivablesAgingAsync(ucpId, targetDate);
            }
            else
            {
                await LoadPayablesAgingAsync(ucpId, targetDate);
            }

            // Calculate KPI Slabs BEFORE filtering by SlabFilter, so top cards always reflect true totals
            var allRows = GroupedReport.SelectMany(g => g.Buyers.SelectMany(b => b.Items)).ToList();
            GrandTotalAmount = allRows.Sum(r => r.Amount);
            GrandTotalGst = allRows.Sum(r => r.GstAmount);
            GrandTotalReceived = allRows.Sum(r => r.ReceivedPaid);
            GrandTotalBalance = allRows.Sum(r => r.Balance);
            GrandTotalPdc = allRows.Sum(r => r.PdcAmount);
            TotalInvoicesCount = allRows.Count;

            Slab_0_30 = allRows.Where(r => r.TotalDays <= 30).Sum(r => r.Balance);
            Slab_31_60 = allRows.Where(r => r.TotalDays > 30 && r.TotalDays <= 60).Sum(r => r.Balance);
            Slab_61_90 = allRows.Where(r => r.TotalDays > 60 && r.TotalDays <= 90).Sum(r => r.Balance);
            Slab_91_120 = allRows.Where(r => r.TotalDays > 90 && r.TotalDays <= 120).Sum(r => r.Balance);
            Slab_120_Plus = allRows.Where(r => r.TotalDays > 120).Sum(r => r.Balance);

            // Now apply SlabFilter if requested
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
            }
        }

        private async Task LoadReceivablesAgingAsync(long ucpId, DateTime targetDate)
        {
            // 1. Get Customers
            var customersQuery = _context.CustomerMasters.Where(c => c.UcpId == ucpId);
            var customers = await customersQuery.OrderBy(c => c.CustomerName).ToListAsync();
            PartyDropdown = customers.Select(c => new SelectListItem
            {
                Value = c.CustomerId.ToString(),
                Text = c.CustomerName ?? "Unknown"
            }).ToList();

            var customerDict = customers.ToDictionary(c => c.CustomerId, c => c);

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

            if (PartyId.HasValue)
            {
                string partyStr = PartyId.Value.ToString();
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

            // 3. Child Line Items (SALL) for Item Descriptions / Counts
            var childLines = await _context.Entries
                .Where(e => e.EntryType == "SALL" && salesIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(salesInvoices.Where(s => s.ItemId.HasValue).Select(s => s.ItemId!.Value))
                .Distinct().ToList();

            var itemsMap = await _context.ItemMasters
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "");

            // 4. SSD Mapping (Contracts)
            var ssdIds = salesInvoices.Where(s => s.EntryRefId.HasValue).Select(s => s.EntryRefId!.Value).Distinct().ToList();
            var ssdMap = await _context.Entries
                .Where(e => e.EntryType == "SSD" && ssdIds.Contains(e.EntryId))
                .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);

            // 5. Total Receipts & Credits per Customer up to targetDate (RV, CN - Only GL-posted)
            var custIdStrings = salesInvoices.Select(s => s.EntryAttribute1).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();

            // Get RV entries for customers
            var allRvEntries = await _context.Entries
                .Where(e => (e.EntryType == "RV" || e.EntryType == "CN") && e.UcpId == ucpId && e.EntryDate <= targetDate && custIdStrings.Contains(e.EntryAttribute2!))
                .OrderBy(e => e.EntryDate)
                .ThenBy(e => e.EntryId)
                .ToListAsync();

            var rvEntries = allRvEntries
                .Where(e => postedEntryIds.Contains(e.EntryId))
                .ToList();

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

            // 6. Build FIFO Settlement per Customer
            var processedRows = new List<AgingItemRow>();

            var groupedByCust = salesInvoices.GroupBy(s => s.EntryAttribute1 ?? "0");

            foreach (var custGroup in groupedByCust)
            {
                string custKey = custGroup.Key;
                long.TryParse(custKey, out long parsedCustId);
                string custName = customerDict.ContainsKey(parsedCustId) ? customerDict[parsedCustId].CustomerName ?? "Unknown Customer" : "Customer #" + custKey;

                var customerVouchers = rvEntries
                    .Where(r => r.EntryAttribute2 == custKey)
                    .Select(r => new ActiveVoucherItem
                    {
                        EntryId = r.EntryId,
                        VoucherNumber = r.EntryNumber.HasValue ? r.EntryNumber.Value.ToString() : r.EntryId.ToString(),
                        VoucherType = r.EntryType ?? "RV",
                        VoucherDate = r.EntryDate,
                        TotalAmount = r.Amount ?? 0,
                        RemainingAmount = r.Amount ?? 0
                    })
                    .ToList();

                foreach (var inv in custGroup.OrderBy(i => i.EntryDate).ThenBy(i => i.EntryId))
                {
                    double invAmount = inv.Amount ?? 0;
                    double needed = invAmount;
                    var appliedList = new List<VoucherSettlementInfo>();

                    foreach (var v in customerVouchers)
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

                    if (UnClearedOnly && balance <= 0.01)
                    {
                        continue; // Skip fully cleared invoices
                    }

                    // Description & Line Details
                    var lines = childLines.Where(c => c.EntryRefId == inv.EntryId).ToList();
                    string desc = "";
                    double totalQty = 0;
                    double rate = inv.Rate ?? 0;
                    double invGst = inv.TaxAmount ?? 0;

                    if (lines.Any())
                    {
                        totalQty = lines.Sum(l => l.Qty ?? 0);
                        if (invGst == 0) invGst = lines.Sum(l => l.TaxAmount ?? 0);

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

                    long contractNo = (inv.EntryRefId.HasValue && ssdMap.ContainsKey(inv.EntryRefId.Value)) ? ssdMap[inv.EntryRefId.Value] : (inv.EntryRefNo ?? 0);
                    string broker = string.IsNullOrWhiteSpace(inv.EntryAttribute4) ? "DIRECT / GENERAL SALE" : inv.EntryAttribute4.Trim();

                    var row = new AgingItemRow
                    {
                        EntryId = inv.EntryId,
                        EntryType = "SAL",
                        TransactionNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString("D7") : inv.EntryId.ToString("D7"),
                        Date = inv.EntryDate,
                        ContractNumber = contractNo > 0 ? contractNo.ToString("D7") : "-",
                        InvoiceNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString() : inv.EntryId.ToString(),
                        Qty = totalQty,
                        Description = desc,
                        Rate = rate,
                        Amount = invAmount,
                        GstAmount = invGst,
                        ReceivedPaid = paidForThis,
                        Balance = balance,
                        ContDays = contDays,
                        TotalDays = totalDays,
                        PdcAmount = 0,
                        BuyerName = custName,
                        BrokerName = broker,
                        AppliedVouchers = appliedList
                    };

                    processedRows.Add(row);
                }
            }

            // Search filter if applied
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                processedRows = processedRows.Where(r =>
                    r.InvoiceNumber.Contains(term) ||
                    r.ContractNumber.Contains(term) ||
                    r.TransactionNumber.Contains(term) ||
                    (r.BuyerName != null && r.BuyerName.ToLower().Contains(term)) ||
                    (r.BrokerName != null && r.BrokerName.ToLower().Contains(term)) ||
                    r.Description.ToLower().Contains(term)
                ).ToList();
            }

            // Multi-level Grouping (Broker -> Buyer)
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
        }

        private async Task LoadPayablesAgingAsync(long ucpId, DateTime targetDate)
        {
            var suppliers = await _context.SupplierMasters.Where(s => s.UcpId == ucpId).OrderBy(s => s.SupplierName).ToListAsync();
            PartyDropdown = suppliers.Select(s => new SelectListItem
            {
                Value = s.SupplierId.ToString(),
                Text = s.SupplierName ?? "Unknown"
            }).ToList();

            var supplierDict = suppliers.ToDictionary(s => s.SupplierId, s => s);

            var purQuery = _context.Entries
                .Where(e => e.EntryType == "PUR" && e.UcpId == ucpId && e.EntryDate <= targetDate);

            if (PartyId.HasValue)
            {
                string partyStr = PartyId.Value.ToString();
                purQuery = purQuery.Where(e => e.EntryAttribute1 == partyStr);
            }

            // Only include entries that are POSTED to General Ledger (TRANS table)
            var postedEntryIds = (await _context.Transactions
                .Where(t => t.UcpId == ucpId && t.EntryId.HasValue)
                .Select(t => t.EntryId!.Value)
                .Distinct()
                .ToListAsync())
                .ToHashSet();

            var allPurchaseInvoices = await purQuery
                .OrderBy(e => e.EntryDate)
                .ThenBy(e => e.EntryId)
                .ToListAsync();

            var purchaseInvoices = allPurchaseInvoices
                .Where(e => postedEntryIds.Contains(e.EntryId))
                .ToList();

            var purIds = purchaseInvoices.Select(p => p.EntryId).ToList();

            var childLines = await _context.Entries
                .Where(e => e.EntryType == "PURL" && purIds.Contains(e.EntryRefId ?? 0))
                .ToListAsync();

            var itemIds = childLines.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value)
                .Concat(purchaseInvoices.Where(p => p.ItemId.HasValue).Select(p => p.ItemId!.Value))
                .Distinct().ToList();

            var itemsMap = await _context.ItemMasters
                .Where(i => itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i.ItemName ?? "");

            var psdIds = purchaseInvoices.Where(p => p.EntryRefNo.HasValue).Select(p => p.EntryRefNo!.Value).Distinct().ToList();
            var psdMap = await _context.Entries
                .Where(e => e.EntryType == "PSD" && psdIds.Contains(e.EntryId))
                .ToDictionaryAsync(e => e.EntryId, e => e.EntryNumber ?? 0);

            var suppIdStrings = purchaseInvoices.Select(p => p.EntryAttribute1).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();

            // Support both PV (Supplier in EntryAttribute2) and DN (Supplier in EntryAttribute1 or EntryAttribute2)
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

            var processedRows = new List<AgingItemRow>();
            var groupedBySupp = purchaseInvoices.GroupBy(p => p.EntryAttribute1 ?? "0");

            foreach (var suppGroup in groupedBySupp)
            {
                string suppKey = suppGroup.Key;
                long.TryParse(suppKey, out long parsedSuppId);
                string suppName = supplierDict.ContainsKey(parsedSuppId) ? supplierDict[parsedSuppId].SupplierName ?? "Unknown Supplier" : "Supplier #" + suppKey;

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

                    if (UnClearedOnly && balance <= 0.01)
                    {
                        continue;
                    }

                    var lines = childLines.Where(c => c.EntryRefId == inv.EntryId).ToList();
                    string desc = "";
                    double totalQty = 0;
                    double rate = inv.Rate ?? 0;
                    double invGst = inv.TaxAmount ?? 0;

                    if (lines.Any())
                    {
                        totalQty = lines.Sum(l => l.Qty ?? 0);
                        if (invGst == 0) invGst = lines.Sum(l => l.TaxAmount ?? 0);

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

                    var row = new AgingItemRow
                    {
                        EntryId = inv.EntryId,
                        EntryType = "PUR",
                        TransactionNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString("D7") : inv.EntryId.ToString("D7"),
                        Date = inv.EntryDate,
                        ContractNumber = contractNo > 0 ? contractNo.ToString("D7") : "-",
                        InvoiceNumber = inv.EntryNumber.HasValue ? inv.EntryNumber.Value.ToString() : inv.EntryId.ToString(),
                        Qty = totalQty,
                        Description = desc,
                        Rate = rate,
                        Amount = invAmount,
                        GstAmount = invGst,
                        ReceivedPaid = paidForThis,
                        Balance = balance,
                        ContDays = contDays,
                        TotalDays = totalDays,
                        PdcAmount = 0,
                        BuyerName = suppName,
                        BrokerName = broker,
                        AppliedVouchers = appliedList
                    };

                    processedRows.Add(row);
                }
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                processedRows = processedRows.Where(r =>
                    r.InvoiceNumber.Contains(term) ||
                    r.ContractNumber.Contains(term) ||
                    r.TransactionNumber.Contains(term) ||
                    (r.BuyerName != null && r.BuyerName.ToLower().Contains(term)) ||
                    (r.BrokerName != null && r.BrokerName.ToLower().Contains(term)) ||
                    r.Description.ToLower().Contains(term)
                ).ToList();
            }

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
        }

        public async Task<IActionResult> OnGetExportExcelAsync()
        {
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            DateTime targetDate = AsOfDate?.Date ?? DateTime.Today;
            await LoadCompanyAndUserDataAsync(activeUcpId.Value);
            await LoadReportDataAsync(activeUcpId.Value);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Aging Report");

            // Header block
            ws.Cell("A1").Value = CompanyName;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:O1").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string typeStr = AgingType == "Receivables" ? "(RECEIVABLES ONLY)" : "(PAYABLES ONLY)";
            string titleStr = $"AGING REPORT {typeStr} UPTO {targetDate:dd-MM-yyyy}";
            ws.Cell("A2").Value = titleStr;
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:O2").Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell("A3").Value = UnClearedOnly ? "UnCleared Bills Only" : "All Bills";
            ws.Cell("O3").Value = $"Date: {GeneratedOn:dd-MM-yyyy hh:mm tt}";

            // Table Headers
            string[] headers = {
                "TRANSACTION NUMBER", "DATE", "CONTRACT NUMBER", "INVOICE NUMBER",
                "QTY", "DESCRIPTION", "RATE", "AMOUNT", "GST",
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
                // Broker Header
                ws.Cell(rowIdx, 1).Value = broker.BrokerName;
                ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                ws.Range(rowIdx, 1, rowIdx, 15).Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f4f8");
                rowIdx++;

                foreach (var buyer in broker.Buyers)
                {
                    // Buyer Name
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
                        ws.Cell(rowIdx, 5).SetValue(item.Qty).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 6).SetValue(item.Description);
                        ws.Cell(rowIdx, 7).SetValue(item.Rate).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 8).SetValue(item.Amount).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(rowIdx, 9).SetValue(item.GstAmount).Style.NumberFormat.Format = "#,##0.00";
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

                    // Buyer Subtotal
                    ws.Cell(rowIdx, 1).Value = "BUYER WISE SUB - TOTAL :";
                    ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                    ws.Range(rowIdx, 1, rowIdx, 7).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    
                    ws.Cell(rowIdx, 8).SetValue(buyer.SubTotalAmount).Style.Font.Bold = true;
                    ws.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(rowIdx, 9).SetValue(buyer.SubTotalGst).Style.Font.Bold = true;
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

                // Broker Subtotal
                ws.Cell(rowIdx, 1).Value = "SUB - TOTAL OF SELLER / BROKER :";
                ws.Cell(rowIdx, 1).Style.Font.Bold = true;
                ws.Range(rowIdx, 1, rowIdx, 7).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                ws.Cell(rowIdx, 8).SetValue(broker.SubTotalAmount).Style.Font.Bold = true;
                ws.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(rowIdx, 9).SetValue(broker.SubTotalGst).Style.Font.Bold = true;
                ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(rowIdx, 10).SetValue(broker.SubTotalReceived > 0 ? $"({broker.SubTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
                ws.Cell(rowIdx, 11).SetValue(broker.SubTotalBalance).Style.Font.Bold = true;
                ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";

                ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                rowIdx += 2;
            }

            // Grand Total
            ws.Cell(rowIdx, 1).Value = "GRAND - TOTAL :";
            ws.Cell(rowIdx, 1).Style.Font.Bold = true;
            ws.Range(rowIdx, 1, rowIdx, 7).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(rowIdx, 8).SetValue(GrandTotalAmount).Style.Font.Bold = true;
            ws.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(rowIdx, 9).SetValue(GrandTotalGst).Style.Font.Bold = true;
            ws.Cell(rowIdx, 9).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(rowIdx, 10).SetValue(GrandTotalReceived > 0 ? $"({GrandTotalReceived:N2})" : "0.00").Style.Font.Bold = true;
            ws.Cell(rowIdx, 11).SetValue(GrandTotalBalance).Style.Font.Bold = true;
            ws.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";
            ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Range(rowIdx, 1, rowIdx, 15).Style.Border.BottomBorder = XLBorderStyleValues.Double;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            string fileName = $"Aging_Report_{AgingType}_{targetDate:yyyyMMdd}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
