using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class PsdRecordTrackModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PsdRecordTrackModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public Entry? PsdHeader { get; set; }
        public string SupplierName { get; set; } = "Unknown";
        public List<LineItemViewModel> LineItems { get; set; } = new();
        public List<PurchaseViewModel> RelatedPurchases { get; set; } = new();
        public List<SaleViewModel> RelatedSales { get; set; } = new();
        public double GrandTotal { get; set; }

        // ViewModel for line items
        public class LineItemViewModel
        {
            public long EntryId { get; set; }
            public long? ItemId { get; set; }
            public string? ItemName { get; set; }
            public double? Qty { get; set; } // Original PSD Qty
            public double? PurchasedQty { get; set; } // Realized from PUR
            public double? SoldQty { get; set; } // Consumed by SSD
            public double? RemainingQty { get; set; } // Purchased - Sold (or Stock)
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
        }

        // ViewModel for related purchases
        public class PurchaseViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public string? Description { get; set; }
            public double? Amount { get; set; }
            public double? GrandTotal { get; set; }
        }

        // ViewModel for related sales
        public class SaleViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? CustomerName { get; set; }
            public double? Amount { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (_context.Entries == null)
            {
                return NotFound();
            }

            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            // Load PSD header
            PsdHeader = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == id && e.EntryType == "PSD" && e.UcpId == activeUcpId);

            if (PsdHeader == null)
            {
                return NotFound();
            }

            // Get supplier name
            if (!string.IsNullOrEmpty(PsdHeader.EntryAttribute1) && 
                long.TryParse(PsdHeader.EntryAttribute1, out long supplierId))
            {
                var supplier = await _context.SupplierMasters
                    .FirstOrDefaultAsync(s => s.SupplierId == supplierId && s.UcpId == activeUcpId);
                SupplierName = supplier?.SupplierName ?? "Unknown";
            }

            // Load line items joined with item names
            var lineItemsWithNames = await (from e in _context.Entries
                                           join i in _context.ItemMasters on e.ItemId equals i.ItemId
                                           where e.EntryRefId == id && e.EntryType == "PSDL" && e.UcpId == activeUcpId && i.UcpId == activeUcpId
                                           orderby e.EntryId
                                           select new { 
                                               e.EntryId, 
                                               e.ItemId, 
                                               i.ItemName, 
                                               e.Qty, 
                                               e.Lbs, 
                                               e.Rate, 
                                               e.Amount 
                                           }).ToListAsync();

            // Load related purchases (PURL) to calculate realization
            // Link via PUR.EntryRefNo == PSD.EntryNumber OR PUR.EntryAttribute2 == PSD.EntryId
            var purchasedData = await (from pur in _context.Entries
                                      join purl in _context.Entries on pur.EntryId equals purl.EntryRefId
                                      where pur.EntryType == "PUR" && purl.EntryType == "PURL" &&
                                            pur.UcpId == activeUcpId && purl.UcpId == activeUcpId &&
                                            (pur.EntryRefNo == id || pur.EntryAttribute2 == id.ToString() || pur.EntryRefNo == PsdHeader.EntryNumber)
                                      group purl by purl.ItemId into g
                                      select new { 
                                          ItemId = g.Key, 
                                          TotalPurchased = g.Sum(p => p.Qty ?? 0) 
                                      }).ToListAsync();

            var purchasedMap = purchasedData.ToDictionary(x => x.ItemId ?? 0, x => x.TotalPurchased);

            // Load related sales (SSD) and SSDL line items to calculate consumption
            var soldData = await (from ssd in _context.Entries
                                 join ssdl in _context.Entries on ssd.EntryId equals ssdl.EntryRefId
                                 where ssd.EntryRefId == id && ssd.EntryType == "SSD" && ssdl.EntryType == "SSDL" &&
                                       ssd.UcpId == activeUcpId && ssdl.UcpId == activeUcpId
                                 group ssdl by ssdl.ItemId into g
                                 select new { 
                                     ItemId = g.Key, 
                                     TotalSold = g.Sum(s => s.Qty ?? 0) 
                                 }).ToListAsync();

            var soldMap = soldData.ToDictionary(x => x.ItemId ?? 0, x => x.TotalSold);

            LineItems = lineItemsWithNames.Select(e => {
                double original = e.Qty ?? 0;
                double purchased = purchasedMap.TryGetValue(e.ItemId ?? 0, out double p) ? p : 0;
                double sold = soldMap.TryGetValue(e.ItemId ?? 0, out double s) ? s : 0;
                
                // Stock Balance is what was Purchased minus what was Sold
                double stockBalance = purchased - sold;
                if (stockBalance < 0) stockBalance = 0;

                return new LineItemViewModel
                {
                    EntryId = e.EntryId,
                    ItemId = e.ItemId,
                    ItemName = e.ItemName,
                    Qty = original,
                    PurchasedQty = purchased,
                    SoldQty = sold,
                    RemainingQty = stockBalance, 
                    Lbs = e.Lbs,
                    Rate = e.Rate,
                    Amount = e.Amount
                };
            }).ToList();

            // Calculate grand total of PSD
            GrandTotal = LineItems.Sum(l => l.Amount ?? 0);

            // Load related purchases for display joined with supplier names
            RelatedPurchases = await (from pur in _context.Entries
                                    join s in _context.SupplierMasters on pur.EntryAttribute1 equals s.SupplierId.ToString() into suppliers
                                    from s in suppliers.DefaultIfEmpty()
                                    where pur.EntryType == "PUR" && pur.UcpId == activeUcpId && 
                                          (pur.EntryRefNo == id || pur.EntryAttribute2 == id.ToString() || pur.EntryRefNo == PsdHeader.EntryNumber)
                                    orderby pur.EntryDate descending
                                    select new PurchaseViewModel
                                    {
                                        EntryId = pur.EntryId,
                                        EntryNumber = pur.EntryNumber,
                                        EntryDate = pur.EntryDate,
                                        SupplierName = s != null ? s.SupplierName : "Unknown",
                                        Description = pur.EntryRemarks ?? "Purchase Entry",
                                        Amount = pur.Amount,
                                        GrandTotal = pur.Amount
                                    }).ToListAsync();

            // Load related sales (SSD) joined with customer names
            RelatedSales = await (from ssd in _context.Entries
                                 join c in _context.CustomerMasters on ssd.EntryAttribute1 equals c.CustomerId.ToString() into customers
                                 from c in customers.DefaultIfEmpty()
                                 where ssd.EntryType == "SSD" && ssd.EntryRefId == id && ssd.UcpId == activeUcpId && (c == null || c.UcpId == activeUcpId)
                                 orderby ssd.EntryDate descending
                                 select new SaleViewModel {
                                     EntryId = ssd.EntryId,
                                     EntryNumber = ssd.EntryNumber,
                                     EntryDate = ssd.EntryDate,
                                     CustomerName = c != null ? c.CustomerName : "Unknown",
                                     Amount = ssd.Amount
                                 }).ToListAsync();

            return Page();
        }
    }
}
