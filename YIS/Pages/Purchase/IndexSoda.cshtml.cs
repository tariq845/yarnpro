using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

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

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 10;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string SortDir { get; set; } = "desc";

        public List<string> SearchSuggestions { get; set; } = new List<string>();

        public PaginationViewModel Pagination { get; set; } = default!;

        // ViewModel for list display
        public class PurchaseSodaViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public double? GrandTotal { get; set; }
            public int ItemCount { get; set; }
            public int InvoiceCount { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // Prefetch Suppliers for display lookup
            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId)
                    .ToListAsync();
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
                var query = _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId);

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);
                    query = query.Where(e => 
                        (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                        (e.EntryNumber != null && e.EntryNumber.ToString().Contains(smart.CleanTerm)) ||
                        (_context.SupplierMasters.Any(s => s.SupplierId.ToString() == e.EntryAttribute1 && s.SupplierName.ToLower().Contains(smart.CleanTerm)))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                // Populate search suggestions (recent PSD numbers and supplier names)
                var recentNumbers = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId && e.EntryNumber != null)
                    .OrderByDescending(e => e.EntryId)
                    .Take(8)
                    .Select(e => $"PSD-{e.EntryNumber}")
                    .ToListAsync();
                SearchSuggestions = recentNumbers
                    .Concat(SupplierNames.Values.Take(6))
                    .Distinct()
                    .ToList();

                var orderedQuery = (SortDir == "asc")
                    ? query.OrderBy(e => e.EntryId)
                    : query.OrderByDescending(e => e.EntryId);

                // Get parent entries (PSD type only)
                var parentEntries = await orderedQuery
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                // Get child entries count per parent
                var childCounts = await _context.Entries
                    .Where(e => e.EntryType == "PSDL" && e.UcpId == activeUcpId)
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.ParentId ?? 0, x => x.Count);

                // Get invoice counts (PUR type referencing these PSD ids via EntryRefNo)
                var psdIds = parentEntries.Select(e => e.EntryId).ToHashSet();
                var invoiceStats = new Dictionary<long, int>();

                if (psdIds.Any())
                {
                    // Fetch all PUR entries and filter in memory
                    var allPurchaseEntries = await _context.Entries
                        .Where(e => e.EntryType == "PUR" && e.EntryRefNo != null && e.UcpId == activeUcpId)
                        .Select(e => e.EntryRefNo.Value)
                        .ToListAsync();
                    
                    // Filter and group in memory
                    invoiceStats = allPurchaseEntries
                        .Where(psdId => psdIds.Contains(psdId))
                        .GroupBy(psdId => psdId)
                        .ToDictionary(g => g.Key, g => g.Count());
                }

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
                        ItemCount = childCounts.GetValueOrDefault(e.EntryId, e.ItemId.HasValue ? 1 : 0),
                        InvoiceCount = invoiceStats.ContainsKey(e.EntryId) ? invoiceStats[e.EntryId] : 0
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
            }

            await _context.SaveChangesAsync();
            return RedirectToPage();
        }
    }
}
