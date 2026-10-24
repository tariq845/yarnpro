using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Sales
{
    public class IndexSodaModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexSodaModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<SaleSodaViewModel> Entries { get; set; } = new List<SaleSodaViewModel>();
        public Dictionary<long, string> CustomerNames { get; set; } = new Dictionary<long, string>();

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

        public class SaleSodaViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? CustomerName { get; set; }
            public double? TotalQty { get; set; }
            public double? GrandTotal { get; set; }
            public string? RefPurchaseNo { get; set; }
            public string? Remarks { get; set; }
            public int ItemCount { get; set; }
            public int InvoiceCount { get; set; }
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);

            // Prefetch Customers
            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .ToListAsync();
                foreach (var c in customers)
                {
                    if (!CustomerNames.ContainsKey(c.CustomerId))
                    {
                        CustomerNames.Add(c.CustomerId, c.CustomerName ?? "Unknown");
                    }
                }
            }

            if (_context.Entries != null)
            {
                var query = _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId);

                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    var smart = SmartSearchHelper.Parse(SearchTerm);
                    query = query.Where(e =>
                        (smart.HasNumber && (e.EntryNumber == smart.ExtractedNumber.Value || e.EntryRefNo == smart.ExtractedNumber.Value)) ||
                        (e.EntryNumber != null && e.EntryNumber.ToString().Contains(smart.CleanTerm)) ||
                        (e.EntryRefNo != null && e.EntryRefNo.ToString().Contains(smart.CleanTerm)) ||
                        (_context.CustomerMasters.Any(c => c.CustomerId.ToString() == e.EntryAttribute1 && c.CustomerName.ToLower().Contains(smart.CleanTerm)))
                    );
                }

                if (PageSize <= 0) PageSize = 10;
                int totalItems = await query.CountAsync();
                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);

                // Populate search suggestions (recent SSD numbers and customer names)
                var recentNumbers = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId && e.EntryNumber != null)
                    .OrderByDescending(e => e.EntryId)
                    .Take(8)
                    .Select(e => $"SSD-{e.EntryNumber}")
                    .ToListAsync();
                SearchSuggestions = recentNumbers
                    .Concat(CustomerNames.Values.Take(6))
                    .Distinct()
                    .ToList();

                var orderedQuery = (SortDir == "asc")
                    ? query.OrderBy(e => e.EntryId)
                    : query.OrderByDescending(e => e.EntryId);

                // Get parent entries (SSD type only)
                var parentEntries = await orderedQuery
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                // Get child entries raw
                var childRaw = await _context.Entries
                    .Where(e => e.EntryType == "SSDL" && e.EntryRefId != null && e.UcpId == activeUcpId)
                    .Select(e => new { RefId = e.EntryRefId, Qty = e.Qty ?? 0 })
                    .ToListAsync();
                
                var childStats = childRaw
                    .GroupBy(e => e.RefId)
                    .Select(g => new { ParentId = g.Key, ItemCount = g.Count(), QtySum = g.Sum(x => x.Qty) })
                    .ToDictionary(x => x.ParentId ?? 0, x => x);

                // Get invoice counts (SAL type referencing these SSD ids)
                var ssdIds = parentEntries.Select(e => e.EntryId).ToHashSet();
                var invoiceStats = new Dictionary<long, int>();

                if (ssdIds.Any())
                {
                    // Fetch all SAL entries and filter in memory to avoid SQL translation issues
                    var allSalesEntries = await _context.Entries
                        .Where(e => e.EntryType == "SAL" && e.EntryRefId != null && e.UcpId == activeUcpId)
                        .Select(e => e.EntryRefId.Value)
                        .ToListAsync();
                    
                    // Filter and group in memory
                    invoiceStats = allSalesEntries
                        .Where(ssdId => ssdIds.Contains(ssdId))
                        .GroupBy(ssdId => ssdId)
                        .ToDictionary(g => g.Key, g => g.Count());
                }

                Entries = parentEntries.Select(e =>
                {
                    string customerName = "Unknown";
                    if (!string.IsNullOrEmpty(e.EntryAttribute1) && long.TryParse(e.EntryAttribute1, out long custId))
                    {
                        if (CustomerNames.ContainsKey(custId))
                        {
                            customerName = CustomerNames[custId];
                        }
                    }

                    string refNo = !string.IsNullOrEmpty(e.EntryRefNo?.ToString()) ? "PSD-" + e.EntryRefNo : "-";

                    int count = 0;
                    double qty = e.Qty ?? 0;

                    if (childStats.ContainsKey(e.EntryId))
                    {
                        var stats = childStats[e.EntryId];
                        count = stats.ItemCount;
                        // Fallback: If header qty is 0 but we have children, use child sum
                        if (qty == 0) qty = stats.QtySum;
                    }
                    else if (e.ItemId.HasValue)
                    {
                        // Legacy single item
                        count = 1;
                    }

                    return new SaleSodaViewModel
                    {
                        EntryId = e.EntryId,
                        EntryNumber = e.EntryNumber,
                        EntryDate = e.EntryDate,
                        CustomerName = customerName,
                        TotalQty = qty,
                        GrandTotal = e.Amount,
                        RefPurchaseNo = refNo,
                        Remarks = e.EntryRemarks,
                        ItemCount = count,
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
                .Where(e => e.EntryRefId == id && e.EntryType == "SSDL")
                .ToListAsync();
            _context.Entries.RemoveRange(children);

            // Delete parent entry
            var entry = await _context.Entries.FindAsync(id);

            if (entry != null)
            {
                _context.Entries.Remove(entry);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage();
        }
    }
}
