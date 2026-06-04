using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

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
        public List<LineItemViewModel> LineItems { get; set; } = new List<LineItemViewModel>();

        public SelectList SupplierList { get; set; } = default!;
        public SelectList ItemList { get; set; } = default!;
        public SelectList PurchaseSodaList { get; set; } = default!;

        public string DisplayEntryNumber { get; set; } = "PUR-(Auto)";
        public bool IsPosted { get; set; }

        // Data for JS Filtering
        public List<PsdEntryInfo>? PsdEntriesForJs { get; set; }

        public class PsdEntryInfo
        {
            public long EntryNumber { get; set; }
            public long EntryId { get; set; }
            public string? DisplayText { get; set; }
            public string? SupplierId { get; set; }
            public List<PsdLineItem>? Items { get; set; }
        }

        public class PsdLineItem
        {
            public long ItemId { get; set; }
            public string? ItemName { get; set; }
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double ConsumedQty { get; set; }
            public double RemainingQty { get; set; }
        }

        public class LineItemViewModel
        {
            public long EntryId { get; set; }
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPercent { get; set; }
            public double? TaxAmount { get; set; }
            public double? NetAmount { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long? id, long? psdId)
        {
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanRead(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Purchase entries.";
                return RedirectToPage("/Index");
            }

            if (id.HasValue && id > 0)
            {
                var entry = await _context.Entries.FindAsync(id);
                if (entry == null || entry.EntryType != "PUR")
                {
                    return NotFound();
                }
                Entry = entry;

                DisplayEntryNumber = $"PUR-{entry.EntryNumber}";

                // Check if posted
                IsPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "PURL")
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
                    GstPercent = c.EntryAttribute2 != null ? double.Parse(c.EntryAttribute2) : 0,
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
                    EntryType = "PUR",
                    EntryRefNo = psdId
                };
                DisplayEntryNumber = "PUR-(Auto)";
                LineItems.Add(new LineItemViewModel { EntryId = 0 });
            }

            if (LineItems.Count == 0) LineItems.Add(new LineItemViewModel { EntryId = 0 });

            await LoadDropdowns();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var path = HttpContext.Request.Path.Value ?? "";
            bool isNew = Entry.EntryId == 0;

            if (isNew && !AuthHelper.CanCreate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to create Purchase entries.";
                return RedirectToPage("./Index");
            }

            if (!isNew && !AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to update Purchase entries.";
                return RedirectToPage("./Index");
            }

            if (!isNew)
            {
                // Check if posted
                if (await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId))
                {
                    TempData["ErrorMessage"] = "Cannot update: This purchase entry has been posted to the ledger.";
                    return RedirectToPage("./Index");
                }
            }

            ModelState.Remove("Entry.ItemId");
            ModelState.Remove("Entry.Qty");
            ModelState.Remove("Entry.Rate");
            ModelState.Remove("Entry.EntryRefCode"); // Remove validation for optional supplier purchase number

            // if (!ModelState.IsValid)
            // {
            //     var errors = string.Join(" | ", ModelState.Values
            //         .SelectMany(v => v.Errors)
            //         .Select(e => e.ErrorMessage));
            //     TempData["ErrorMessage"] = "Validation Error: " + errors;

            //     await LoadDropdowns();
            //     DisplayEntryNumber = Entry.EntryId > 0 ? $"PUR-{Entry.EntryNumber}" : "PUR-(Auto)";
            //     return Page();
            // }

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

                if (item.Lbs.HasValue && item.Rate.HasValue)
                {
                    item.Amount = (item.Lbs.Value * item.Rate.Value);
                }
                if (item.GstPercent.HasValue && item.Amount.HasValue)
                {
                    item.TaxAmount = item.Amount.Value * (item.GstPercent.Value / 100);
                }

                grandTotal += (item.Amount ?? 0) + (item.TaxAmount ?? 0);
                totalQty += item.Qty ?? 0;
            }

            Entry.EntryType = "PUR";
            Entry.Amount = grandTotal;
            Entry.Qty = totalQty;
            Entry.EntryAttribute3 = grandTotal.ToString(); // NET AMOUNT

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Get Active Company Profile from Session
                long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
                if (!activeUcpId.HasValue)
                {
                    TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                    return RedirectToPage("/SelectCompany");
                }

                if (Entry.EntryId == 0)
                {
                    Entry.UcpId = activeUcpId;
                    Entry.CreationOn = DateTime.Now;

                    var last = await _context.Entries
                        .AsNoTracking()
                        .Where(e => e.EntryType == "PUR" && e.UcpId == activeUcpId)
                        .OrderByDescending(e => e.EntryNumber)
                        .FirstOrDefaultAsync();
                    Entry.EntryNumber = (last?.EntryNumber ?? 0) + 1;

                    Entry.EntryAttribute2 = Entry.EntryRefNo?.ToString();
                    _context.Entries.Add(Entry);
                    await _context.SaveChangesAsync();

                    foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
                    {
                        var child = new Entry
                        {
                            EntryId = 0,
                            EntryType = "PURL",
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
                            UcpId = activeUcpId,
                            CreationOn = DateTime.Now
                        };
                        _context.Entries.Add(child);
                    }
                }
                else
                {
                    var existing = await _context.Entries.FindAsync(Entry.EntryId);
                    if (existing == null) return NotFound();

                    existing.EntryDate = Entry.EntryDate;
                    existing.EntryAttribute1 = Entry.EntryAttribute1;
                    existing.EntryRefNo = Entry.EntryRefNo;
                    existing.EntryRefCode = Entry.EntryRefCode;
                    existing.EntryAttribute2 = Entry.EntryRefNo?.ToString();
                    existing.EntryAttribute3 = grandTotal.ToString();
                    existing.EntryAttribute4 = Entry.EntryAttribute4;
                    existing.EntryAttribute5 = Entry.EntryAttribute5;

                    existing.EntryCommPer = Entry.EntryCommPer;
                    existing.EntryCommAmt = Entry.EntryCommAmt;
                    existing.EntryTermsDays = Entry.EntryTermsDays;
                    existing.EntryCommSukri = Entry.EntryCommSukri;

                    existing.EntryRemarks = Entry.EntryRemarks;
                    existing.Amount = grandTotal;
                    existing.Qty = totalQty;
                    existing.UcpId = activeUcpId;
                    existing.LastUpdateOn = DateTime.Now;

                    var existingChildren = await _context.Entries
                        .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "PURL")
                        .ToListAsync();
                    _context.Entries.RemoveRange(existingChildren);

                    foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
                    {
                        var child = new Entry
                        {
                            EntryId = 0,
                            EntryType = "PURL",
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
                            UcpId = activeUcpId,
                            CreationOn = DateTime.Now
                        };
                        _context.Entries.Add(child);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["StatusMessage"] = isNew ? "Purchase entry created successfully!" : "Purchase entry updated successfully!";
                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Error saving entry: " + ex.Message;
                await LoadDropdowns();
                DisplayEntryNumber = Entry.EntryId > 0 ? $"PUR-{Entry.EntryNumber}" : "PUR-(Auto)";
                return Page();
            }
        }

        private async Task LoadDropdowns()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return;

            if (_context.SupplierMasters != null)
            {
                var suppliers = await _context.SupplierMasters
                     .Where(s => s.UcpId == activeUcpId && (s.SupplierDeactiveOn == null || s.SupplierDeactiveOn > DateTime.Today))
                    .Select(s => new { s.SupplierId, s.SupplierName })
                    .OrderBy(s => s.SupplierName)
                    .ToListAsync();
                SupplierList = new SelectList(suppliers, "SupplierId", "SupplierName");
            }
            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters
                     .Where(i => i.UcpId == activeUcpId && (i.ItemDeactiveOn == null || i.ItemDeactiveOn > DateTime.Today))
                    .OrderBy(i => i.ItemName)
                    .ToListAsync();
                ItemList = new SelectList(items, "ItemId", "ItemName");
            }

            if (_context.Entries != null)
            {
                // Fetch PSD Entries
                var psdRaw = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId)
                    .OrderByDescending(e => e.EntryDate)
                     .ThenByDescending(e => e.EntryNumber)
                    .Select(e => new { e.EntryId, e.EntryNumber, e.EntryDate, e.EntryAttribute1, e.Qty })
                    .ToListAsync();

                // Calculate Utilized and Remaining Qty for PSDs
                // 1. Get all Purchase Lines (PURL) that are linked to a PSD (EntryRefNo)
                var purchaseLines = await _context.Entries
                    .Where(e => e.EntryType == "PURL")
                    .Join(_context.Entries, child => child.EntryRefId, parent => parent.EntryId, (child, parent) => new { child, parent })
                    .Where(x => x.parent.EntryRefNo != null) // Only those linked to PSD
                    .Select(x => new
                    {
                        PsdEntryNumber = x.parent.EntryRefNo,
                        ItemId = x.child.ItemId,
                        Qty = x.child.Qty,
                        ParentEntryId = x.parent.EntryId // For exclusion
                    })
                    .ToListAsync();

                var consumedQtyMap = purchaseLines
                    .Where(x => Entry == null || x.ParentEntryId != Entry.EntryId) // EXCLUDE CURRENT DOCUMENT
                    .GroupBy(x => new { x.PsdEntryNumber, x.ItemId })
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty ?? 0));

                var psdLineItems = await _context.Entries
                    .Where(e => e.EntryType == "PSDL" && e.EntryRefId != null)
                    .Join(_context.Entries, child => child.EntryRefId, parent => parent.EntryId, (child, parent) => new { parent.EntryId, parent.EntryNumber, child })
                    .Join(_context.ItemMasters, psdl => psdl.child.ItemId, item => item.ItemId, (psdl, item) => new
                    {
                        psdl.EntryId,
                        psdl.EntryNumber,
                        psdl.child.ItemId,
                        ItemName = item.ItemName,
                        psdl.child.Qty,
                        psdl.child.Lbs,
                        psdl.child.Rate
                    })
                    .ToListAsync();

                var groupedItems = psdLineItems
                   .GroupBy(x => x.EntryId)
                   .ToDictionary(g => g.Key, g => g.Select(x =>
                   {
                       double consumed = 0;
                       if (x.EntryNumber.HasValue && x.ItemId.HasValue)
                       {
                           var key = new { PsdEntryNumber = x.EntryNumber, ItemId = x.ItemId };
                           if (consumedQtyMap.ContainsKey(key))
                           {
                               consumed = consumedQtyMap[key];
                           }
                       }

                       return new PsdLineItem
                       {
                           ItemId = x.ItemId ?? 0,
                           ItemName = x.ItemName,
                           Qty = x.Qty ?? 0,
                           ConsumedQty = consumed,
                           RemainingQty = (x.Qty ?? 0) - consumed,
                           Lbs = x.Lbs ?? 0,
                           Rate = x.Rate ?? 0
                       };
                   }).ToList());

                PsdEntriesForJs = psdRaw.Select(p =>
                {
                    List<PsdLineItem>? items = groupedItems.ContainsKey(p.EntryId) ? groupedItems[p.EntryId] : null;

                    string itemsStr = items != null && items.Any()
                        ? " - " + string.Join(", ", items.Select(i => i.ItemName).Distinct())
                        : "";

                    return new PsdEntryInfo
                    {
                        EntryNumber = p.EntryNumber ?? 0,
                        EntryId = p.EntryId,
                        DisplayText = "PSD-" + p.EntryNumber + " (" + (p.EntryDate.HasValue ? p.EntryDate.Value.ToString("yyyy-MM-dd") : "") + ")" + itemsStr + (items != null && items.Any() ? " | Rates: " + string.Join(", ", items.Select(i => i.Rate.ToString("0.0000")).Distinct()) : ""),
                        SupplierId = p.EntryAttribute1,
                        Items = items
                    };
                }).ToList();

                PurchaseSodaList = new SelectList(PsdEntriesForJs, "EntryId", "DisplayText");
            }
        }
    }
}
