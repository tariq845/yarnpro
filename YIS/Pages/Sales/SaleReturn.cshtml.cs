using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Sales
{
    public class SaleReturnModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SaleReturnModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = default!;

        [BindProperty]
        public List<LineItemViewModel> LineItems { get; set; } = new List<LineItemViewModel>();

        public SelectList CustomerList { get; set; } = default!;
        public SelectList ItemList { get; set; } = default!;
        public SelectList SaleInvoiceList { get; set; } = default!;
        
        public string DisplayEntryNumber { get; set; } = "CN-(Auto)";
        public bool IsPosted { get; set; }

        public class LineItemViewModel
        {
            public long EntryId { get; set; } 
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPer { get; set; }
            public double? GstAmt { get; set; }
            public double? NetAmount { get; set; }
        }

        public List<InvoicedEntryInfo>? SaleInvoicesForJs { get; set; }
        
        public class InvoicedEntryInfo
        {
            public long EntryId { get; set; }
            public long EntryNumber { get; set; }
            public string? DisplayText { get; set; }
            public string? CustomerId { get; set; }
            public List<InvoicedItemInfo>? Items { get; set; }
        }

        public class InvoicedItemInfo
        {
            public long ItemId { get; set; }
            public string? ItemName { get; set; }
            public double OriginalQty { get; set; }
            public double RemainingQty { get; set; }
            public double Rate { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            await LoadDropdowns();

            if (id.HasValue && id > 0)
            {
                var entry = await _context.Entries.FindAsync(id);
                if (entry == null || entry.EntryType != "CN")
                {
                    return NotFound();
                }
                Entry = entry;
                DisplayEntryNumber = $"CN-{entry.EntryNumber}";

                // Check if posted
                IsPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "CNL")
                    .OrderBy(e => e.EntryId)
                    .ToListAsync();

                LineItems = childEntries.Select(c => {
                    double.TryParse(c.EntryAttribute1, out double gstPer);
                    return new LineItemViewModel
                    {
                        EntryId = c.EntryId,
                        ItemId = c.ItemId,
                        Qty = c.Qty,
                        Lbs = c.Lbs,
                        Rate = c.Rate,
                        Amount = c.Amount,
                        GstPer = gstPer,
                        GstAmt = c.TaxAmount,
                        NetAmount = (c.Amount ?? 0) + (c.TaxAmount ?? 0)
                    };
                }).ToList();
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryDate = DateTime.Today,
                    EntryType = "CN",
                    EntryCommSukri = 0
                };
                DisplayEntryNumber = "CN-(Auto)";
                LineItems.Add(new LineItemViewModel { EntryId = 0 });
            }

            if (LineItems.Count == 0) LineItems.Add(new LineItemViewModel { EntryId = 0 });

            if (Entry != null && !Entry.EntryCommSukri.HasValue)
            {
                Entry.EntryCommSukri = 0;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Check if posted
            if (Entry.EntryId > 0 && await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId))
            {
                TempData["ErrorMessage"] = "Cannot update: This entry has already been posted to the ledger.";
                return RedirectToPage("./SaleReturnIndex");
            }
            ModelState.Remove("Entry.ItemId");
            ModelState.Remove("Entry.Qty");
            ModelState.Remove("Entry.Rate");
            
            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                DisplayEntryNumber = Entry.EntryId > 0 ? $"CN-{Entry.EntryNumber}" : "CN-(Auto)";
                return Page();
            }

            double grandTotalQty = 0;
            double grandTotalAmount = 0; // Item Values
            double totalTax = 0;
            double finalNetTotal = 0;

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

                // Default GST to 0% if not provided
                if (!item.GstPer.HasValue)
                {
                    item.GstPer = 0;
                }

                if (item.Qty.HasValue && item.Rate.HasValue)
                {
                    item.Amount = (item.Qty.Value * item.Rate.Value);
                    
                    double tax = 0;
                    if (item.GstPer.HasValue && item.GstPer > 0)
                    {
                        tax = (item.Amount.Value * item.GstPer.Value) / 100;
                    }
                    item.GstAmt = tax;
                    item.NetAmount = item.Amount.Value + tax;

                    grandTotalQty += item.Qty.Value;
                    grandTotalAmount += item.Amount.Value;
                    totalTax += tax;
                    finalNetTotal += item.NetAmount.Value;
                }
            }

            // Entry Net Amount is after GST
            // Commission, Sukri etc are stored but maybe not deducted from primary 'Amount' field 
            // depending on how user treats them.
            // Following 'Sale Entry' logic: Entry.Amount = grandTotalAmount (Inc Tax)
            
            Entry.EntryType = "CN";
            Entry.Amount = finalNetTotal; 
            Entry.Qty = grandTotalQty;
            Entry.TaxAmount = totalTax;
            Entry.EntryAttribute3 = finalNetTotal.ToString(); // NET AMOUNT
            Entry.EntryCommSukri = Entry.EntryCommSukri ?? 0;

            if (Entry.EntryId == 0)
            {
                Entry.CreationOn = DateTime.Now;
     
                var last = await _context.Entries
                    .Where(e => e.EntryType == "CN")
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
                        EntryType = "CNL",
                        EntryDate = Entry.EntryDate,
                        EntryRefId = Entry.EntryId,
                        ItemId = l.ItemId,
                        Qty = l.Qty,
                        Lbs = l.Lbs,
                        Rate = l.Rate,
                        Amount = l.Amount,
                        TaxAmount = l.GstAmt,
                        EntryAttribute1 = l.GstPer?.ToString(), // Store GST % in Attr1
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
                 existing.EntryAttribute1 = Entry.EntryAttribute1; // Customer
                 existing.EntryRefNo = Entry.EntryRefNo; // Against Sale Invoice (EntryNumber)
                 existing.EntryRefId = Entry.EntryRefId; // Against Sale Invoice (EntryId)
                 
                 existing.EntryCommPer = Entry.EntryCommPer;
                 existing.EntryCommAmt = Entry.EntryCommAmt;
                 existing.EntryTermsDays = Entry.EntryTermsDays;
                 existing.EntryCommSukri = Entry.EntryCommSukri;
                 existing.EntryRemarks = Entry.EntryRemarks;
                 
                 existing.Amount = finalNetTotal;
                 existing.Qty = grandTotalQty;
                 existing.TaxAmount = totalTax;
                 existing.EntryAttribute3 = finalNetTotal.ToString(); 
                 existing.LastUpdateOn = DateTime.Now;
                 
                 var existingChildren = await _context.Entries
                    .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "CNL")
                    .ToListAsync();
                 _context.Entries.RemoveRange(existingChildren);
                 
                 foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
                {
                    var child = new Entry
                    {
                        EntryId = 0,
                        EntryType = "CNL",
                        EntryDate = Entry.EntryDate,
                        EntryRefId = Entry.EntryId,
                        ItemId = l.ItemId,
                        Qty = l.Qty,
                        Lbs = l.Lbs,
                        Rate = l.Rate,
                        Amount = l.Amount,
                        TaxAmount = l.GstAmt,
                        EntryAttribute1 = l.GstPer?.ToString(),
                        CreationOn = DateTime.Now
                    };
                    _context.Entries.Add(child);
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = Entry.EntryId == 0 ? "Sale Return created successfully!" : "Sale Return updated successfully!";
            return RedirectToPage("./SaleReturnIndex");
        }

        private async Task LoadDropdowns()
        {
            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters
                     .Where(c => c.CustomerDeactiveOn == null || c.CustomerDeactiveOn > DateTime.Today)
                    .Select(c => new { c.CustomerId, c.CustomerName })
                    .OrderBy(c => c.CustomerName)
                    .ToListAsync();
                CustomerList = new SelectList(customers, "CustomerId", "CustomerName");
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
                // Fetch SAL (Sale Invoices) for dropdown and filtering
                var salRaw = await _context.Entries
                    .Where(e => e.EntryType == "SAL")
                    .OrderByDescending(e => e.EntryDate)
                     .ThenByDescending(e => e.EntryNumber)
                    .Select(e => new { e.EntryId, e.EntryNumber, e.EntryDate, e.EntryAttribute1 })
                    .ToListAsync();

                // Fetch SAL Line Items (SALL)
                var salLineItems = await _context.Entries
                    .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
                    .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { 
                        l.EntryRefId, l.ItemId, l.Qty, l.Rate, i.ItemName 
                    }).ToListAsync();

                // Fetch existing returns (CNL) to calculate remaining balance
                // CNL -> CN (Header) -> EntryRefId = SAL (Header ID)
                var cnHeaders = await _context.Entries
                    .Where(e => e.EntryType == "CN" && e.EntryRefId != null)
                    .Select(e => new { e.EntryId, e.EntryRefId })
                    .ToListAsync();
                
                var cnMap = cnHeaders.ToDictionary(k => k.EntryId, v => v.EntryRefId); // CN_ID -> SAL_ID

                var cnLines = await _context.Entries
                    .Where(e => e.EntryType == "CNL" && e.EntryRefId != null)
                    .ToListAsync();

                // Previously Returned: (SalId, ItemId) -> Qty
                var returnedMap = new Dictionary<(long, long), double>();
                foreach(var line in cnLines)
                {
                    if (line.EntryRefId.HasValue && cnMap.TryGetValue(line.EntryRefId.Value, out long? salId))
                    {
                        if (salId.HasValue)
                        {
                            // Exclude current return if editing
                            if (Entry != null && Entry.EntryId == line.EntryRefId.Value) continue;

                            var key = (salId.Value, line.ItemId ?? 0);
                            if (!returnedMap.ContainsKey(key)) returnedMap[key] = 0;
                            returnedMap[key] += line.Qty ?? 0;
                        }
                    }
                }

                var groupedSalItems = salLineItems
                    .GroupBy(x => x.EntryRefId)
                    .ToDictionary(g => g.Key ?? 0, g => g.ToList());

                SaleInvoicesForJs = salRaw.Select(s => {
                    var items = new List<InvoicedItemInfo>();
                    if (groupedSalItems.TryGetValue(s.EntryId, out var siList))
                    {
                        foreach(var si in siList)
                        {
                            var returned = returnedMap.TryGetValue((s.EntryId, si.ItemId ?? 0), out double r) ? r : 0;
                            var rem = (si.Qty ?? 0) - returned;
                            if (rem < 0) rem = 0;

                            items.Add(new InvoicedItemInfo {
                                ItemId = si.ItemId ?? 0,
                                ItemName = si.ItemName,
                                OriginalQty = si.Qty ?? 0,
                                RemainingQty = rem,
                                Rate = si.Rate ?? 0
                            });
                        }
                    }

                    string itemsStr = items.Any() ? " - " + string.Join(", ", items.Select(i => i.ItemName).Distinct()) : "";

                    return new InvoicedEntryInfo
                    {
                        EntryId = s.EntryId,
                        EntryNumber = s.EntryNumber ?? 0,
                        CustomerId = s.EntryAttribute1,
                        DisplayText = "SAL-" + s.EntryNumber + " (" + (s.EntryDate.HasValue ? s.EntryDate.Value.ToString("yyyy-MM-dd") : "") + ")" + itemsStr,
                        Items = items
                    };
                }).ToList();

                SaleInvoiceList = new SelectList(SaleInvoicesForJs, "EntryId", "DisplayText");
            }
        }
    }
}
