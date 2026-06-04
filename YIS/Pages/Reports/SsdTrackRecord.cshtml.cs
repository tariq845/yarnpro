using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Reports
{
    public class SsdTrackRecordModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SsdTrackRecordModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public Entry? SsdHeader { get; set; }
        public string CustomerName { get; set; } = "Unknown";
        public string ParentPsdNumber { get; set; } = "N/A";
        public long? ParentPsdId { get; set; }
        public List<LineItemViewModel> LineItems { get; set; } = new List<LineItemViewModel>();
        public List<SaleViewModel> RelatedSales { get; set; } = new List<SaleViewModel>();
        public double GrandTotal { get; set; }

        public class LineItemViewModel
        {
            public long EntryId { get; set; }
            public long? ItemId { get; set; }
            public string ItemName { get; set; } = "Unknown";
            public double? Qty { get; set; }
            public double? SoldQty { get; set; }
            public double? RemainingQty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
        }

        public class SaleViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string Description { get; set; } = "";
            public double? Amount { get; set; }
            public double? GrandTotal { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (_context.Entries == null)
            {
                return NotFound();
            }

            // Load SSD header
            SsdHeader = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == id && e.EntryType == "SSD");

            if (SsdHeader == null)
            {
                return NotFound();
            }

            // Get customer name
            if (!string.IsNullOrEmpty(SsdHeader.EntryAttribute1) && 
                long.TryParse(SsdHeader.EntryAttribute1, out long customerId))
            {
                var customer = await _context.CustomerMasters
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId);
                CustomerName = customer?.CustomerName ?? "Unknown";
            }

            // Get parent PSD number
            if (SsdHeader.EntryRefId.HasValue)
            {
                var psd = await _context.Entries.FindAsync(SsdHeader.EntryRefId.Value);
                if (psd != null)
                {
                    ParentPsdNumber = psd.EntryNumber?.ToString() ?? "N/A";
                    ParentPsdId = psd.EntryId;
                }
            }

            // Load line items (SSDL) joined with item names
            var lineItemsWithNames = await (from e in _context.Entries
                                           join i in _context.ItemMasters on e.ItemId equals i.ItemId
                                           where e.EntryRefId == id && e.EntryType == "SSDL"
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

            // Load related invoices (SAL entries referencing this SSD)
            // Note: SAL.EntryRefId = SSD.EntryId
            var invoiceData = await (from sal in _context.Entries
                                    join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                    where sal.EntryRefId == id && sal.EntryType == "SAL" && sall.EntryType == "SALL"
                                    group sall by sall.ItemId into g
                                    select new { 
                                        ItemId = g.Key, 
                                        TotalSold = g.Sum(s => s.Qty ?? 0) 
                                    }).ToListAsync();

            var soldMap = invoiceData.ToDictionary(x => x.ItemId ?? 0, x => x.TotalSold);

            LineItems = lineItemsWithNames.Select(e => {
                double original = e.Qty ?? 0;
                double sold = soldMap.TryGetValue(e.ItemId ?? 0, out double s) ? s : 0;
                double remaining = original - sold;
                if (remaining < 0) remaining = 0;

                return new LineItemViewModel
                {
                    EntryId = e.EntryId,
                    ItemId = e.ItemId,
                    ItemName = e.ItemName,
                    Qty = original,
                    SoldQty = sold,
                    RemainingQty = remaining,
                    Lbs = e.Lbs,
                    Rate = e.Rate,
                    Amount = e.Amount
                };
            }).ToList();

            // Calculate grand total of SSD
            GrandTotal = LineItems.Sum(l => l.Amount ?? 0);

            // Fetch related sales for display
            RelatedSales = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "SAL")
                .OrderByDescending(e => e.EntryDate)
                .Select(e => new SaleViewModel
                {
                    EntryId = e.EntryId,
                    EntryNumber = e.EntryNumber,
                    EntryDate = e.EntryDate,
                    Description = e.EntryRemarks ?? "Sale Invoice",
                    Amount = e.Amount,
                    GrandTotal = e.Amount
                }).ToListAsync();

            return Page();
        }
    }
}
