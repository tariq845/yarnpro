using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Sales
{
    public class IndexSodaModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexSodaModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<Entry> Entries { get; set; } = default!;
        public Dictionary<long, string> CustomerNames { get; set; } = new Dictionary<long, string>();
        public Dictionary<long, string> ItemNames { get; set; } = new Dictionary<long, string>();

        public async Task OnGetAsync()
        {
            if (_context.Entries != null)
            {
                Entries = await _context.Entries
                    .Where(e => e.EntryType == "SSD")
                    .OrderByDescending(e => e.EntryDate)
                    .ThenByDescending(e => e.EntryId)
                    .ToListAsync();
            }

            // Prefetch Customers
            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters.ToListAsync();
                foreach (var c in customers)
                {
                    if (!CustomerNames.ContainsKey(c.CustomerId))
                    {
                        CustomerNames.Add(c.CustomerId, c.CustomerName ?? "Unknown");
                    }
                }
            }

            // Prefetch Items
            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters.ToListAsync();
                foreach (var i in items)
                {
                    if (!ItemNames.ContainsKey(i.ItemId))
                    {
                        ItemNames.Add(i.ItemId, i.ItemName ?? "Unknown");
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(long id)
        {
            if (_context.Entries == null)
            {
                return NotFound();
            }
            var entry = await _context.Entries.FindAsync(id);

            if (entry != null)
            {
                _context.Entries.Remove(entry);
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = $"Sale Entry SSD-{entry.EntryNumber} deleted successfully.";
            }

            return RedirectToPage();
        }
    }
}
