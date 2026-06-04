using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Purchase
{
    public class IndexSodaModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexSodaModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<PurchaseSodaViewModel> Entries { get; set; } = new List<PurchaseSodaViewModel>();
        public Dictionary<long, string> SupplierNames { get; set; } = new Dictionary<long, string>();

        // ViewModel for list display
        public class PurchaseSodaViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public double? GrandTotal { get; set; }
            public int ItemCount { get; set; }
        }

        public async Task OnGetAsync()
        {
            // Prefetch Suppliers for display lookup
            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters.ToListAsync();
                foreach (var s in suppliers)
                {
                    if (!SupplierNames.ContainsKey(s.SupplierId))
                    {
                        SupplierNames.Add(s.SupplierId, s.SupplierName ?? "Unknown");
                    }
                }
            }

            if (_context.Entries != null)
            {
                // Get parent entries (PSD type only)
                var parentEntries = await _context.Entries
                    .Where(e => e.EntryType == "PSD")
                    .OrderByDescending(e => e.EntryDate)
                    .ThenByDescending(e => e.EntryId)
                    .ToListAsync();

                // Get child entries count per parent
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "PSDL")
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);

                // Build view models
                Entries = parentEntries.Select(e =>
                {
                    string supplierName = "Unknown";
                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long supId))
                    {
                        if (SupplierNames.ContainsKey(supId))
                        {
                            supplierName = SupplierNames[supId];
                        }
                    }

                    return new PurchaseSodaViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        SupplierName = supplierName,
                        GrandTotal = e.Amount, // Grand total stored in Amount
                        ItemCount = childCounts.GetValueOrDefault(e.EntryId, e.ItemId.HasValue ? 1 : 0)
                    };
                }).ToList();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.Entries == null)
            {
                return NotFound();
            }

            // Delete child entries first
            var children = await _context.Entries
                .Where(e => e.EntryRefId == id && e.EntryType == "PSDL")
                .ToListAsync();
            _context.Entries.RemoveRange(children);

            // Delete parent entry
            var entry = await _context.Entries.FindAsync(id);
            if (entry != null)
            {
                _context.Entries.Remove(entry);
                TempData["StatusMessage"] = $"Purchase Soda Entry PSD-{entry.EntryNumber} deleted successfully.";
            }

            await _context.SaveChangesAsync();
            return RedirectToPage();
        }
    }
}
