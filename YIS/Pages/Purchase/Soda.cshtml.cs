using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Purchase
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

        [BindProperty]
        public List<LineItemViewModel> LineItems { get; set; } = new List<LineItemViewModel>();

        public SelectList SupplierList { get; set; } = default!;
        public SelectList ItemList { get; set; } = default!;

        // Display property for showing Entry Number in edit mode
        public string DisplayEntryNumber { get; set; } = "PSD-(Auto)";

        // ViewModel for line items
        public class LineItemViewModel
        {
            public long EntryId { get; set; } // 0 for new items
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPercent { get; set; } = 0;
            public double? TaxAmount { get; set; }
            public double? NetAmount { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            await LoadDropdowns();

            if (id.HasValue)
            {
                // Load existing parent entry
                var entry = await _context.Entries.FindAsync(id);
                if (entry == null)
                {
                    return NotFound();
                }
                Entry = entry;

                // Set display entry number for edit mode
                DisplayEntryNumber = $"PSD-{entry.EntryNumber}";

                // Load child line items
                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "PSDL")
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
                    GstPercent = double.TryParse(c.EntryAttribute2, out double gst) ? gst : 0,
                    TaxAmount = c.TaxAmount ?? ((c.Amount ?? 0) * ((double.TryParse(c.EntryAttribute2, out double g) ? g : 0) / 100.0)),
                    NetAmount = (c.Amount ?? 0) + (c.TaxAmount ?? ((c.Amount ?? 0) * ((double.TryParse(c.EntryAttribute2, out double g2) ? g2 : 0) / 100.0)))
                }).ToList();

                // If no child items exist (old data), create one from parent
                if (LineItems.Count == 0 && Entry.ItemId.HasValue)
                {
                    double gst = double.TryParse(Entry.EntryAttribute2, out double g) ? g : 0;
                    double tax = Entry.TaxAmount ?? ((Entry.Amount ?? 0) * (gst / 100.0));
                    LineItems.Add(new LineItemViewModel
                    {
                        EntryId = 0,
                        ItemId = Entry.ItemId,
                        Qty = Entry.Qty,
                        Lbs = Entry.Lbs,
                        Rate = Entry.Rate,
                        Amount = Entry.Amount,
                        GstPercent = gst,
                        TaxAmount = tax,
                        NetAmount = (Entry.Amount ?? 0) + tax
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
                    EntryType = "PSD",
                    EntryAttribute2 = "Credit",
                    EntryAttribute5 = "No",
                    EntryCommPer = 10
                };
                DisplayEntryNumber = "PSD-(Auto)";

                // Add one empty line item
                LineItems.Add(new LineItemViewModel { EntryId = 0, GstPercent = 0 });
            }

            // Ensure at least one line item
            if (LineItems.Count == 0)
            {
                LineItems.Add(new LineItemViewModel { EntryId = 0, GstPercent = 0 });
            }

            if (Entry != null && string.IsNullOrEmpty(Entry.EntryAttribute2))
            {
                Entry.EntryAttribute2 = "Credit";
            }
            if (Entry != null && string.IsNullOrEmpty(Entry.EntryAttribute5))
            {
                Entry.EntryAttribute5 = "No";
            }
            if (Entry != null && !Entry.EntryCommPer.HasValue)
            {
                Entry.EntryCommPer = 10;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Remove validation for Entry.ItemId since we use line items
            ModelState.Remove("Entry.ItemId");
            ModelState.Remove("Entry.Qty");
            ModelState.Remove("Entry.Rate");

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" | ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                TempData["ErrorMessage"] = "Validation Error: " + errors;

                await LoadDropdowns();
                DisplayEntryNumber = Entry.EntryId > 0 ? $"PSD-{Entry.EntryNumber}" : "PSD-(Auto)";
                return Page();
            }

            // Calculate grand totals and line item amounts from line items
            double grandTotalBasic = 0;
            double grandTotalTax = 0;
            double grandTotalNet = 0;
            double totalQty = 0;
            foreach (var item in LineItems)
            {
                if (item.Qty.HasValue && (!item.Lbs.HasValue || item.Lbs == 0))
                {
                    item.Lbs = item.Qty.Value * 100;
                }
                if (item.Lbs.HasValue && item.Rate.HasValue)
                {
                    item.Amount = item.Lbs.Value * item.Rate.Value;
                }
                if (item.Amount.HasValue && item.GstPercent.HasValue)
                {
                    item.TaxAmount = item.Amount.Value * (item.GstPercent.Value / 100.0);
                }
                else
                {
                    item.TaxAmount = 0;
                }
                item.NetAmount = (item.Amount ?? 0) + (item.TaxAmount ?? 0);

                grandTotalBasic += item.Amount ?? 0;
                grandTotalTax += item.TaxAmount ?? 0;
                grandTotalNet += item.NetAmount ?? 0;
                totalQty += item.Qty ?? 0;
            }

            // Commission Calculation
            if (Entry.EntryAttribute5 == "Yes")
            {
                double commPer = Entry.EntryCommPer ?? 10;
                Entry.EntryCommPer = commPer;
                Entry.EntryCommAmt = grandTotalNet * (commPer / 100.0);
                Entry.EntryCommSukri = (Entry.EntryCommAmt ?? 0) * 0.10;
            }
            else
            {
                Entry.EntryAttribute5 = "No";
                Entry.EntryAttribute4 = null;
                Entry.EntryCommPer = null;
                Entry.EntryCommAmt = null;
                Entry.EntryCommSukri = null;
            }

            // Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (Entry.EntryId == 0)
            {
                // New Parent Entry
                Entry.UcpId = activeUcpId;
                Entry.EntryType = "PSD";
                Entry.CreationOn = DateTime.Now;
                Entry.Amount = grandTotalNet; // Store grand total including GST
                Entry.TaxAmount = grandTotalTax; // Store total GST
                Entry.Qty = totalQty; // Store total qty
                Entry.EntryAttribute3 = grandTotalNet.ToString("F2");

                // Generate Entry Number
                var lastEntry = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId)
                    .OrderByDescending(e => e.EntryNumber)
                    .FirstOrDefaultAsync();

                long nextNumber = (lastEntry?.EntryNumber ?? 0) + 1;
                Entry.EntryNumber = nextNumber;

                _context.Entries.Add(Entry);
                await _context.SaveChangesAsync(); // Save to get EntryId

                // Create child line item entries
                foreach (var lineItem in LineItems.Where(l => l.ItemId.HasValue))
                {
                    var childEntry = new Entry
                    {
                        EntryId = 0,
                        EntryType = "PSDL", // Purchase Soda Line
                        EntryDate = Entry.EntryDate,
                        EntryRefId = Entry.EntryId, // Link to parent
                        EntryAttribute1 = Entry.EntryAttribute1, // Same supplier
                        EntryAttribute2 = lineItem.GstPercent?.ToString(), // GST %
                        EntryAttribute3 = lineItem.NetAmount?.ToString(), // Net Amount
                        TaxAmount = lineItem.TaxAmount, // GST Amount
                        ItemId = lineItem.ItemId,
                        Qty = lineItem.Qty,
                        Lbs = lineItem.Lbs,
                        Rate = lineItem.Rate,
                        Amount = lineItem.Amount,
                        UcpId = activeUcpId,
                        CreationOn = DateTime.Now
                    };
                    _context.Entries.Add(childEntry);
                }
            }
            else
            {
                // Update Existing Parent Entry
                var existingEntry = await _context.Entries.FindAsync(Entry.EntryId);
                if (existingEntry == null)
                {
                    return NotFound();
                }

                // Update parent fields
                existingEntry.EntryDate = Entry.EntryDate;
                existingEntry.EntryAttribute1 = Entry.EntryAttribute1;
                existingEntry.EntryAttribute2 = Entry.EntryAttribute2;
                existingEntry.EntryTermsDays = Entry.EntryTermsDays;
                existingEntry.EntryRemarks = Entry.EntryRemarks;
                existingEntry.EntryAttribute4 = Entry.EntryAttribute4;
                existingEntry.EntryAttribute5 = Entry.EntryAttribute5;
                existingEntry.EntryCommPer = Entry.EntryCommPer;
                existingEntry.EntryCommAmt = Entry.EntryCommAmt;
                existingEntry.EntryCommSukri = Entry.EntryCommSukri;
                existingEntry.Amount = grandTotalNet;
                existingEntry.TaxAmount = grandTotalTax;
                existingEntry.Qty = totalQty;
                existingEntry.EntryAttribute3 = grandTotalNet.ToString("F2");
                existingEntry.UcpId = activeUcpId;
                existingEntry.LastUpdateOn = DateTime.Now;

                // Get existing child entries
                var existingChildren = await _context.Entries
                    .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "PSDL")
                    .ToListAsync();

                // Track which children to keep
                var submittedIds = LineItems.Where(l => l.EntryId > 0).Select(l => l.EntryId).ToHashSet();

                // Delete removed children
                foreach (var child in existingChildren)
                {
                    if (!submittedIds.Contains(child.EntryId))
                    {
                        _context.Entries.Remove(child);
                    }
                }

                // Update or create line items
                foreach (var lineItem in LineItems.Where(l => l.ItemId.HasValue))
                {
                    if (lineItem.EntryId > 0)
                    {
                        // Update existing child
                        var childToUpdate = existingChildren.FirstOrDefault(c => c.EntryId == lineItem.EntryId);
                        if (childToUpdate != null)
                        {
                            childToUpdate.ItemId = lineItem.ItemId;
                            childToUpdate.Qty = lineItem.Qty;
                            childToUpdate.Lbs = lineItem.Lbs;
                            childToUpdate.Rate = lineItem.Rate;
                            childToUpdate.Amount = lineItem.Amount;
                            childToUpdate.EntryAttribute2 = lineItem.GstPercent?.ToString();
                            childToUpdate.EntryAttribute3 = lineItem.NetAmount?.ToString();
                            childToUpdate.TaxAmount = lineItem.TaxAmount;
                            childToUpdate.UcpId = activeUcpId;
                            childToUpdate.LastUpdateOn = DateTime.Now;
                        }
                    }
                    else
                    {
                        // Create new child
                        var newChild = new Entry
                        {
                            EntryId = 0,
                            EntryType = "PSDL",
                            EntryDate = Entry.EntryDate,
                            EntryRefId = Entry.EntryId,
                            EntryAttribute1 = Entry.EntryAttribute1,
                            EntryAttribute2 = lineItem.GstPercent?.ToString(),
                            EntryAttribute3 = lineItem.NetAmount?.ToString(),
                            TaxAmount = lineItem.TaxAmount,
                            ItemId = lineItem.ItemId,
                            Qty = lineItem.Qty,
                            Lbs = lineItem.Lbs,
                            Rate = lineItem.Rate,
                            Amount = lineItem.Amount,
                            UcpId = activeUcpId,
                            CreationOn = DateTime.Now
                        };
                        _context.Entries.Add(newChild);
                    }
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = Entry.EntryId == 0 ? "Purchase Soda created successfully!" : "Purchase Soda updated successfully!";
            return RedirectToPage("./IndexSoda");
        }

        private async Task LoadDropdowns()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return;

            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters
                    .Where(s => s.UcpId == activeUcpId && (s.SupplierDeactiveOn == null || s.SupplierDeactiveOn > DateTime.Today))
                    .Select(s => new { s.SupplierName, s.SupplierId })
                    .ToListAsync();

                SupplierList = new SelectList(suppliers, "SupplierId", "SupplierName");
            }

            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters
                    .Where(i => i.UcpId == activeUcpId && (i.ItemDeactiveOn == null || i.ItemDeactiveOn > DateTime.Today))
                    .ToListAsync();
                ItemList = new SelectList(items, "ItemId", "ItemName");
            }
        }
    }
}
