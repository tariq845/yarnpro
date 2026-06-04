using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class SsdTrackIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SsdTrackIndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<SsdTrackViewModel> Entries { get; set; } = new List<SsdTrackViewModel>();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public class SsdTrackViewModel
        {
            public long EntryId { get; set; }
            public long? EntryNumber { get; set; }
            public DateTime? EntryDate { get; set; }
            public string? CustomerName { get; set; }
            public double? GrandTotal { get; set; }
            public int ItemCount { get; set; }
            public double Progress { get; set; } // Invoicing progress (SAL / SSD)
        }

        public async Task OnGetAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (_context.Entries == null) return;

            var query = _context.Entries
                .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId)
                .Join(_context.CustomerMasters.Where(c => c.UcpId == activeUcpId),
                      e => e.EntryAttribute1,
                      c => c.CustomerId.ToString(),
                      (e, c) => new { e, c.CustomerName });

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                query = query.Where(x => x.e.EntryNumber.ToString().Contains(SearchTerm) ||
                                       x.CustomerName.Contains(SearchTerm));
            }

            var rawEntries = await query
                .OrderByDescending(x => x.e.EntryDate)
                .ThenByDescending(x => x.e.EntryNumber)
                .ToListAsync();

            // Get item counts for these SSDs specifically
            var itemCountsResult = await (from e in _context.Entries
                                          join q in query on e.EntryRefId equals q.e.EntryId
                                          where e.EntryType == "SSDL"
                                          group e by e.EntryRefId into g
                                          select new { EntryId = g.Key ?? 0, Count = g.Count() })
                                         .ToListAsync();
            var itemCounts = itemCountsResult.ToDictionary(x => x.EntryId, x => x.Count);

            // Calculate invoicing progress per SSD (SAL items linked to these SSDs)
            var invoicingResult = await (from sal in _context.Entries
                                         join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                         join q in query on sal.EntryRefId equals q.e.EntryId
                                         where sal.EntryType == "SAL" && sall.EntryType == "SALL"
                                         group sall by sal.EntryRefId into g
                                         select new
                                         {
                                             SsdId = g.Key ?? 0,
                                             TotalInvoiced = g.Sum(s => s.Qty ?? 0)
                                         }).ToListAsync();

            var invoicingMap = invoicingResult.ToDictionary(x => x.SsdId, x => x.TotalInvoiced);

            Entries = rawEntries.Select(x =>
            {
                double originalQty = x.e.Qty ?? 0;
                double invoicedQty = invoicingMap.TryGetValue(x.e.EntryId, out double iValue) ? iValue : 0;
                double progress = originalQty > 0 ? (invoicedQty / originalQty) * 100 : 0;
                if (progress > 100) progress = 100;

                return new SsdTrackViewModel
                {
                    EntryId = x.e.EntryId,
                    EntryNumber = x.e.EntryNumber,
                    EntryDate = x.e.EntryDate,
                    CustomerName = x.CustomerName,
                    GrandTotal = x.e.Amount,
                    ItemCount = itemCounts.TryGetValue(x.e.EntryId, out int count) ? count : 0,
                    Progress = progress
                };
            }).ToList();
        }
    }
}
