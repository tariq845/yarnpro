using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Sales
{
    public class SodaModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SodaModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = default!;

        public SelectList CustomerList { get; set; } = default!;
        public SelectList ItemList { get; set; } = default!;
        public SelectList PurchaseSodaList { get; set; } = default!;
        
        // Helper list for JS date mapping
        public class PsdEntryInfo
        {
            public long EntryId { get; set; }
            public DateTime? EntryDate { get; set; }
            public double? OriginalQty { get; set; }
            public double RemainingQty { get; set; }
        }

        public List<PsdEntryInfo> PsdEntriesForJs { get; set; } = new List<PsdEntryInfo>();

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
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryDate = DateTime.Today,
                    EntryType = "SSD",
                    EntryRefType = "PSD"
                };
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                return Page();
            }

            // Calculations and Fixed Values
            if (Entry.Qty.HasValue && Entry.Rate.HasValue)
            {
                Entry.Amount = Entry.Qty.Value * Entry.Rate.Value;
            }
            
            // If EntryRefNo (selected PSD EntryNumber) is available, let's look up the details to ensure consistency
            if (Entry.EntryRefNo.HasValue)
            {
                 // We need to find the PSD entry to get its ID and Date
                 // The dropdown selects EntryNumber (as per requirement "Maps to Purchase Soda Entry Number") 
                 // BUT standard is to bind ID. 
                 // User said: "EntryRefNo Label: Against Purchase Soda No" -> Dropdown.
                 // "EntryRefId -> Auto populated -> Hidden".
                 // "EntryRefDate -> Auto populated".
                 
                 // If the dropdown binds to EntryRefNo (EnteryNumber), then we look up by Number.
                 // If it binds to EntryRefId (ID), we look up by ID.
                 // Usually Dropdowns bind to ID. Let's assume Dropdown Value is ID, and we store it in RefId?
                 // Wait user req: "EntryRefNo -> Dropdown... Maps to Purchase Soda Entry Number".
                 // This implies EntryRefNo holds the Number.
                 // "EntryRefId -> Auto populated... Hidden".
                 // So RefNo = Number, RefId = ID.
                 
                 // I will bind the dropdown to EntryRefId (Primary Key) for uniqueness, 
                 // and then Populate EntryRefNo and EntryRefDate from that.
                 // This is safer.
                 // But if user insists on binding EntryRefNo, I can do that.
                 // "Maps to Purchase Soda Entry Number" suggests RefNo field holds the number.
                 // Let's rely on RefId for the selection if possible as it's cleaner.
                 // But wait, "Auto populated based on selected Purchase Soda" for RefId.
                 // This implies the User selects RefNo? Or User selects the "Entry" and we populate both.
                 // I'll bind the SelectList Value to EntryNumber (RefNo) AND ID? No, Value is single.
                 // Best practice: Bind to ID (RefId). Use JS to populate RefNo and Date.
                 // OR Bind to RefNo.
                 
                 // Let's try: User selects "Purchase Entry". Value = EntryId.
                 // We bind this to `EntryRefId`.
                 // Then we look up EntryRefNo and Date.
                 // However, the field `EntryRefNo` is separate.
                 
                 // Alternative: I will follow the user's "EntryRefId -> Auto populated" literally.
                 // Meaning the user interacts with a control that sets RefNo? 
                 // Or maybe the dropdown IS RefNo?
                 // "EntryRefNo Label: Against Purchase Soda No -> Dropdown".
                 // Ok, I will bind the Dropdown to `Entry.EntryRefId` (so value is ID) 
                 // but displayed label is "PUR-SODA-123".
                 // Then on Save, I update `EntryRefNo` string/long from the ID.
                 // This seems most robust.
                 
                 // WAIT: `Entry.EntryRefNo` is long? Yes.
                 // `Entry.EntryRefId` is long? Yes.
                 // So both are nums.
                 // I'll bind the dropdown value to `EntryRefId` (the PK of the PSD entry).
            }

            if (Entry.EntryRefId.HasValue)
            {
               var psdEntry = await _context.Entries.FindAsync(Entry.EntryRefId);
               if (psdEntry != null)
               {
                   Entry.EntryRefNo = psdEntry.EntryNumber;
                   Entry.EntryRefDate = psdEntry.EntryDate;
                   Entry.EntryRefType = "PSD";
                   
                   // VALIDATION: Check if Sale Qty > Remaining Purchase Qty
                   // 1. Calculate Total Sold against this Purchase Entry
                   var totalSold = await _context.Entries
                       .Where(e => e.EntryType == "SSD" && e.EntryRefId == Entry.EntryRefId && e.EntryId != Entry.EntryId) // Exclude current if Edit
                       .SumAsync(e => e.Qty ?? 0);
                   
                   var purchaseQty = psdEntry.Qty ?? 0;
                   var remainingQty = purchaseQty - totalSold;

                   // Strict check: If Purchase Qty is 0, we can't sell against it at all.
                   if (purchaseQty <= 0)
                   {
                        ModelState.AddModelError("Entry.EntryRefId", "Selected Purchase Entry has 0 Quantity. Cannot create sale.");
                        await LoadDropdowns();
                        return Page();
                   }

                   if (Entry.Qty > remainingQty)
                   {
                       ModelState.AddModelError("Entry.Qty", $"Quantity cannot be greater than Remaining Balance ({remainingQty}). Purchase Qty: {purchaseQty}, Already Sold: {totalSold}");
                       await LoadDropdowns();
                       return Page();
                   }
               }
            }

            if (Entry.EntryId == 0)
            {
                // New
                Entry.EntryType = "SSD";
                Entry.CreationOn = DateTime.Now;

                // Generate Number
                var lastEntry = await _context.Entries
                    .Where(e => e.EntryType == "SSD")
                    .OrderByDescending(e => e.EntryNumber)
                    .FirstOrDefaultAsync();
                
                Entry.EntryNumber = (lastEntry?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
                TempData["StatusMessage"] = $"Sale Entry SSD-{Entry.EntryNumber} created successfully.";
            }
            else
            {
                // Update
                var existing = await _context.Entries.FindAsync(Entry.EntryId);
                if (existing == null) return NotFound();

                existing.EntryDate = Entry.EntryDate;
                existing.EntryAttribute1 = Entry.EntryAttribute1;
                existing.EntryRefId = Entry.EntryRefId; 
                existing.EntryRefNo = Entry.EntryRefNo; // Updated above
                existing.EntryRefDate = Entry.EntryRefDate; // Updated above
                existing.EntryRefType = "PSD";
                existing.ItemId = Entry.ItemId;
                existing.Qty = Entry.Qty;
                existing.Lbs = Entry.Lbs;
                existing.Rate = Entry.Rate;
                existing.Amount = Entry.Amount;
                existing.EntryRemarks = Entry.EntryRemarks;
                existing.LastUpdateOn = DateTime.Now;
                TempData["StatusMessage"] = $"Sale Entry SSD-{existing.EntryNumber} updated successfully.";
            }

            await _context.SaveChangesAsync();
            return RedirectToPage("./IndexSoda");
        }

        private async Task LoadDropdowns()
        {
            if (_context.CustomerMasters != null)
            {
                 var customers = await _context.CustomerMasters
                    .Where(c => c.CustomerDeactiveOn == null || c.CustomerDeactiveOn > DateTime.Today)
                    .Select(c => new { c.CustomerName, c.CustomerId })
                    .ToListAsync();
                 // Binding EntryAttribute1 to CustomerId (as string? ItemMaster/Supplier logic was ID)
                 // Requirement: "EntryAttribute1 Label: Customer".
                 // I'll bind to CustomerId.
                 CustomerList = new SelectList(customers, "CustomerId", "CustomerName");
            }

            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters
                     .Where(i => i.ItemDeactiveOn == null || i.ItemDeactiveOn > DateTime.Today)
                     .ToListAsync();
                ItemList = new SelectList(items, "ItemId", "ItemName");
            }
            
            if (_context.Entries != null)
            {
                var psdEntries = await _context.Entries
                    .Where(e => e.EntryType == "PSD")
                    .OrderByDescending(e => e.EntryDate)
                    .Select(e => new 
                    { 
                        e.EntryId, 
                        DisplayText = "PUR-SODA-" + e.EntryNumber + " (" + (e.EntryDate.HasValue ? e.EntryDate.Value.ToString("yyyy-MM-dd") : "") + ")"
                    })
                    .ToListAsync();
                
                // We bind to EntryRefId
                PurchaseSodaList = new SelectList(psdEntries, "EntryId", "DisplayText");
                
                // Populate raw list for JS with BALANCE calculation
                // 1. Get all SSD entries grouped by RefId
                var soldQtys = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.EntryRefId != null)
                    .GroupBy(e => e.EntryRefId)
                    .Select(g => new { RefId = g.Key, TotalSold = g.Sum(x => x.Qty ?? 0) })
                    .ToDictionaryAsync(x => x.RefId, x => x.TotalSold);

                var psdList = await _context.Entries
                    .Where(e => e.EntryType == "PSD")
                    .OrderByDescending(e => e.EntryDate)
                    .ToListAsync();

                PsdEntriesForJs = psdList.Select(e => {
                    double sold = 0;
                    if (soldQtys.ContainsKey(e.EntryId)) sold = soldQtys[e.EntryId];
                    
                    // IF EDIT MODE: If we are editing a Sale, we must ADD back its own Qty to the balance
                    // because the User sees "Available" as (Total - Others). 
                    // However, LoadDropdowns doesn't always know if it's Edit context easily here without passing ID.
                    // But effectively, frontend logic usually just checks "Max allowed".
                    // If I have 50 total. Sold 10 (this entry). Remaining 40.
                    // If I edit this entry, I want to see I can use up to 50 (40 + 10).
                    // So we need to handle that.
                    // But this list is global. JS will handle logic?
                    // Let's send the "Absolute Remaining excluding CURRENT interaction" which is tricky.
                    // Simplest: Send (Original - TotalSold). 
                    // And in JS, if Edit, we rely on the backend or we add the header value?
                    // Let's just send (Original - TotalSold) which is the TRUE remaining balance in DB.
                    // In Edit mode, we should ideally add the binded Entry.Qty to this limit in JS?
                    // OR we calculate it here if Entry is not null.
                    
                    if (Entry != null && Entry.EntryId != 0 && Entry.EntryRefId == e.EntryId)
                    {
                        sold -= (Entry.Qty ?? 0); // Remove current entry from sold count to show available including current
                    }

                    return new PsdEntryInfo 
                    {
                        EntryId = e.EntryId,
                        EntryDate = e.EntryDate,
                        OriginalQty = e.Qty,
                        RemainingQty = (e.Qty ?? 0) - sold
                    };
                }).ToList();
            }
        }
    }
}
