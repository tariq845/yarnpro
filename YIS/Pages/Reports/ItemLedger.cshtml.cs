using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class ItemLedgerModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ItemLedgerModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public long? SelectedItemId { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        public SelectList ItemList { get; set; } = default!;
        public ItemMaster? SelectedItem { get; set; }
        public List<LedgerViewModel> LedgerList { get; set; } = new List<LedgerViewModel>();

        public double OpeningBalance { get; set; } // Opening Quantity
        public double LastPurchaseRate { get; set; } // Standard Cost Rate
        public double OpeningValue { get; set; } // Opening Quantity * LastPurchaseRate
        
        public double TotalPeriodPurchase { get; set; }
        public double TotalPeriodDebitNote { get; set; }
        public double TotalPeriodSale { get; set; }
        public double TotalPeriodCreditNote { get; set; }

        public double FinalBalance { get; set; } // Closing Quantity
        public double FinalValue { get; set; } // Closing Quantity * LastPurchaseRate

        public class LedgerViewModel
        {
            public DateTime Date { get; set; }
            public string RefNo { get; set; } = "";
            public string RefType { get; set; } = ""; 
            public string PartyName { get; set; } = "";
            public double PurchaseQty { get; set; }
            public double DebitNoteQty { get; set; }
            public double SaleQty { get; set; }
            public double CreditNoteQty { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double Balance { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // 1. Load Item Dropdown
            var items = await _context.ItemMasters
                .Where(i => activeUcpId == null || i.UcpId == activeUcpId)
                .Select(i => new { i.ItemId, i.ItemName, i.ItemHsCode })
                .OrderBy(i => i.ItemName)
                .AsNoTracking()
                .ToListAsync();

            ItemList = new SelectList(items, "ItemId", "ItemName");

            if (SelectedItemId.HasValue && SelectedItemId.Value > 0)
            {
                SelectedItem = await _context.ItemMasters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.ItemId == SelectedItemId.Value && (activeUcpId == null || i.UcpId == activeUcpId));

                // 2. Load Party Lookups (Supplier/Customer)
                var customers = await _context.CustomerMasters
                    .Where(c => activeUcpId == null || c.UcpId == activeUcpId)
                    .Select(c => new { c.CustomerId, c.CustomerName })
                    .AsNoTracking()
                    .ToDictionaryAsync(k => k.CustomerId.ToString(), v => v.CustomerName);

                var suppliers = await _context.SupplierMasters
                    .Where(s => activeUcpId == null || s.UcpId == activeUcpId)
                    .Select(s => new { s.SupplierId, s.SupplierName })
                    .AsNoTracking()
                    .ToDictionaryAsync(k => k.SupplierId.ToString(), v => v.SupplierName);

                // 3. Determine Last Purchase Rate (Standard Costing Rate)
                // Prioritize the last purchase before FromDate if FromDate is specified; else the overall latest purchase rate
                var purEntries = await _context.Entries
                    .AsNoTracking()
                    .Where(e => e.ItemId == SelectedItemId && 
                                e.EntryType == "PURL" && 
                                (activeUcpId == null || e.UcpId == activeUcpId) && 
                                e.Rate.HasValue && e.Rate.Value > 0)
                    .OrderByDescending(e => e.EntryDate)
                    .ThenByDescending(e => e.EntryId)
                    .Select(e => new { e.EntryDate, e.Rate })
                    .ToListAsync();

                double lastRate = 0;
                if (FromDate.HasValue)
                {
                    var rateBeforeFrom = purEntries.FirstOrDefault(e => e.EntryDate < FromDate.Value);
                    if (rateBeforeFrom != null)
                    {
                        lastRate = rateBeforeFrom.Rate ?? 0;
                    }
                }

                if (lastRate == 0 && purEntries.Any())
                {
                    lastRate = purEntries.First().Rate ?? 0;
                }

                LastPurchaseRate = lastRate;

                // 4. Calculate Opening Balance (Quantity)
                OpeningBalance = 0;
                double runningBalance = 0;
                var preDate = FromDate ?? DateTime.MinValue;
                if (preDate > DateTime.MinValue)
                {
                    var preEntries = await _context.Entries
                        .AsNoTracking()
                        .Where(e => e.ItemId == SelectedItemId && 
                                   (activeUcpId == null || e.UcpId == activeUcpId) &&
                                   e.EntryDate < preDate &&
                                   (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL"))
                        .ToListAsync();

                    double prePurchase = preEntries.Where(e => e.EntryType == "PURL").Sum(e => e.Qty ?? 0);
                    double preDebit = preEntries.Where(e => e.EntryType == "DNL").Sum(e => e.Qty ?? 0);
                    double preSale = preEntries.Where(e => e.EntryType == "SALL").Sum(e => e.Qty ?? 0);
                    double preCredit = preEntries.Where(e => e.EntryType == "CNL").Sum(e => e.Qty ?? 0);

                    runningBalance = (prePurchase + preCredit) - (preSale + preDebit);
                    OpeningBalance = runningBalance;
                }

                // Standard Costing: Opening Stock Value = Opening Qty * Last Purchase Rate
                OpeningValue = OpeningBalance * LastPurchaseRate;

                // 5. Fetch Transactions within date range
                var query = from e in _context.Entries
                            join p in _context.Entries on e.EntryRefId equals p.EntryId into pj
                            from p in pj.DefaultIfEmpty()
                            where e.ItemId == SelectedItemId && 
                                  (activeUcpId == null || e.UcpId == activeUcpId) &&
                                  (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL")
                            select new { Child = e, Parent = p };

                if (FromDate.HasValue)
                    query = query.Where(x => x.Child.EntryDate >= FromDate.Value);
                
                if (ToDate.HasValue)
                    query = query.Where(x => x.Child.EntryDate <= ToDate.Value);

                var rawEntries = await query
                    .OrderBy(x => x.Child.EntryDate)
                    .ThenBy(x => x.Child.EntryId) 
                    .Select(x => new 
                    {
                        x.Child.EntryDate,
                        x.Child.EntryType,
                        x.Child.Qty,
                        x.Child.Rate,
                        x.Child.Amount,
                        PartyId = x.Parent != null ? x.Parent.EntryAttribute1 : x.Child.EntryAttribute1,
                        RefNumber = x.Parent != null ? x.Parent.EntryNumber : x.Child.EntryRefNo,
                        EntryId = x.Child.EntryId
                    })
                    .ToListAsync();

                foreach (var entry in rawEntries)
                {
                    double qty = entry.Qty ?? 0;
                    double pur = 0, deb = 0, sal = 0, cred = 0;
                    string partyName = "-";
                    string typeDisplay = "";
                    string prefix = "";

                    switch (entry.EntryType)
                    {
                        case "PURL":
                            prefix = "PUR-";
                            typeDisplay = "Purchase";
                            pur = qty;
                            break;
                        case "SALL":
                            prefix = "SAL-";
                            typeDisplay = "Sale";
                            sal = qty;
                            break;
                        case "CNL":
                            prefix = "SR-";
                            typeDisplay = "Sale Return";
                            cred = qty;
                            break;
                        case "DNL":
                            prefix = "PR-";
                            typeDisplay = "Purchase Return";
                            deb = qty;
                            break;
                    }

                    string refNo = entry.RefNumber != null ? prefix + entry.RefNumber : "-";

                    if (entry.PartyId != null)
                    {
                        if ((entry.EntryType == "PURL" || entry.EntryType == "DNL") && suppliers.ContainsKey(entry.PartyId))
                            partyName = suppliers[entry.PartyId];
                        else if ((entry.EntryType == "SALL" || entry.EntryType == "CNL") && customers.ContainsKey(entry.PartyId))
                            partyName = customers[entry.PartyId];
                    }

                    runningBalance = runningBalance + pur + cred - sal - deb;

                    double? rate = entry.Rate;
                    double? amount = entry.Amount;
                    if ((amount == null || amount == 0) && (entry.Qty.HasValue && entry.Qty.Value > 0) && (rate.HasValue && rate.Value > 0))
                    {
                        amount = entry.Qty.Value * rate.Value;
                    }

                    LedgerList.Add(new LedgerViewModel
                    {
                        Date = entry.EntryDate ?? DateTime.MinValue,
                        RefNo = refNo,
                        RefType = typeDisplay,
                        PartyName = partyName,
                        PurchaseQty = pur,
                        DebitNoteQty = deb,
                        SaleQty = sal,
                        CreditNoteQty = cred,
                        Rate = rate,
                        Amount = amount,
                        Balance = runningBalance
                    });
                }
                
                TotalPeriodPurchase = LedgerList.Sum(x => x.PurchaseQty);
                TotalPeriodDebitNote = LedgerList.Sum(x => x.DebitNoteQty);
                TotalPeriodSale = LedgerList.Sum(x => x.SaleQty);
                TotalPeriodCreditNote = LedgerList.Sum(x => x.CreditNoteQty);

                FinalBalance = runningBalance;
                FinalValue = FinalBalance * LastPurchaseRate;
            }
        }
    }
}
