using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

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

        [BindProperty]
        public List<LineItemViewModel> LineItems { get; set; } = new List<LineItemViewModel>();

        public SelectList? CustomerList { get; set; }
        public SelectList? ItemList { get; set; }
        public SelectList? PurchaseSodaList { get; set; }

        public string DisplayEntryNumber { get; set; } = "SSD-(Auto)";

        public class LineItemViewModel
        {
            public long EntryId { get; set; } // 0 for new items
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPer { get; set; } = 0;
            public double? GstAmt { get; set; }
            public double? NetAmount { get; set; }
        }

        public class PsdEntryInfo
        {
            public long EntryId { get; set; }
            public DateTime? EntryDate { get; set; }
            public double? OriginalQty { get; set; }
            public double RemainingQty { get; set; }
            public List<PsdItemInfo> Items { get; set; } = new List<PsdItemInfo>();
        }

        public class PsdItemInfo
        {
            public long ItemId { get; set; }
            public string ItemName { get; set; } = "";
            public double OriginalQty { get; set; }
            public double RemainingQty { get; set; }
            public double Rate { get; set; }
            public double Lbs { get; set; }
            public double GstPer { get; set; } = 0;
        }

        public List<PsdEntryInfo> PsdEntriesForJs { get; set; } = new List<PsdEntryInfo>();

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            if (id.HasValue && id > 0)
            {
                Entry = await _context.Entries.FindAsync(id);
                if (Entry == null) return NotFound();

                DisplayEntryNumber = $"SSD-{Entry.EntryNumber}";

                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "SSDL")
                    .OrderBy(e => e.EntryId)
                    .ToListAsync();

                if (childEntries.Any())
                {
                    LineItems = childEntries.Select(c =>
                    {
                        double gst = double.TryParse(c.EntryAttribute1, out double g1) ? g1 : (double.TryParse(c.EntryAttribute2, out double g2) ? g2 : 0);
                        double tax = c.TaxAmount ?? ((c.Amount ?? 0) * (gst / 100.0));
                        return new LineItemViewModel
                        {
                            EntryId = c.EntryId,
                            ItemId = c.ItemId,
                            Qty = c.Qty,
                            Lbs = c.Lbs,
                            Rate = c.Rate,
                            Amount = c.Amount,
                            GstPer = gst,
                            GstAmt = tax,
                            NetAmount = (c.Amount ?? 0) + tax
                        };
                    }).ToList();
                }
                else if (Entry.ItemId.HasValue)
                {
                    double gst = double.TryParse(Entry.EntryAttribute1, out double g1) ? g1 : (double.TryParse(Entry.EntryAttribute2, out double g2) ? g2 : 0);
                    double tax = Entry.TaxAmount ?? ((Entry.Amount ?? 0) * (gst / 100.0));
                    LineItems.Add(new LineItemViewModel
                    {
                        EntryId = 0,
                        ItemId = Entry.ItemId,
                        Qty = Entry.Qty,
                        Lbs = Entry.Lbs,
                        Rate = Entry.Rate,
                        Amount = Entry.Amount,
                        GstPer = gst,
                        GstAmt = tax,
                        NetAmount = (Entry.Amount ?? 0) + tax
                    });
                }
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryDate = DateTime.Today,
                    EntryType = "SSD",
                    EntryRefType = "PSD",
                    EntryAttribute2 = "Credit",
                    EntryAttribute5 = "No",
                    EntryCommPer = 10,
                    EntryCommSukri = 0
                };
                DisplayEntryNumber = "SSD-(Auto)";
                LineItems.Add(new LineItemViewModel { EntryId = 0, GstPer = 0 });
            }

            if (LineItems.Count == 0) LineItems.Add(new LineItemViewModel { EntryId = 0, GstPer = 0 });

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
            if (Entry != null && !Entry.EntryCommSukri.HasValue)
            {
                Entry.EntryCommSukri = 0;
            }

            await LoadDropdowns();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadDropdowns();
                return Page();
            }

            // Recalculate amounts and GST based on Lbs * Rate and GstPer
            double totalQty = 0;
            double totalAmount = 0;
            double totalGst = 0;
            double grandTotalNet = 0;

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
                if (item.Amount.HasValue && item.GstPer.HasValue)
                {
                    item.GstAmt = item.Amount.Value * (item.GstPer.Value / 100.0);
                }
                else
                {
                    item.GstAmt = 0;
                }
                item.NetAmount = (item.Amount ?? 0) + (item.GstAmt ?? 0);

                totalQty += item.Qty ?? 0;
                totalAmount += item.Amount ?? 0;
                totalGst += item.GstAmt ?? 0;
                grandTotalNet += item.NetAmount ?? 0;
            }

            // Commission Calculation (on amount without GST)
            if (Entry.EntryAttribute5 == "Yes")
            {
                double commPer = Entry.EntryCommPer ?? 10;
                Entry.EntryCommPer = commPer;
                Entry.EntryCommAmt = totalAmount * (commPer / 100.0);
                Entry.EntryCommSukri = Entry.EntryCommSukri ?? 0;
            }
            else
            {
                Entry.EntryAttribute5 = "No";
                Entry.EntryAttribute4 = null;
                Entry.EntryCommPer = null;
                Entry.EntryCommAmt = null;
                Entry.EntryCommSukri = Entry.EntryCommSukri ?? 0;
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
                Entry.UcpId = activeUcpId;
                Entry.EntryType = "SSD";
                Entry.EntryRefType = "PSD";
                Entry.CreationOn = DateTime.Now;
                Entry.Qty = totalQty;
                Entry.Amount = grandTotalNet; // Store grand total including GST
                Entry.TaxAmount = totalGst; // Store total GST
                Entry.EntryAttribute3 = grandTotalNet.ToString("F2");

                var lastEntry = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId)
                    .OrderByDescending(e => e.EntryNumber)
                    .FirstOrDefaultAsync();
                Entry.EntryNumber = (lastEntry?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
            }
            else
            {
                var existing = await _context.Entries.FindAsync(Entry.EntryId);
                if (existing == null) return NotFound();

                existing.EntryDate = Entry.EntryDate;
                existing.EntryAttribute1 = Entry.EntryAttribute1; // Customer
                existing.EntryAttribute2 = Entry.EntryAttribute2; // Payment Mode
                existing.EntryTermsDays = Entry.EntryTermsDays; // Payment Terms Days
                existing.EntryRefId = Entry.EntryRefId; // PSD Ref
                existing.EntryRemarks = Entry.EntryRemarks;
                existing.EntryAttribute4 = Entry.EntryAttribute4;
                existing.EntryAttribute5 = Entry.EntryAttribute5;
                existing.EntryCommPer = Entry.EntryCommPer;
                existing.EntryCommAmt = Entry.EntryCommAmt;
                existing.EntryCommSukri = Entry.EntryCommSukri;
                existing.Qty = totalQty;
                existing.Amount = grandTotalNet;
                existing.TaxAmount = totalGst;
                existing.EntryAttribute3 = grandTotalNet.ToString("F2");
                existing.UcpId = activeUcpId;
                existing.LastUpdateOn = DateTime.Now;

                // Sync Entry properties with view model if needed
                Entry = existing;
            }

            await _context.SaveChangesAsync();

            // Handle Line Items
            var existingLines = await _context.Entries
                .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "SSDL")
                .ToListAsync();

            // Delete removed lines
            var submittedIds = LineItems.Select(x => x.EntryId).ToHashSet();
            foreach (var line in existingLines)
            {
                if (!submittedIds.Contains(line.EntryId)) _context.Entries.Remove(line);
            }

            // Add or Update lines
            foreach (var item in LineItems.Where(x => x.ItemId.HasValue))
            {
                if (item.EntryId > 0)
                {
                    var existingLine = existingLines.FirstOrDefault(x => x.EntryId == item.EntryId);
                    if (existingLine != null)
                    {
                        existingLine.ItemId = item.ItemId;
                        existingLine.Qty = item.Qty;
                        existingLine.Lbs = item.Lbs;
                        existingLine.Rate = item.Rate;
                        existingLine.Amount = item.Amount;
                        existingLine.EntryAttribute1 = item.GstPer?.ToString(); // GST %
                        existingLine.TaxAmount = item.GstAmt; // GST Amount
                        existingLine.EntryAttribute3 = item.NetAmount?.ToString();
                        existingLine.UcpId = activeUcpId;
                        existingLine.LastUpdateOn = DateTime.Now;
                    }
                }
                else
                {
                    _context.Entries.Add(new Entry
                    {
                        EntryId = 0,
                        EntryType = "SSDL",
                        EntryRefId = Entry.EntryId,
                        EntryDate = Entry.EntryDate,
                        ItemId = item.ItemId,
                        Qty = item.Qty,
                        Lbs = item.Lbs,
                        Rate = item.Rate,
                        Amount = item.Amount,
                        EntryAttribute1 = item.GstPer?.ToString(), // GST %
                        TaxAmount = item.GstAmt, // GST Amount
                        EntryAttribute3 = item.NetAmount?.ToString(),
                        UcpId = activeUcpId,
                        CreationOn = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = Entry.EntryId == 0 ? "Sale Soda created successfully!" : "Sale Soda updated successfully!";
            return RedirectToPage("./IndexSoda");
        }

        private async Task LoadDropdowns()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return;

            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters
                   .Where(c => c.UcpId == activeUcpId && (c.CustomerDeactiveOn == null || c.CustomerDeactiveOn > DateTime.Today))
                   .OrderBy(c => c.CustomerName)
                   .Select(c => new { c.CustomerName, c.CustomerId })
                   .ToListAsync();
                CustomerList = new SelectList(customers, "CustomerId", "CustomerName");
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
                // Fetch PSD Headers
                var psdRaw = await _context.Entries
                    .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId)
                    .OrderByDescending(e => e.EntryDate)
                    .ToListAsync();

                // Fetch PSD Items (PSDL)
                var psdItemsRaw = await _context.Entries
                   .Where(e => e.EntryType == "PSDL" && e.EntryRefId != null && e.UcpId == activeUcpId)
                   .Join(_context.ItemMasters, e => e.ItemId, i => i.ItemId, (e, i) => new { e, i })
                   .Select(x => new
                   {
                       x.e.EntryRefId,
                       x.e.ItemId,
                       x.e.Qty,
                       x.e.Lbs,
                       x.e.Rate,
                       x.e.EntryAttribute2,
                       x.i.ItemName
                   })
                   .ToListAsync();

                // Fetch SSDL (Sold Items) to calculate remaining balance per item
                // SSDL -> EntryRefId = SSD_ID. SSD -> EntryRefId = PSD_ID.
                var ssdHeaders = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.EntryRefId != null && e.UcpId == activeUcpId)
                    .Select(e => new { e.EntryId, e.EntryRefId })
                    .ToListAsync();

                var ssdMap = ssdHeaders.ToDictionary(k => k.EntryId, v => v.EntryRefId); // SSD ID -> PSD ID

                var ssdlRaw = await _context.Entries
                    .Where(e => e.EntryType == "SSDL" && e.EntryRefId != null && e.UcpId == activeUcpId)
                    .ToListAsync();

                // Dictionary: (PsdId, ItemId) -> SoldQty
                var soldMap = new Dictionary<(long, long), double>();
                foreach (var line in ssdlRaw)
                {
                    if (line.EntryRefId.HasValue && ssdMap.TryGetValue(line.EntryRefId.Value, out long? psdId))
                    {
                        if (psdId.HasValue)
                        {
                            // Exclude current entry if needed? 
                            // In SodaModel, the SSD is the Entry. If we are editing SSD, we shouldn't count its own lines as sold.
                            if (Entry != null && Entry.EntryId == line.EntryRefId.Value) continue;

                            var key = (psdId.Value, line.ItemId ?? 0);
                            if (!soldMap.ContainsKey(key)) soldMap[key] = 0;
                            soldMap[key] += line.Qty ?? 0;
                        }
                    }
                }

                var groupedPsdItems = psdItemsRaw
                    .GroupBy(x => x.EntryRefId)
                    .ToDictionary(g => g.Key ?? 0, g => g.ToList());

                PsdEntriesForJs = psdRaw.Select(p =>
                {
                    var items = new List<PsdItemInfo>();
                    double totalRemaining = 0;

                    if (groupedPsdItems.TryGetValue(p.EntryId, out var pItems))
                    {
                        foreach (var pi in pItems)
                        {
                            var sold = soldMap.TryGetValue((p.EntryId, pi.ItemId ?? 0), out double s) ? s : 0;
                            var rem = (pi.Qty ?? 0) - sold;
                            if (rem < 0) rem = 0;

                            items.Add(new PsdItemInfo
                            {
                                ItemId = pi.ItemId ?? 0,
                                ItemName = pi.ItemName,
                                OriginalQty = pi.Qty ?? 0,
                                RemainingQty = rem,
                                Rate = pi.Rate ?? 0,
                                Lbs = pi.Lbs ?? 0,
                                GstPer = double.TryParse(pi.EntryAttribute2, out double g) ? g : 0
                            });
                            totalRemaining += rem;
                        }
                    }

                    return new PsdEntryInfo
                    {
                        EntryId = p.EntryId,
                        EntryDate = p.EntryDate,
                        OriginalQty = p.Qty,
                        RemainingQty = totalRemaining,
                        Items = items
                    };
                }).ToList();

                var selectListItems = PsdEntriesForJs.Select(p => new
                {
                    p.EntryId,
                    DisplayText = "PSD-" + psdRaw.FirstOrDefault(x => x.EntryId == p.EntryId)?.EntryNumber + " (" + (p.EntryDate.HasValue ? p.EntryDate.Value.ToString("yyyy-MM-dd") : "") + ")" +
                                  (p.Items.Any() ? " - " + string.Join(", ", p.Items.Select(i => i.ItemName).Distinct()) : "")
                }).ToList();

                PurchaseSodaList = new SelectList(selectListItems, "EntryId", "DisplayText");
            }
        }
    }
}
