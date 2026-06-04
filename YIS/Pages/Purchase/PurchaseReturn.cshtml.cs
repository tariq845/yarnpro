using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Purchase
{
    public class PurchaseReturnModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PurchaseReturnModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = default!;

        [BindProperty]
        public List<LineItemViewModel> LineItems { get; set; } = new List<LineItemViewModel>();

        public SelectList SupplierList { get; set; } = default!;
        public SelectList ItemList { get; set; } = default!;
        
        public string DisplayEntryNumber { get; set; } = "DN-(Auto)";
        public bool IsPosted { get; set; }

        public class LineItemViewModel
        {
            public long EntryId { get; set; } 
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPercent { get; set; }
            public double? TaxAmount { get; set; } // GST Amt
            public double? NetAmount { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            await LoadDropdowns();

            if (id.HasValue && id > 0)
            {
                var entry = await _context.Entries.FindAsync(id);
                if (entry == null || entry.EntryType != "DN")
                {
                    return NotFound();
                }
                Entry = entry;
                
                DisplayEntryNumber = $"DN-{entry.EntryNumber}";

                // Check if posted
                IsPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "DNL")
                    .OrderBy(e => e.EntryId)
                    .ToListAsync();

                LineItems = childEntries.Select(c => new LineItemViewModel
                {
                    EntryId = c.EntryId,
                    ItemId = c.ItemId,
                    Qty = c.Qty,
                    Lbs = c.Lbs,
                    Rate = c.Rate,
                    Amount = c.Amount,
                    GstPercent = c.EntryAttribute2 != null ? double.Parse(c.EntryAttribute2) : 0, // Assuming GST % stored in Attr2 at line level? 
                    // Wait, legacy data: Attr2 was Header GST %. 
                    // If we move to Line Item GST %, we should store it in Line Item Entry. 
                    // Standard Purchase uses Line Items. 
                    // Purchase Return (DN) currently had Header GST %.
                    // If we want "UI same as Purchase", Purchase has Line Item GST.
                    // So we should support Line Item GST.
                    // For now, let's map it if exists.
                    TaxAmount = c.TaxAmount,
                    NetAmount = (c.Amount ?? 0) + (c.TaxAmount ?? 0)
                }).ToList();
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryDate = DateTime.Today,
                    EntryType = "DN"
                };
                DisplayEntryNumber = "DN-(Auto)";
                LineItems.Add(new LineItemViewModel { EntryId = 0 });
            }

            if (LineItems.Count == 0) LineItems.Add(new LineItemViewModel { EntryId = 0 });

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Check if posted
            if (Entry.EntryId > 0 && await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId))
            {
                TempData["ErrorMessage"] = "Cannot update: This entry has already been posted to the ledger.";
                return RedirectToPage("./PurchaseReturnIndex");
            }
             // Clear validations for fields not directly used or auto-filled
            ModelState.Remove("Entry.ItemId");
            ModelState.Remove("Entry.Qty");
            ModelState.Remove("Entry.Rate");
            
            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                DisplayEntryNumber = Entry.EntryId > 0 ? $"DN-{Entry.EntryNumber}" : "DN-(Auto)";
                return Page();
            }

            double grandTotal = 0;
            double totalQty = 0;
            foreach (var item in LineItems)
            {
                // Auto-calculate LBs = Qty * 100
                if (item.Qty.HasValue)
                {
                    item.Lbs = item.Qty.Value * 100;
                }

                // Enforce Rate to 4 decimal places
                if (item.Rate.HasValue)
                {
                    item.Rate = Math.Round(item.Rate.Value, 4);
                }

                // Default GST to 18% if not provided
                if (!item.GstPercent.HasValue || item.GstPercent == 0)
                {
                    item.GstPercent = 18;
                }

                if (item.Qty.HasValue && item.Rate.HasValue)
                {
                    item.Amount = (item.Qty.Value * item.Rate.Value);
                }
                // GST Amt
                if (item.GstPercent.HasValue && item.Amount.HasValue)
                {
                    item.TaxAmount = item.Amount.Value * (item.GstPercent.Value / 100);
                }
                
                grandTotal += (item.Amount ?? 0) + (item.TaxAmount ?? 0);
                totalQty += item.Qty ?? 0;
            }

            Entry.EntryType = "DN";
            Entry.Amount = grandTotal; 
            Entry.Qty = totalQty;
            
            // ENTRY_ATTRIBUTE3 : NET AMOUNT
            Entry.EntryAttribute3 = grandTotal.ToString();

            // Commission fields
            // ENTRY_ATTRIBUTE4 : COMMISSION AGENT
            // ENTRY_ATTRIBUTE5 : COMMISSION (YES/No)
            // ENTRY_COMM_PER : COMM %
            // ENTRY_COMM_AMT : AMOUNT
            // ENTRY_TERMS_DAYS : TERMS DAYS
            // ENTRY_COMM_SUKRI : SUKRI
            
            if (Entry.EntryId == 0)
            {
                Entry.CreationOn = DateTime.Now;
     
                var last = await _context.Entries
                    .Where(e => e.EntryType == "DN")
                    .OrderByDescending(e => e.EntryNumber)
                    .FirstOrDefaultAsync();
                Entry.EntryNumber = (last?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
                await _context.SaveChangesAsync();

                foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
                {
                    var child = new Entry
                    {
                        EntryId = 0,
                        EntryType = "DNL",
                        EntryDate = Entry.EntryDate,
                        EntryRefId = Entry.EntryId,
                        EntryAttribute1 = Entry.EntryAttribute1,
                        ItemId = l.ItemId,
                        Qty = l.Qty,
                        Lbs = l.Lbs,
                        Rate = l.Rate,
                        Amount = l.Amount,
                        TaxAmount = l.TaxAmount, // GST AMT
                        EntryAttribute2 = l.GstPercent?.ToString(), // GST %
                        CreationOn = DateTime.Now
                    };
                    _context.Entries.Add(child);
                }
            }
            else
            {
                 var existing = await _context.Entries.FindAsync(Entry.EntryId);
                 if (existing == null) return NotFound();
                 
                 // Update Header
                 existing.EntryDate = Entry.EntryDate;
                 existing.EntryAttribute1 = Entry.EntryAttribute1;
                 existing.EntryRefNo = Entry.EntryRefNo;
                 existing.EntryRefCode = Entry.EntryRefCode;
                 existing.EntryAttribute2 = Entry.EntryAttribute2; // GST %
                 existing.EntryAttribute3 = grandTotal.ToString(); // NET AMOUNT
                 existing.EntryAttribute4 = Entry.EntryAttribute4; // COMM AGENT
                 existing.EntryAttribute5 = Entry.EntryAttribute5; // COMM YES/NO
                 
                 existing.EntryCommPer = Entry.EntryCommPer;
                 existing.EntryCommAmt = Entry.EntryCommAmt;
                 existing.EntryTermsDays = Entry.EntryTermsDays;
                 existing.EntryCommSukri = Entry.EntryCommSukri;
                 
                 existing.EntryRemarks = Entry.EntryRemarks;
                 existing.Amount = grandTotal;
                 existing.Qty = totalQty;
                 existing.LastUpdateOn = DateTime.Now;
                 
                 // Manage Children
                 var existingChildren = await _context.Entries
                    .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "DNL")
                    .ToListAsync();
                 _context.Entries.RemoveRange(existingChildren);
                 
                 foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
                {
                    var child = new Entry
                    {
                        EntryId = 0,
                        EntryType = "DNL",
                        EntryDate = Entry.EntryDate,
                        EntryRefId = Entry.EntryId,
                        EntryAttribute1 = Entry.EntryAttribute1,
                        ItemId = l.ItemId,
                        Qty = l.Qty,
                        Lbs = l.Lbs,
                        Rate = l.Rate,
                        Amount = l.Amount,
                        TaxAmount = l.TaxAmount,
                        EntryAttribute2 = l.GstPercent?.ToString(),
                        CreationOn = DateTime.Now
                    };
                    _context.Entries.Add(child);
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = Entry.EntryId == 0 ? "Purchase Return created successfully!" : "Purchase Return updated successfully!";
            return RedirectToPage("./PurchaseReturnIndex");
        }

        public SelectList PurchaseList { get; set; } = default!;
        
        // ... (existing properties)

        public List<PurchaseEntryInfo>? PurchaseEntriesForJs { get; set; }
        
        public class PurchaseEntryInfo
        {
            public long EntryNumber { get; set; }
            public long EntryId { get; set; }
            public string? DisplayText { get; set; }
            public string? SupplierId { get; set; }
            public List<PurLineItem>? Items { get; set; }
        }

        public class PurLineItem
        {
            public long ItemId { get; set; }
            public string? ItemName { get; set; }
            public double Qty { get; set; }
            public double Rate { get; set; }
        }

        private async Task LoadDropdowns()
        {
            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters
                     .Where(s => s.SupplierDeactiveOn == null || s.SupplierDeactiveOn > DateTime.Today)
                    .Select(s => new { s.SupplierId, s.SupplierName })
                    .OrderBy(s => s.SupplierName)
                    .ToListAsync();
                SupplierList = new SelectList(suppliers, "SupplierId", "SupplierName");
            }
            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters
                     .Where(i => i.ItemDeactiveOn == null || i.ItemDeactiveOn > DateTime.Today)
                    .OrderBy(i => i.ItemName)
                    .ToListAsync();
                ItemList = new SelectList(items, "ItemId", "ItemName");
            }

            
            if (_context.Entries != null) 
            {
                // Fetch PUR entries with basic info
                var purRaw = await _context.Entries
                    .Where(e => e.EntryType == "PUR")
                    .OrderByDescending(e => e.EntryDate)
                     .ThenByDescending(e => e.EntryNumber)
                    .Select(e => new { e.EntryId, e.EntryNumber, e.EntryDate, e.EntryAttribute1 })
                    .ToListAsync();

                // Fetch PUR Line Items (PURL)
                var purLineItems = await _context.Entries
                    .Where(e => e.EntryType == "PURL" && e.EntryRefId != null)
                    .Join(_context.Entries, child => child.EntryRefId, parent => parent.EntryId, (child, parent) => new { parent.EntryId, child })
                    .Join(_context.ItemMasters, purl => purl.child.ItemId, item => item.ItemId, (purl, item) => new { 
                        purl.EntryId, 
                        purl.child.ItemId, 
                        ItemName = item.ItemName, 
                        purl.child.Qty, 
                        purl.child.Rate 
                    })
                    .ToListAsync();

                var groupedItems = purLineItems
                    .GroupBy(x => x.EntryId)
                    .ToDictionary(g => g.Key, g => g.Select(x => new PurLineItem 
                    { 
                        ItemId = x.ItemId ?? 0, 
                        ItemName = x.ItemName,
                        Qty = x.Qty ?? 0,
                        Rate = x.Rate ?? 0
                    }).ToList());

                PurchaseEntriesForJs = purRaw.Select(p => {
                    List<PurLineItem>? items = groupedItems.ContainsKey(p.EntryId) ? groupedItems[p.EntryId] : null;
                    
                    string itemsStr = items != null && items.Any() 
                        ? " - " + string.Join(", ", items.Select(i => i.ItemName).Distinct()) 
                        : "";

                    return new PurchaseEntryInfo
                    {
                        EntryNumber = p.EntryNumber ?? 0,
                        EntryId = p.EntryId,
                        DisplayText = "PUR-" + p.EntryNumber + " (" + (p.EntryDate.HasValue ? p.EntryDate.Value.ToString("yyyy-MM-dd") : "") + ")" + itemsStr,
                        SupplierId = p.EntryAttribute1,
                        Items = items
                    };
                }).ToList();

                PurchaseList = new SelectList(PurchaseEntriesForJs, "EntryNumber", "DisplayText");
            }
        }
    }
}
