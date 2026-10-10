using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Purchase
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<PurchaseViewModel> Entries { get; set; } = new List<PurchaseViewModel>();
        public Dictionary<long, string> SupplierNames { get; set; } = new Dictionary<long, string>();

        public class PurchaseViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public int ItemCount { get; set; }
            public double? GrandTotalNet { get; set; }
        }

        public async Task OnGetAsync()
        {
            if (_context.SupplierMasters != null)
            {
                SupplierNames = await _context.SupplierMasters
                    .ToDictionaryAsync(s => s.SupplierId, s => s.SupplierName ?? "Unknown");
            }

            if (_context.Entries != null)
            {
                // Get Parent Entries
                var parents = await _context.Entries
                    .Where(e => e.EntryType == "PUR")
                    .OrderByDescending(e => e.EntryDate)
                     .ThenByDescending(e => e.EntryNumber)
                    .ToListAsync();

                // Get Child Counts
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "PURL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { pId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(k => k.pId ?? 0, v => v.Count);

                Entries = parents.Select(e =>
                {
                    string supName = "Unknown";
                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long sId))
                    {
                        if (SupplierNames.ContainsKey(sId)) supName = SupplierNames[sId];
                    }

                    double.TryParse(e.EntryAttribute3, out double netAmt);

                    return new PurchaseViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        SupplierName = supName,
                        GrandTotalNet = netAmt, // Assuming Grand Total Net is stored in Attr3
                        ItemCount = childCounts.ContainsKey(e.EntryId) ? childCounts[e.EntryId] : (e.ItemId.HasValue ? 1 : 0) // Fallback for legacy
                    };
                }).ToList();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.Entries == null) return NotFound();

            // Delete Children
            var children = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "PURL")
                .ToListAsync();
            _context.Entries.RemoveRange(children);

            // Delete Parent
            var entry = await _context.Entries.FindAsync(id);
            if (entry != null && entry.EntryType == "PUR")
            {
                _context.Entries.Remove(entry);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Purchase Entry PUR-{entry.EntryNumber} deleted successfully.";
            }
            return RedirectToPage();
        }
    }
}
