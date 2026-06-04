using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class PsdTrackIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PsdTrackIndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<PsdTrackViewModel> Entries { get; set; } = new List<PsdTrackViewModel>();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public class PsdTrackViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? SupplierName { get; set; }
            public double? GrandTotal { get; set; }
            public int ItemCount { get; set; }
            public double Progress { get; set; } // Realization progress (Purchased / Original)
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (_context.Entries == null) return;

            var query = _context.Entries
                .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId)
                .Join(_context.SupplierMasters.Where(s => s.UcpId == activeUcpId),
                      e => e.EntryAttribute1,
                      s => s.SupplierId.ToString(),
                      (e, s) => new { e, s.SupplierName });

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                query = query.Where(x => x.e.EntryNumber.ToString().Contains(SearchTerm) ||
                                       x.SupplierName.Contains(SearchTerm));
            }

            var rawEntries = await query
                .OrderByDescending(x => x.e.EntryDate)
                .ThenByDescending(x => x.e.EntryNumber)
                .ToListAsync();

            // Get item counts for these PSDs specifically
            var itemCountsResult = await (from e in _context.Entries
                                          join q in query on e.EntryRefId equals q.e.EntryId
                                          where e.EntryType == "PSDL" && e.UcpId == activeUcpId
                                          group e by e.EntryRefId into g
                                          select new { EntryId = g.Key ?? 0, Count = g.Count() })
                                         .ToListAsync();
            var itemCounts = itemCountsResult.ToDictionary(x => x.EntryId, x => x.Count);

            // Calculate realization progress per PSD (PUR items linked to these PSDs)
            var realizationResult = await (from pur in _context.Entries
                                           join purl in _context.Entries on pur.EntryId equals purl.EntryRefId
                                           join q in query on pur.EntryRefNo equals q.e.EntryId
                                           where pur.EntryType == "PUR" && purl.EntryType == "PURL" && pur.UcpId == activeUcpId && purl.UcpId == activeUcpId
                                           group purl by pur.EntryRefNo into g
                                           select new
                                           {
                                               PsdId = g.Key,
                                               TotalPurchased = g.Sum(p => p.Qty ?? 0)
                                           }).ToListAsync();

            var realizationMap = realizationResult
                .Where(x => x.PsdId.HasValue)
                .ToDictionary(x => x.PsdId!.Value, x => x.TotalPurchased);

            Entries = rawEntries.Select(x =>
            {
                double originalQty = x.e.Qty ?? 0;
                double purchasedQty = realizationMap.TryGetValue(x.e.EntryId, out double p) ? p : 0;
                double progress = originalQty > 0 ? (purchasedQty / originalQty) * 100 : 0;
                if (progress > 100) progress = 100;

                return new PsdTrackViewModel
                {
                    EntryId = x.e.EntryId,
                    EntryNumber = x.e.EntryNumber,
                    EntryDate = x.e.EntryDate,
                    SupplierName = x.SupplierName,
                    GrandTotal = x.e.Amount,
                    ItemCount = itemCounts.TryGetValue(x.e.EntryId, out int count) ? count : 0,
                    Progress = progress
                };
            }).ToList();
        }
    }
}
