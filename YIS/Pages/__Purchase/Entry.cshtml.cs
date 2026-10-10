using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Purchase
{
    public class EntryModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public EntryModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = default!;

        [BindProperty]
        public List<PurchaseLineItemViewModel> LineItems { get; set; } = new List<PurchaseLineItemViewModel>();

        public SelectList SupplierList { get; set; } = default!;
        public SelectList ItemList { get; set; } = default!;
        public SelectList PurchaseSodaList { get; set; } = default!;
        public SelectList CommissionAgentList { get; set; } = default!;

        public string DisplayEntryNumber { get; set; } = "PUR-(Auto)";

        public class PurchaseLineItemViewModel
        {
            public long EntryId { get; set; } // 0 for new items
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; } // Basic Amount
            public double? GstPercent { get; set; } // EntryAttribute2
            public double? GstAmount { get; set; } // TaxAmount
            public double? NetAmount { get; set; } // EntryAttribute3
        }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            await LoadDropdowns();

            if (id.HasValue)
            {
                var entry = await _context.Entries.FindAsync(id);
                if (entry == null)
                {
                    return NotFound();
                }
                Entry = entry;
                DisplayEntryNumber = $"PUR-{Entry.EntryNumber}";

                // Load child entries
                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "PURL")
                    .OrderBy(e => e.EntryId)
                    .ToListAsync();

                LineItems = childEntries.Select(c =>
                {
                    double.TryParse(c.EntryAttribute2, out double gstPer);
                    double.TryParse(c.EntryAttribute3, out double netAmt);

                    return new PurchaseLineItemViewModel
                    {
                        EntryId = c.EntryId,
                        ItemId = c.ItemId,
                        Qty = c.Qty,
                        Lbs = c.Lbs,
                        Rate = c.Rate,
                        Amount = c.Amount,
                        GstPercent = gstPer,
                        GstAmount = c.TaxAmount,
                        NetAmount = netAmt
                    };
                }).ToList();

                // Handle Legacy Single Item Entries (if no children found but item exists)
                if (LineItems.Count == 0 && Entry.ItemId.HasValue)
                {
                     double.TryParse(Entry.EntryAttribute2, out double gstPer);
                     double.TryParse(Entry.EntryAttribute3, out double netAmt);

                     LineItems.Add(new PurchaseLineItemViewModel
                     {
                         EntryId = 0, // Treat as new child to be created on save
                         ItemId = Entry.ItemId,
                         Qty = Entry.Qty,
                         Lbs = Entry.Lbs,
                         Rate = Entry.Rate,
                         Amount = Entry.Amount,
                         GstPercent = gstPer,
                         GstAmount = Entry.TaxAmount,
                         NetAmount = netAmt
                     });
                }
            }
            else
            {
                // Initialize default values for new entry
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryDate = DateTime.Today,
                    EntryType = "PUR",
                    EntryAttribute5 = "No" // Default Commission to No
                };
                LineItems.Add(new PurchaseLineItemViewModel { EntryId = 0 });
            }
            
            if (LineItems.Count == 0)
            {
                 LineItems.Add(new PurchaseLineItemViewModel { EntryId = 0 });
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Remove validation for Entry.ItemId as it's now in LineItems
            ModelState.Remove("Entry.ItemId");
            ModelState.Remove("Entry.Qty");
            ModelState.Remove("Entry.Rate");

            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                DisplayEntryNumber = Entry.EntryId > 0 ? $"PUR-{Entry.EntryNumber}" : "PUR-(Auto)";
                return Page();
            }

            // Calculate totals from Line Items
            double grandTotalBasic = 0;
            double grandTotalGst = 0;
            double grandTotalNet = 0;

            foreach (var item in LineItems)
            {
                if (item.Qty.HasValue && item.Rate.HasValue)
                {
                    item.Amount = item.Qty.Value * item.Rate.Value;
                }
                
                // Recalculate item totals to be safe
                double basicAmt = item.Amount ?? 0;
                double gstAmt = basicAmt * ((item.GstPercent ?? 0) / 100);
                item.GstAmount = gstAmt;
                item.NetAmount = basicAmt + gstAmt;

                grandTotalBasic += basicAmt;
                grandTotalGst += gstAmt;
                grandTotalNet += item.NetAmount ?? 0;
            }

            // Commission Calculations (on Grand Total Net Amount usually, or Basic? Based on previous logic it was on Amount)
            // Previous logic: CommAmt = Amount * (CommPer / 100).
            // Let's assume Commission is on the Grand Total BASIC Amount for now, unless specified otherwise.
            // Wait, previous code: Entry.EntryCommAmt = Entry.Amount.Value * (Entry.EntryCommPer.Value / 100);
            // Entry.Amount used to be Qty*Rate. So it's Basic Amount.
            
            if (Entry.EntryAttribute5 == "No")
            {
                Entry.EntryCommPer = 0;
                Entry.EntryCommAmt = 0;
            }
            else if (Entry.EntryCommPer.HasValue)
            {
                Entry.EntryCommAmt = grandTotalBasic * (Entry.EntryCommPer.Value / 100);
            }

            if (Entry.EntryId == 0)
            {
                // New Parent Entry
                Entry.EntryType = "PUR";
                Entry.CreationOn = DateTime.Now;
                
                // Store Grand Totals in Parent
                Entry.Amount = grandTotalBasic; 
                Entry.TaxAmount = grandTotalGst;
                Entry.EntryAttribute3 = grandTotalNet.ToString("F2");
                
                // Generate Entry Number
                var lastEntry = await _context.Entries
                    .Where(e => e.EntryType == "PUR")
                    .OrderByDescending(e => e.EntryNumber)
                    .FirstOrDefaultAsync();

                Entry.EntryNumber = (lastEntry?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
                await _context.SaveChangesAsync(); // Get ID

                // Create Child Entries
                foreach (var item in LineItems.Where(l => l.ItemId.HasValue))
                {
                    var childEntry = new Entry
                    {
                        EntryId = 0,
                        EntryType = "PURL",
                        EntryDate = Entry.EntryDate,
                        EntryRefId = Entry.EntryId,
                        EntryAttribute1 = Entry.EntryAttribute1, // Supplier
                        
                        ItemId = item.ItemId,
                        Qty = item.Qty,
                        Lbs = item.Lbs,
                        Rate = item.Rate,
                        Amount = item.Amount,
                        
                        EntryAttribute2 = item.GstPercent?.ToString(),
                        TaxAmount = item.GstAmount,
                        EntryAttribute3 = item.NetAmount?.ToString(),
                        
                        CreationOn = DateTime.Now
                    };
                    _context.Entries.Add(childEntry);
                }
                TempData["StatusMessage"] = $"Purchase Entry PUR-{Entry.EntryNumber} created successfully.";
            }
            else
            {
                // Update Existing Parent
                var existingEntry = await _context.Entries.FindAsync(Entry.EntryId);
                if (existingEntry == null) return NotFound();

                existingEntry.EntryDate = Entry.EntryDate;
                existingEntry.EntryRefNo = Entry.EntryRefNo;
                existingEntry.EntryRefCode = Entry.EntryRefCode;
                existingEntry.EntryAttribute1 = Entry.EntryAttribute1;
                existingEntry.EntryAttribute4 = Entry.EntryAttribute4; // Agent
                existingEntry.EntryAttribute5 = Entry.EntryAttribute5; // Comm Yes/No
                
                existingEntry.Amount = grandTotalBasic;
                existingEntry.TaxAmount = grandTotalGst;
                existingEntry.EntryAttribute3 = grandTotalNet.ToString("F2");
                
                existingEntry.EntryCommPer = Entry.EntryCommPer;
                existingEntry.EntryCommAmt = Entry.EntryCommAmt;
                existingEntry.EntryTermsDays = Entry.EntryTermsDays;
                existingEntry.EntryCommSukri = Entry.EntryCommSukri;
                existingEntry.EntryRemarks = Entry.EntryRemarks;
                existingEntry.LastUpdateOn = DateTime.Now;

                // Handle Child Entries
                var existingChildren = await _context.Entries
                    .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "PURL")
                    .ToListAsync();
                
                var submittedIds = LineItems.Where(l => l.EntryId > 0).Select(l => l.EntryId).ToHashSet();

                // Delete removed
                foreach (var child in existingChildren)
                {
                    if (!submittedIds.Contains(child.EntryId))
                    {
                        _context.Entries.Remove(child);
                    }
                }

                // Update/Create
                foreach (var item in LineItems.Where(l => l.ItemId.HasValue))
                {
                    if (item.EntryId > 0)
                    {
                        var child = existingChildren.FirstOrDefault(c => c.EntryId == item.EntryId);
                        if (child != null)
                        {
                            child.ItemId = item.ItemId;
                            child.Qty = item.Qty;
                            child.Lbs = item.Lbs;
                            child.Rate = item.Rate;
                            child.Amount = item.Amount;
                            child.EntryAttribute2 = item.GstPercent?.ToString();
                            child.TaxAmount = item.GstAmount;
                            child.EntryAttribute3 = item.NetAmount?.ToString();
                            child.LastUpdateOn = DateTime.Now;
                        }
                    }
                    else
                    {
                        var newChild = new Entry
                        {
                            EntryId = 0,
                            EntryType = "PURL",
                            EntryDate = Entry.EntryDate,
                            EntryRefId = Entry.EntryId,
                            EntryAttribute1 = Entry.EntryAttribute1,
                            
                            ItemId = item.ItemId,
                            Qty = item.Qty,
                            Lbs = item.Lbs,
                            Rate = item.Rate,
                            Amount = item.Amount,
                            
                            EntryAttribute2 = item.GstPercent?.ToString(),
                            TaxAmount = item.GstAmount,
                            EntryAttribute3 = item.NetAmount?.ToString(),
                            
                            CreationOn = DateTime.Now
                        };
                        _context.Entries.Add(newChild);
                    }
                }
                TempData["StatusMessage"] = $"Purchase Entry PUR-{existingEntry.EntryNumber} updated successfully.";
            }

            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }

        private async Task LoadDropdowns()
        {
            // Load Suppliers
            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters
                    .Where(s => s.SupplierDeactiveOn == null || s.SupplierDeactiveOn > DateTime.Today)
                    .Select(s => new { s.SupplierId, s.SupplierName })
                    .ToListAsync();
                SupplierList = new SelectList(suppliers, "SupplierId", "SupplierName");
            }

            // Load Items
            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters
                    .Where(i => i.ItemDeactiveOn == null || i.ItemDeactiveOn > DateTime.Today)
                    .ToListAsync();
                ItemList = new SelectList(items, "ItemId", "ItemName");
            }

            // Load Purchase Soda entries for reference
            if (_context.Entries != null)
            {
                var psdEntries = await _context.Entries
                    .Where(e => e.EntryType == "PSD")
                    .OrderByDescending(e => e.EntryDate)
                     .ThenByDescending(e => e.EntryNumber)
                    .Select(e => new
                    {
                        e.EntryNumber,
                        DisplayText = "PUR-SODA-" + e.EntryNumber + " (" + (e.EntryDate.HasValue ? e.EntryDate.Value.ToString("yyyy-MM-dd") : "") + ")"
                    })
                    .ToListAsync();
                PurchaseSodaList = new SelectList(psdEntries, "EntryNumber", "DisplayText");
            }

            // Commission Agents - using Suppliers for now
            if (_context.SupplierMasters != null)
            {
                var agents = await _context.SupplierMasters
                    .Where(s => s.SupplierDeactiveOn == null || s.SupplierDeactiveOn > DateTime.Today)
                    .Select(s => new { s.SupplierId, s.SupplierName })
                    .ToListAsync();
                CommissionAgentList = new SelectList(agents, "SupplierId", "SupplierName");
            }
        }
    }
}
