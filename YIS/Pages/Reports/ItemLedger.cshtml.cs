using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

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
        public List<LedgerViewModel> LedgerList { get; set; } = new List<LedgerViewModel>();
        public double OpeningBalance { get; set; }
        public double FinalBalance { get; set; }

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

            public double Balance { get; set; }
        }

        public async Task OnGetAsync()
        {
            // 1. Load Item Dropdown
            var items = await _context.ItemMasters
                .Select(i => new { i.ItemId, i.ItemName })
                .OrderBy(i => i.ItemName)
                .AsNoTracking()
                .ToListAsync();

            ItemList = new SelectList(items, "ItemId", "ItemName");

            if (SelectedItemId.HasValue && SelectedItemId.Value > 0)
            {
                // 2. Load Party Lookups (Supplier/Customer)
                var customers = await _context.CustomerMasters
                    .Select(c => new { c.CustomerId, c.CustomerName })
                    .AsNoTracking()
                    .ToDictionaryAsync(k => k.CustomerId.ToString(), v => v.CustomerName);

                var suppliers = await _context.SupplierMasters
                    .Select(s => new { s.SupplierId, s.SupplierName })
                    .AsNoTracking()
                    .ToDictionaryAsync(k => k.SupplierId.ToString(), v => v.SupplierName);

                // 3. Fetch Transactions
                var query = from e in _context.Entries
                            join p in _context.Entries on e.EntryRefId equals p.EntryId
                            where e.ItemId == SelectedItemId && 
                                  (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL")
                            select new { Child = e, Parent = p };

                if (FromDate.HasValue)
                    query = query.Where(x => x.Child.EntryDate >= FromDate.Value);
                
                if (ToDate.HasValue)
                    query = query.Where(x => x.Child.EntryDate <= ToDate.Value);

                var rawEntries = await query
                    .OrderBy(x => x.Child.EntryId) 
                    .Select(x => new 
                    {
                        x.Child.EntryDate,
                        x.Child.EntryType,
                        x.Child.Qty,
                        PartyId = x.Parent.EntryAttribute1,
                        RefNumber = x.Parent.EntryNumber,
                        EntryId = x.Child.EntryId
                    })
                    .ToListAsync();
                
                // 4. Calculate Opening Balance
                OpeningBalance = 0;
                double runningBalance = 0;
                var preDate = FromDate ?? DateTime.MinValue;
                if (preDate > DateTime.MinValue)
                {
                    var preEntries = await _context.Entries
                        .AsNoTracking()
                        .Where(e => e.ItemId == SelectedItemId && e.EntryDate < preDate &&
                                   (e.EntryType == "PURL" || e.EntryType == "SALL" || e.EntryType == "DNL" || e.EntryType == "CNL"))
                        .ToListAsync();

                    double prePurchase = preEntries.Where(e => e.EntryType == "PURL").Sum(e => e.Qty ?? 0);
                    double preDebit = preEntries.Where(e => e.EntryType == "DNL").Sum(e => e.Qty ?? 0);
                    double preSale = preEntries.Where(e => e.EntryType == "SALL").Sum(e => e.Qty ?? 0);
                    double preCredit = preEntries.Where(e => e.EntryType == "CNL").Sum(e => e.Qty ?? 0);

                    runningBalance = (prePurchase + preCredit) - (preSale + preDebit);
                    OpeningBalance = runningBalance;
                }

                foreach (var entry in rawEntries.OrderBy(x => x.EntryId))
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
                        Balance = runningBalance
                    });
                }
                
                FinalBalance = runningBalance;
            }
        }
    }
}
