//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.RazorPages;
//using Microsoft.AspNetCore.Mvc.Rendering;
//using Microsoft.EntityFrameworkCore;
//using YIS.Data;
//using YIS.Models;
//using YIS.Helpers;

//namespace YIS.Pages.Sales
//{
//    public class EntryModel : PageModel
//    {
//        private readonly ApplicationDbContext _context;

//        public EntryModel(ApplicationDbContext context)
//        {
//            _context = context;
//        }

//        [BindProperty]
//        public Entry Entry { get; set; } = new Entry { EntryId = 0 };

//        [BindProperty]
//        public List<InvoiceLineItemViewModel> LineItems { get; set; } = new List<InvoiceLineItemViewModel>();

//        public SelectList? CustomerList { get; set; }
//        public SelectList? SalesSodaList { get; set; }
//        public SelectList? ItemList { get; set; }
//        public SelectList? PurchaseEntryList { get; set; }

//        public string DisplayEntryNumber { get; set; } = "New";
//        public bool IsLedgerPosted { get; set; }

//        public class InvoiceLineItemViewModel
//        {
//            public long EntryId { get; set; } // 0 for new
//            public long? ItemId { get; set; }
//            public double? Qty { get; set; }
//            public double? Lbs { get; set; }
//            public double? Rate { get; set; }
//            public double? Amount { get; set; }
//            public double? GstPer { get; set; } // ENTRY_ATTRIBUTE1
//            public double? GstAmt { get; set; } // TAX_AMOUNT
//            public double? TotalAmount { get; set; } // Amount + Tax
//        }

//        // For JS Data
//        public List<SsdEntryInfo>? SsdEntriesForJs { get; set; }
//        public class SsdEntryInfo
//        {
//            public long EntryId { get; set; }
//            public DateTime? EntryDate { get; set; }
//            public long? CustomerId { get; set; }
//            public double RemainingQty { get; set; }
//            public long? PurNumber { get; set; }
//            public List<SsdItemInfo> Items { get; set; } = new List<SsdItemInfo>();
//        }

//        public class SsdItemInfo
//        {
//            public long ItemId { get; set; }
//            public string ItemName { get; set; }
//            public double OriginalQty { get; set; }
//            public double RemainingQty { get; set; }
//            public double Rate { get; set; }
//            public double Lbs { get; set; }
//        }

//        // PUR JS Data
//        public List<PurEntryInfo>? PurEntriesForJs { get; set; }
//        public class PurEntryInfo
//        {
//            public long EntryNumber { get; set; }
//            public string Date { get; set; } = "";
//            public double TotalQty { get; set; }
//            public double TotalRemainingQty { get; set; }
//            public List<PurItemInfo> Items { get; set; } = new List<PurItemInfo>();
//        }
//        public class PurItemInfo
//        {
//            public long ItemId { get; set; }
//            public string ItemName { get; set; }
//            public double Qty { get; set; }
//            public double SoldQty { get; set; }
//            public double RemainingQty { get; set; }
//            public double Lbs { get; set; }
//            public double Rate { get; set; }
//        }

//        public async Task<IActionResult> OnGetAsync(long? id, long? ssdId)
//        {
//            var path = HttpContext.Request.Path.Value ?? "";
//            if (!AuthHelper.CanRead(HttpContext, path))
//            {
//                TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Sales entries.";
//                return RedirectToPage("/Index");
//            }

//            if (id.HasValue && id > 0)
//            {
//                Entry = await _context.Entries.FindAsync(id);
//                if (Entry == null) return NotFound();

//                if (Entry.EntryNumber.HasValue)
//                {
//                    DisplayEntryNumber = "SAL-" + Entry.EntryNumber;
//                }

//                // Check if posted
//                IsLedgerPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

//                // Load Children (SALL)
//                var childEntries = await _context.Entries
//                    .Where(e => e.EntryRefId == id && e.EntryType == "SALL")
//                    .ToListAsync();

//                LineItems = childEntries.Select(c =>
//                {
//                    double.TryParse(c.EntryAttribute1, out double gstPer);
//                    return new InvoiceLineItemViewModel
//                    {
//                        EntryId = c.EntryId,
//                        ItemId = c.ItemId,
//                        Qty = c.Qty,
//                        Lbs = c.Lbs,
//                        Rate = c.Rate,
//                        Amount = c.Amount,
//                        GstPer = gstPer,
//                        GstAmt = c.TaxAmount,
//                        TotalAmount = (c.Amount ?? 0) + (c.TaxAmount ?? 0)
//                    };
//                }).ToList();
//            }
//            else
//            {
//                Entry = new Entry
//                {
//                    EntryId = 0,
//                    EntryType = "SAL",
//                    EntryDate = DateTime.Today,
//                    EntryRefId = ssdId
//                };
//                LineItems.Add(new InvoiceLineItemViewModel());
//            }

//            await LoadDropdowns();
//            return Page();
//        }

//        private async Task LoadDropdowns()
//        {
//            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
//            if (!activeUcpId.HasValue)
//            {
//                TempData["ErrorMessage"] = "Please select a company first.";
//                return; // Or handle as needed
//            }

//            if (_context.CustomerMasters != null)
//            {
//                var customers = await _context.CustomerMasters
//                    .Where(c => c.UcpId == activeUcpId)
//                    .OrderBy(c => c.CustomerName).ToListAsync();
//                CustomerList = new SelectList(customers, "CustomerId", "CustomerName");
//            }

//            if (_context.ItemMasters != null)
//            {
//                var items = await _context.ItemMasters
//                    .Where(i => i.UcpId == activeUcpId)
//                    .OrderBy(i => i.ItemName).ToListAsync();
//                ItemList = new SelectList(items, "ItemId", "ItemName");
//            }

//            if (_context.Entries != null && _context.ItemMasters != null)
//            {
//                // 1. Fetch PUR & PURL data
//                var purHeaders = await _context.Entries
//                    .Where(e => e.EntryType == "PUR" && e.UcpId == activeUcpId)
//                    .ToListAsync();

//                var purLines = await _context.Entries
//                    .Where(l => l.EntryType == "PURL" && l.EntryRefId != null)
//                    .Join(_context.Entries.Where(h => h.EntryType == "PUR"),
//                          l => l.EntryRefId,
//                          h => h.EntryId,
//                          (l, h) => new { l.ItemId, l.Qty, l.Lbs, l.Rate, h.EntryId, h.EntryNumber })
//                    .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { l.EntryId, l.EntryNumber, l.ItemId, l.Qty, l.Lbs, l.Rate, i.ItemName })
//                    .ToListAsync();

//                // Calculate SALL units already sold against these PUR numbers
//                var soldHeaders = await _context.Entries
//                    .Where(e => e.EntryType == "SAL" && e.EntryRefNo != null)
//                    .Join(_context.Entries.Where(h => h.EntryType == "PUR"),
//                          s => s.EntryRefNo,
//                          p => p.EntryNumber,
//                          (s, p) => new { s.EntryId, s.EntryRefNo })
//                    .ToListAsync();

//                var soldLines = await _context.Entries
//                    .Where(l => l.EntryType == "SALL" && l.EntryRefId != null)
//                    .Join(_context.Entries.Where(e => e.EntryType == "SAL"),
//                          l => l.EntryRefId,
//                          e => e.EntryId,
//                          (l, e) => l)
//                    .ToListAsync();

//                // Group sold quantities by PUR_NUMBER and ITEM_ID
//                var purSoldMap = new Dictionary<(long, long), double>();
//                foreach (var sl in soldLines)
//                {
//                    var parentSal = soldHeaders.FirstOrDefault(h => h.EntryId == sl.EntryRefId);
//                    if (parentSal != null && parentSal.EntryRefNo.HasValue)
//                    {
//                        // EXCLUDE CURRENT DOCUMENT from consumption
//                        if (Entry != null && Entry.EntryId == sl.EntryRefId) continue;

//                        var key = (parentSal.EntryRefNo.Value, sl.ItemId ?? 0);
//                        if (!purSoldMap.ContainsKey(key)) purSoldMap[key] = 0;
//                        purSoldMap[key] += sl.Qty ?? 0;
//                    }
//                }

//                PurEntriesForJs = purHeaders.Select(h =>
//                {
//                    var items = purLines.Where(l => l.EntryId == h.EntryId).Select(l =>
//                    {
//                        var sold = purSoldMap.ContainsKey((h.EntryNumber ?? 0, l.ItemId ?? 0)) ? purSoldMap[(h.EntryNumber ?? 0, l.ItemId ?? 0)] : 0;
//                        var rem = (l.Qty ?? 0) - sold;
//                        return new PurItemInfo
//                        {
//                            ItemId = l.ItemId ?? 0,
//                            ItemName = l.ItemName,
//                            Qty = l.Qty ?? 0,
//                            SoldQty = sold,
//                            RemainingQty = rem > 0 ? rem : 0,
//                            Lbs = l.Lbs ?? 0,
//                            Rate = l.Rate ?? 0
//                        };
//                    }).ToList();

//                    return new PurEntryInfo
//                    {
//                        EntryNumber = h.EntryNumber ?? 0,
//                        Date = h.EntryDate.HasValue ? h.EntryDate.Value.ToString("yyyy-MM-dd") : "",
//                        TotalQty = h.Qty ?? 0,
//                        TotalRemainingQty = items.Sum(i => i.RemainingQty),
//                        Items = items
//                    };
//                }).ToList();

//                var purEntriesDropdown = PurEntriesForJs
//                    .OrderByDescending(e => e.Date)
//                    .Select(e => new
//                    {
//                        e.EntryNumber,
//                        DisplayText = "PUR-" + e.EntryNumber + 
//                                     (e.Items.Any() ? " (Rates: " + string.Join(", ", e.Items.Select(i => i.Rate.ToString("F4")).Distinct()) + ")" : "") +
//                                     (e.Items.Any() ? " - " + string.Join(", ", e.Items.Select(i => i.ItemName).Distinct()) : "")
//                    })
//                    .ToList();
//                PurchaseEntryList = new SelectList(purEntriesDropdown, "EntryNumber", "DisplayText");

//                // 2. Fetch SSD Headers (and join with parent PSD to get PUR Number)
//                var ssdHeaders = await _context.Entries
//                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId)
//                    .OrderByDescending(e => e.EntryDate)
//                    .Join(_context.Entries.Where(p => p.EntryType == "PSD"),
//                          ssd => ssd.EntryRefId,
//                          psd => psd.EntryId,
//                          (ssd, psd) => new
//                          {
//                              ssd.EntryId,
//                              ssd.EntryNumber,
//                              ssd.EntryDate,
//                              ssd.EntryAttribute1, // CustomerID
//                              ssd.Qty,
//                              PurNumber = psd.EntryRefNo
//                          })
//                    .ToListAsync();

//                // 3. Fetch SSDL Lines (Sale Soda Items)
//                var ssdItemsRaw = await _context.Entries
//                   .Where(e => e.EntryType == "SSDL" && e.EntryRefId != null)
//                   .Join(_context.Entries, child => child.EntryRefId, parent => parent.EntryId, (child, parent) => new { parent.EntryId, child })
//                   .Join(_context.ItemMasters, p => p.child.ItemId, i => i.ItemId, (p, i) => new
//                   {
//                       p.EntryId,
//                       p.child.ItemId,
//                       p.child.Qty,
//                       p.child.Lbs,
//                       p.child.Rate,
//                       i.ItemName
//                   })
//                   .ToListAsync();

//                var groupedSsdItems = ssdItemsRaw
//                   .GroupBy(x => x.EntryId)
//                   .ToDictionary(g => g.Key, g => g.ToList());

//                // 4. Fetch SALL (Sale Invoice Lines) - These reduce the SSD Balance
//                var salHeaders = await _context.Entries
//                    .Where(e => e.EntryType == "SAL" && e.EntryRefId != null) // RefId is SSD ID
//                    .Select(e => new { e.EntryId, e.EntryRefId })
//                    .ToListAsync();

//                var salMap = salHeaders.ToDictionary(k => k.EntryId, v => v.EntryRefId); // SAL ID -> SSD ID

//                var salLines = await _context.Entries
//                    .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
//                    .ToListAsync();

//                // Calculate Sold Qty per (SSD_ID, Item_ID)
//                var soldMap = new Dictionary<(long, long), double>();

//                foreach (var line in salLines)
//                {
//                    if (line.EntryRefId.HasValue && salMap.ContainsKey(line.EntryRefId.Value))
//                    {
//                        var ssdId = salMap[line.EntryRefId.Value]; // The Parent SSD
//                        var itemId = line.ItemId ?? 0;

//                        // Exclude current entry being edited from "Sold" count
//                        if (Entry != null && Entry.EntryId == line.EntryRefId.Value) continue;

//                        if (ssdId.HasValue)
//                        {
//                            var key = (ssdId.Value, itemId);
//                            if (!soldMap.ContainsKey(key)) soldMap[key] = 0;
//                            soldMap[key] += line.Qty ?? 0;
//                        }
//                    }
//                }

//                SsdEntriesForJs = ssdHeaders.Select(ssd =>
//                {
//                    var items = new List<SsdItemInfo>();
//                    double totalRemaining = 0;

//                    if (groupedSsdItems.ContainsKey(ssd.EntryId))
//                    {
//                        foreach (var si in groupedSsdItems[ssd.EntryId])
//                        {
//                            var sold = soldMap.ContainsKey((ssd.EntryId, si.ItemId ?? 0)) ? soldMap[(ssd.EntryId, si.ItemId ?? 0)] : 0;
//                            var original = si.Qty ?? 0;
//                            var rem = original - sold;
//                            if (rem < 0) rem = 0;

//                            items.Add(new SsdItemInfo
//                            {
//                                ItemId = si.ItemId ?? 0,
//                                ItemName = si.ItemName,
//                                OriginalQty = original,
//                                RemainingQty = rem,
//                                Rate = si.Rate ?? 0,
//                                Lbs = si.Lbs ?? 0
//                            });
//                            totalRemaining += rem;
//                        }
//                    }
//                    else
//                    {
//                        if (ssd.Qty > 0) totalRemaining = ssd.Qty.Value;
//                    }

//                    long.TryParse(ssd.EntryAttribute1, out long custId);

//                    return new SsdEntryInfo
//                    {
//                        EntryId = ssd.EntryId,
//                        EntryDate = ssd.EntryDate,
//                        CustomerId = custId,
//                        RemainingQty = totalRemaining,
//                        PurNumber = ssd.PurNumber,
//                        Items = items
//                    };
//                }).ToList();

//                var dropdownList = SsdEntriesForJs.Select(e => new
//                {
//                    e.EntryId,
//                    DisplayText = $"SSD-{ssdHeaders.FirstOrDefault(x => x.EntryId == e.EntryId)?.EntryNumber} " +
//                                  (e.Items.Any() ? "(Rates: " + string.Join(", ", e.Items.Select(i => i.Rate.ToString("F4")).Distinct()) + ")" : "") +
//                                  (e.Items.Any() ? " - " + string.Join(", ", e.Items.Select(i => i.ItemName).Distinct()) : "")
//                }).ToList();

//                SalesSodaList = new SelectList(dropdownList, "EntryId", "DisplayText");
//            }
//        }

//        public async Task<IActionResult> OnPostAsync()
//        {
//            var path = HttpContext.Request.Path.Value ?? "";
//            bool isNew = Entry.EntryId == 0;

//            if (isNew && !AuthHelper.CanCreate(HttpContext, path))
//            {
//                TempData["ErrorMessage"] = "Access Denied: You do not have permission to create Sales entries.";
//                return RedirectToPage("./Index");
//            }

//            if (!isNew && !AuthHelper.CanUpdate(HttpContext, path))
//            {
//                TempData["ErrorMessage"] = "Access Denied: You do not have permission to update Sales entries.";
//                return RedirectToPage("./Index");
//            }

//            // Check if posted
//            if (Entry.EntryId > 0 && await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId))
//            {
//                TempData["ErrorMessage"] = "Cannot update: This sales entry has already been posted to the ledger.";
//                return RedirectToPage("./Index");
//            }

//            if (!Entry.EntryRefNo.HasValue)
//            {
//                ModelState.AddModelError("Entry.EntryRefNo", "The Against Purchase (PUR) field is required.");
//            }
//            else
//            {
//                // Re-calculate stock for this specific PUR to prevent over-selling
//                var purHeader = await _context.Entries.FirstOrDefaultAsync(e => e.EntryType == "PUR" && e.EntryNumber == Entry.EntryRefNo);
//                if (purHeader != null)
//                {
//                    var purLines = await _context.Entries
//                        .Where(e => e.EntryType == "PURL" && e.EntryRefId == purHeader.EntryId)
//                        .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { l.ItemId, l.Qty, i.ItemName })
//                        .ToListAsync();

//                    var soldAgainstPur = await _context.Entries
//                        .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
//                        .Join(_context.Entries.Where(h => h.EntryType == "SAL" && h.EntryRefNo == Entry.EntryRefNo && h.EntryId != Entry.EntryId),
//                              l => l.EntryRefId,
//                              h => h.EntryId,
//                              (l, h) => new { l.ItemId, l.Qty })
//                        .GroupBy(e => e.ItemId)
//                        .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Qty ?? 0) })
//                        .ToDictionaryAsync(x => x.ItemId ?? 0, x => x.Qty);

//                    foreach (var line in LineItems)
//                    {
//                        var itemId = line.ItemId ?? 0;
//                        var purItem = purLines.FirstOrDefault(i => i.ItemId == itemId);
//                        if (purItem != null)
//                        {
//                            var purchased = purItem.Qty ?? 0;
//                            var alreadySold = soldAgainstPur.ContainsKey(itemId) ? soldAgainstPur[itemId] : 0;
//                            var remaining = purchased - alreadySold;

//                            if ((line.Qty ?? 0) > remaining)
//                            {
//                                ModelState.AddModelError("", $"Quantity for '{purItem.ItemName}' exceeds available Purchase balance ({remaining}).");
//                            }
//                        }
//                    }
//                }
//            }

//            // SSD Check
//            if (Entry.EntryRefId.HasValue)
//            {
//                var ssdHeader = await _context.Entries.FirstOrDefaultAsync(e => e.EntryId == Entry.EntryRefId && e.EntryType == "SSD");
//                if (ssdHeader != null)
//                {
//                    var ssdLines = await _context.Entries
//                        .Where(e => e.EntryType == "SSDL" && e.EntryRefId == ssdHeader.EntryId)
//                        .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { l.ItemId, l.Qty, i.ItemName })
//                        .ToListAsync();

//                    var soldAgainstSsd = await _context.Entries
//                        .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
//                        .Join(_context.Entries.Where(h => h.EntryType == "SAL" && h.EntryRefId == Entry.EntryRefId && h.EntryId != Entry.EntryId),
//                              l => l.EntryRefId,
//                              h => h.EntryId,
//                              (l, h) => new { l.ItemId, l.Qty })
//                        .GroupBy(e => e.ItemId)
//                        .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Qty ?? 0) })
//                        .ToDictionaryAsync(x => x.ItemId ?? 0, x => x.Qty);

//                    foreach (var line in LineItems)
//                    {
//                        var itemId = line.ItemId ?? 0;
//                        var ssdItem = ssdLines.FirstOrDefault(i => i.ItemId == itemId);
//                        if (ssdItem != null)
//                        {
//                            var budgeted = ssdItem.Qty ?? 0;
//                            var alreadySold = soldAgainstSsd.ContainsKey(itemId) ? soldAgainstSsd[itemId] : 0;
//                            var remaining = budgeted - alreadySold;

//                            if ((line.Qty ?? 0) > remaining)
//                            {
//                                ModelState.AddModelError("", $"Quantity for '{ssdItem.ItemName}' exceeds available SSD balance ({remaining}).");
//                            }
//                        }
//                    }
//                }
//            }

//            if (!ModelState.IsValid)
//            {
//                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
//                TempData["ErrorMessage"] = string.Join(" | ", errors);
//                await LoadDropdowns();
//                return Page();
//            }

//            // Get Active Company Profile from Session
//            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
//            if (!activeUcpId.HasValue)
//            {
//                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
//                return RedirectToPage("/SelectCompany");
//            }

//            // Calculation Logic
//            double grandTotalQty = 0;
//            double grandTotalAmount = 0; // Item Amounts + Tax
//            double totalTax = 0;
//            double totalItemValue = 0;

//            foreach (var item in LineItems)
//            {
//                // Auto-calculate LBs = Qty * 100
//                if (item.Qty.HasValue)
//                {
//                    item.Lbs = item.Qty.Value * 100;
//                }

//                // Enforce Rate to 4 decimal places
//                if (item.Rate.HasValue)
//                {
//                    item.Rate = Math.Round(item.Rate.Value, 4);
//                }

//                // Default GST to 18% if not provided
//                if (!item.GstPer.HasValue || item.GstPer == 0)
//                {
//                    item.GstPer = 18;
//                }

//                if (item.Rate.HasValue)
//                {
//                    double amt = 0;
//                    if (item.Lbs.HasValue)
//                    {
//                        amt = item.Lbs.Value * item.Rate.Value;
//                    }
//                    else if (item.Qty.HasValue)
//                    {
//                        // Fallback if Lbs is missing? Or should we enforce Lbs? 
//                        // Given request "multiply on lbs * rate", assuming Lbs is primary.
//                        // If Lbs is 0 or null, amount might be 0.
//                        amt = (item.Lbs ?? 0) * item.Rate.Value;
//                    }

//                    item.Amount = amt;

//                    // Tax
//                    double tax = 0;
//                    if (item.GstPer.HasValue && item.GstPer > 0)
//                    {
//                        tax = (item.Amount.Value * item.GstPer.Value) / 100;
//                    }
//                    item.GstAmt = tax;
//                    item.TotalAmount = item.Amount.Value + tax;

//                    grandTotalQty += item.Qty.Value;
//                    grandTotalAmount += item.TotalAmount.Value;
//                    totalTax += tax;
//                    totalItemValue += item.Amount.Value;
//                }
//            }

//            // Commission
//            // Comm Amt = (ItemValue * CommPer) / 100 ? Or on TotalAmount? Usually on ItemValue (Pre-tax)
//            double commAmt = 0;
//            if (Entry.EntryCommPer.HasValue && Entry.EntryCommPer > 0)
//            {
//                commAmt = (totalItemValue * Entry.EntryCommPer.Value) / 100;
//            }
//            Entry.EntryCommAmt = commAmt;

//            // Sukri is usually deduction or addition? Assuming deduction from final? Or just info?
//            // "Net Amount" (EntryAttribute2) usually = GrandTotal - Commission? 
//            // Or GrandTotal + Commission?
//            // Let's assume Net Amount = Grand Total (inc Tax)
//            // But usually Invoice Amount is what Customer Pays.

//            Entry.Amount = grandTotalAmount;
//            Entry.Qty = grandTotalQty;
//            Entry.TaxAmount = totalTax;

//            // Net Amount stored in String Attribute 2 per request
//            Entry.EntryAttribute2 = grandTotalAmount.ToString();

//            if (Entry.EntryId == 0)
//            {
//                Entry.UcpId = activeUcpId;
//                Entry.EntryType = "SAL";
//                Entry.CreationOn = DateTime.Now;
//                // Generate Number
//                var lastEntry = await _context.Entries
//                   .Where(e => e.EntryType == "SAL")
//                   .OrderByDescending(e => e.EntryNumber)
//                   .FirstOrDefaultAsync();
//                Entry.EntryNumber = (lastEntry?.EntryNumber ?? 0) + 1;

//                _context.Entries.Add(Entry);
//                await _context.SaveChangesAsync();

//                // Children
//                foreach (var lineItem in LineItems.Where(l => l.ItemId.HasValue))
//                {
//                    var child = new Entry
//                    {
//                        EntryId = 0, // Required field
//                        EntryType = "SALL",
//                        EntryRefId = Entry.EntryId,
//                        EntryDate = Entry.EntryDate,
//                        ItemId = lineItem.ItemId,
//                        Qty = lineItem.Qty,
//                        Lbs = lineItem.Lbs,
//                        Rate = lineItem.Rate,
//                        Amount = lineItem.Amount,
//                        TaxAmount = lineItem.GstAmt,
//                        EntryAttribute1 = lineItem.GstPer?.ToString(), // Store GST %
//                        UcpId = activeUcpId,
//                        CreationOn = DateTime.Now
//                    };
//                    _context.Entries.Add(child);
//                }
//            }
//            else
//            {
//                var existing = await _context.Entries.FindAsync(Entry.EntryId);
//                if (existing == null) return NotFound();

//                existing.EntryDate = Entry.EntryDate;
//                existing.EntryAttribute1 = Entry.EntryAttribute1; // Customer
//                existing.EntryRefId = Entry.EntryRefId; // SSD Ref
//                existing.EntryRefNo = Entry.EntryRefNo; // PUR Ref
//                existing.EntryRemarks = Entry.EntryRemarks;
//                existing.EntryCommPer = Entry.EntryCommPer;
//                existing.EntryCommAmt = Entry.EntryCommAmt;
//                existing.EntryCommSukri = Entry.EntryCommSukri;
//                existing.EntryTermsDays = Entry.EntryTermsDays;
//                existing.EntryPostedToFbr = Entry.EntryPostedToFbr;
//                existing.EntryFbrDiNo = Entry.EntryFbrDiNo;
//                existing.EntryFbrError = Entry.EntryFbrError;

//                existing.Qty = Entry.Qty;
//                existing.Amount = Entry.Amount;
//                existing.TaxAmount = Entry.TaxAmount;
//                existing.EntryAttribute2 = Entry.EntryAttribute2;
//                existing.UcpId = activeUcpId;

//                existing.LastUpdateOn = DateTime.Now;

//                // Handle Children
//                var existingChildren = await _context.Entries
//                   .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "SALL")
//                   .ToListAsync();

//                var submittedIds = LineItems.Where(l => l.EntryId > 0).Select(l => l.EntryId).ToHashSet();

//                // Delete
//                foreach (var c in existingChildren)
//                {
//                    if (!submittedIds.Contains(c.EntryId)) _context.Entries.Remove(c);
//                }

//                // Update/Add
//                foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
//                {
//                    if (l.EntryId > 0)
//                    {
//                        var child = existingChildren.FirstOrDefault(c => c.EntryId == l.EntryId);
//                        if (child != null)
//                        {
//                            child.ItemId = l.ItemId;
//                            child.Qty = l.Qty;
//                            child.Lbs = l.Lbs;
//                            child.Rate = l.Rate;
//                            child.Amount = l.Amount;
//                            child.TaxAmount = l.GstAmt;
//                            child.EntryAttribute1 = l.GstPer?.ToString();
//                            child.UcpId = activeUcpId;
//                            child.LastUpdateOn = DateTime.Now;
//                        }
//                    }
//                    else
//                    {
//                        var child = new Entry
//                        {
//                            EntryId = 0, // Required field
//                            EntryType = "SALL",
//                            EntryRefId = Entry.EntryId,
//                            EntryDate = Entry.EntryDate,
//                            ItemId = l.ItemId,
//                            Qty = l.Qty,
//                            Lbs = l.Lbs,
//                            Rate = l.Rate,
//                            Amount = l.Amount,
//                            TaxAmount = l.GstAmt,
//                            EntryAttribute1 = l.GstPer?.ToString(),
//                            UcpId = activeUcpId,
//                            CreationOn = DateTime.Now
//                        };
//                        _context.Entries.Add(child);
//                    }
//                }
//            }

//            await _context.SaveChangesAsync();
//            await _context.SaveChangesAsync();
//            TempData["StatusMessage"] = isNew ? "Sales entry created successfully!" : "Sales entry updated successfully!";
//            return RedirectToPage("./Index");
//        }

//        public async Task<JsonResult> OnGetFilteredSsd(string customerId)
//        {
//            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
//            if (!activeUcpId.HasValue || string.IsNullOrEmpty(customerId))
//                return new JsonResult(new List<object>());

//            var ssdEntries = await _context.Entries
//                .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId && e.EntryAttribute1 == customerId)
//                .OrderByDescending(e => e.EntryDate)
//                .Select(e => new
//                {
//                    e.EntryId,
//                    e.EntryNumber,
//                    e.EntryRefCode,
//                    Items = _context.Entries
//                        .Where(li => li.EntryType == "SSDL" && li.EntryRefId == e.EntryId)
//                        .Join(_context.ItemMasters, li => li.ItemId, im => im.ItemId, (li, im) => new { li.Rate, im.ItemName })
//                        .ToList()
//                })
//                .ToListAsync();

//            var result = ssdEntries.Select(e => new
//            {
//                e.EntryId,
//                DisplayText = $"SSD-{e.EntryNumber} " +
//                              (!string.IsNullOrEmpty(e.EntryRefCode) ? $" [{e.EntryRefCode}]" : "") +
//                              (e.Items.Any() ? "(Rates: " + string.Join(", ", e.Items.Select(i => i.Rate?.ToString("F4")).Distinct()) + ")" : "") +
//                              (e.Items.Any() ? " - " + string.Join(", ", e.Items.Select(i => i.ItemName).Distinct()) : "")
//            });

//            return new JsonResult(result);
////        }

//        public async Task<JsonResult> OnGetSsdDetails(long ssdId)
//        {
//            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
//            if (!activeUcpId.HasValue) return new JsonResult(null);

//            // 1. Get SSD and its linked PSD ID (EntryRefId)
//            var ssd = await _context.Entries
//                .Where(e => e.EntryId == ssdId && e.EntryType == "SSD")
//                .Select(e => new
//                {
//                    e.EntryId,
//                    e.EntryDate,
//                    e.Qty,
//                    PsdId = e.EntryRefId // This is the PSD ID
//                })
//                .FirstOrDefaultAsync();

//            if (ssd == null) return new JsonResult(null);

//            // 2. Fetch all PURs linked to this PSD
//            var purList = new List<object>();
//            if (ssd.PsdId.HasValue)
//            {
//                 // First get the PSD's EntryNumber (PUR.EntryRefNo references PSD.EntryNumber, not PSD.EntryId)
//                 var psdEntry = await _context.Entries
//                     .Where(e => e.EntryId == ssd.PsdId && e.EntryType == "PSD")
//                     .Select(e => new { e.EntryNumber })
//                     .FirstOrDefaultAsync();

//                 var psdNumber = psdEntry?.EntryNumber;

//                 // Fetch PURs and their items for rate display
//                var purs = await _context.Entries
//                    .Where(e => e.EntryType == "PUR" && psdNumber.HasValue && e.EntryRefNo == psdNumber)
//                    .OrderByDescending(e => e.EntryDate)
//                    .Select(e => new 
//                    {
//                        e.EntryNumber,
//                        e.EntryRefCode,
//                        Items = _context.Entries
//                            .Where(li => li.EntryType == "PURL" && li.EntryRefId == e.EntryId)
//                            .Select(li => new { li.Rate }) // optimizing fetch
//                            .ToList()
//                    })
//                    .ToListAsync();

//                purList = purs.Select(p => new
//                {
//                    Value = p.EntryNumber,
//                    Text = $"PUR-{p.EntryNumber}" + 
//                           (!string.IsNullOrEmpty(p.EntryRefCode) ? $" [{p.EntryRefCode}]" : "") +
//                           (p.Items.Any() ? " (Rates: " + string.Join(", ", p.Items.Select(i => i.Rate?.ToString("F4")).Distinct()) + ")" : "")
//                }).Cast<object>().ToList();
//            }

//            var items = await _context.Entries
//                .Where(e => e.EntryType == "SSDL" && e.EntryRefId == ssdId)
//                .Join(_context.ItemMasters, li => li.ItemId, im => im.ItemId, (li, im) => new
//                {
//                    li.ItemId,
//                    im.ItemName,
//                    li.Qty,
//                    li.Lbs,
//                    li.Rate
//                })
//                .ToListAsync();

//            // Calculate remaining quantities (simplified for AJAX, logic mirroring LoadDropdowns)
//            var salLines = await _context.Entries
//                .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
//                .Join(_context.Entries.Where(h => h.EntryType == "SAL" && h.EntryRefId == ssdId),
//                      l => l.EntryRefId,
//                      h => h.EntryId,
//                      (l, h) => l)
//                .ToListAsync();

//            var soldMap = salLines
//                .GroupBy(l => l.ItemId ?? 0)
//                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty ?? 0));

//            var itemDetails = items.Select(i => new
//            {
//                i.ItemId,
//                i.ItemName,
//                OriginalQty = i.Qty ?? 0,
//                RemainingQty = Math.Max(0, (i.Qty ?? 0) - (soldMap.ContainsKey((long)(i.ItemId ?? 0)) ? soldMap[(long)(i.ItemId ?? 0)] : 0)),
//                Rate = i.Rate ?? 0,
//                Lbs = i.Lbs ?? 0
//            }).ToList();

//            return new JsonResult(new
//            {
//                ssd.EntryId,
//                Date = ssd.EntryDate?.ToString("yyyy-MM-dd"),
//                ssd.Qty,
//                ValidPurs = purList, // List of valid PURs
//                Items = itemDetails
//            });
//        }
//    }
//}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Sales
{
    public class EntryModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public EntryModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Entry Entry { get; set; } = new Entry { EntryId = 0 };

        [BindProperty]
        public List<InvoiceLineItemViewModel> LineItems { get; set; } = new List<InvoiceLineItemViewModel>();

        public SelectList? CustomerList { get; set; }
        public SelectList? SalesSodaList { get; set; }
        public SelectList? ItemList { get; set; }
        public SelectList? PurchaseEntryList { get; set; }

        public string DisplayEntryNumber { get; set; } = "New";
        public bool IsLedgerPosted { get; set; }

        public class InvoiceLineItemViewModel
        {
            public long EntryId { get; set; } // 0 for new
            public long? ItemId { get; set; }
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
            public double? GstPer { get; set; } // ENTRY_ATTRIBUTE1
            public double? GstAmt { get; set; } // TAX_AMOUNT
            public double? TotalAmount { get; set; } // Amount + Tax
        }

        // For JS Data
        public List<SsdEntryInfo>? SsdEntriesForJs { get; set; }
        public class SsdEntryInfo
        {
            public long EntryId { get; set; }
            public DateTime? EntryDate { get; set; }
            public long? CustomerId { get; set; }
            public long? PsdId { get; set; }
            public string RefCode { get; set; }
            public double RemainingQty { get; set; }
            public List<SsdItemInfo> Items { get; set; } = new List<SsdItemInfo>();
        }

        public class SsdItemInfo
        {
            public long ItemId { get; set; }
            public string ItemName { get; set; }
            public double OriginalQty { get; set; }
            public double RemainingQty { get; set; }
            public double Rate { get; set; }
            public double Lbs { get; set; }
        }

        // PUR JS Data
        public List<PurEntryInfo>? PurEntriesForJs { get; set; }
        public class PurEntryInfo
        {
            public long EntryNumber { get; set; }
            public string Date { get; set; } = "";
            public double TotalQty { get; set; }
            public double TotalRemainingQty { get; set; }
            public long? PsdId { get; set; }
            public string? RefCode { get; set; }
            public List<PurItemInfo> Items { get; set; } = new List<PurItemInfo>();
        }
        public class PurItemInfo
        {
            public long ItemId { get; set; }
            public string ItemName { get; set; }
            public double Qty { get; set; }
            public double SoldQty { get; set; }
            public double RemainingQty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
        }


        public async Task<IActionResult> OnGetAsync(long? id, long? ssdId)
        {
            var path = HttpContext.Request.Path.Value ?? "";
            if (!AuthHelper.CanRead(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to view Sales entries.";
                return RedirectToPage("/Index");
            }

            if (id.HasValue && id > 0)
            {
                Entry = await _context.Entries.FindAsync(id);
                if (Entry == null) return NotFound();

                if (Entry.EntryNumber.HasValue)
                {
                    DisplayEntryNumber = "SAL-" + Entry.EntryNumber;
                }

                // Check if posted
                IsLedgerPosted = await _context.Transactions.AnyAsync(t => t.EntryId == id);

                // Load Children (SALL)
                var childEntries = await _context.Entries
                    .Where(e => e.EntryRefId == id && e.EntryType == "SALL")
                    .ToListAsync();

                LineItems = childEntries.Select(c =>
                {
                    double.TryParse(c.EntryAttribute1, out double gstPer);
                    return new InvoiceLineItemViewModel
                    {
                        EntryId = c.EntryId,
                        ItemId = c.ItemId,
                        Qty = c.Qty,
                        Lbs = c.Lbs,
                        Rate = c.Rate,
                        Amount = c.Amount,
                        GstPer = gstPer,
                        GstAmt = c.TaxAmount,
                        TotalAmount = (c.Amount ?? 0) + (c.TaxAmount ?? 0)
                    };
                }).ToList();
            }
            else
            {
                Entry = new Entry
                {
                    EntryId = 0,
                    EntryType = "SAL",
                    EntryDate = DateTime.Today,
                    EntryRefId = ssdId
                };
                LineItems.Add(new InvoiceLineItemViewModel());
            }

            await LoadDropdowns();
            return Page();
        }

        private async Task LoadDropdowns()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Please select a company first.";
                return; // Or handle as needed
            }

            if (_context.CustomerMasters != null)
            {
                var customers = await _context.CustomerMasters
                    .Where(c => c.UcpId == activeUcpId)
                    .OrderBy(c => c.CustomerName).ToListAsync();
                CustomerList = new SelectList(customers, "CustomerId", "CustomerName");
            }

            if (_context.ItemMasters != null)
            {
                var items = await _context.ItemMasters
                    .Where(i => i.UcpId == activeUcpId)
                    .OrderBy(i => i.ItemName).ToListAsync();
                ItemList = new SelectList(items, "ItemId", "ItemName");
            }

            if (_context.Entries != null && _context.ItemMasters != null)
            {
                // 1. Fetch PUR & PURL data
                var purHeaders = await _context.Entries
                    .Where(e => e.EntryType == "PUR" && e.UcpId == activeUcpId)
                    .ToListAsync();

                var purLines = await _context.Entries
                    .Where(l => l.EntryType == "PURL" && l.EntryRefId != null)
                    .Join(_context.Entries.Where(h => h.EntryType == "PUR"),
                          l => l.EntryRefId,
                          h => h.EntryId,
                          (l, h) => new { l.ItemId, l.Qty, l.Lbs, l.Rate, h.EntryId, h.EntryNumber })
                    .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { l.EntryId, l.EntryNumber, l.ItemId, l.Qty, l.Lbs, l.Rate, i.ItemName })
                    .ToListAsync();

                // Calculate SALL units already sold against these PUR numbers
                var soldHeaders = await _context.Entries
                    .Where(e => e.EntryType == "SAL" && e.EntryRefNo != null)
                    .Join(_context.Entries.Where(h => h.EntryType == "PUR"),
                          s => s.EntryRefNo,
                          p => p.EntryNumber,
                          (s, p) => new { s.EntryId, s.EntryRefNo })
                    .ToListAsync();

                var soldLines = await _context.Entries
                    .Where(l => l.EntryType == "SALL" && l.EntryRefId != null)
                    .Join(_context.Entries.Where(e => e.EntryType == "SAL"),
                          l => l.EntryRefId,
                          e => e.EntryId,
                          (l, e) => l)
                    .ToListAsync();

                // Group sold quantities by PUR_NUMBER and ITEM_ID
                var purSoldMap = new Dictionary<(long, long), double>();
                foreach (var sl in soldLines)
                {
                    var parentSal = soldHeaders.FirstOrDefault(h => h.EntryId == sl.EntryRefId);
                    if (parentSal != null && parentSal.EntryRefNo.HasValue)
                    {
                        // EXCLUDE CURRENT DOCUMENT from consumption
                        if (Entry != null && Entry.EntryId == sl.EntryRefId) continue;

                        var key = (parentSal.EntryRefNo.Value, sl.ItemId ?? 0);
                        if (!purSoldMap.ContainsKey(key)) purSoldMap[key] = 0;
                        purSoldMap[key] += sl.Qty ?? 0;
                    }
                }

                PurEntriesForJs = purHeaders.Select(h =>
                {
                    var items = purLines.Where(l => l.EntryId == h.EntryId).Select(l =>
                    {
                        var sold = purSoldMap.ContainsKey((h.EntryNumber ?? 0, l.ItemId ?? 0)) ? purSoldMap[(h.EntryNumber ?? 0, l.ItemId ?? 0)] : 0;
                        var rem = (l.Qty ?? 0) - sold;
                        return new PurItemInfo
                        {
                            ItemId = l.ItemId ?? 0,
                            ItemName = l.ItemName,
                            Qty = l.Qty ?? 0,
                            SoldQty = sold,
                            RemainingQty = rem > 0 ? rem : 0,
                            Lbs = l.Lbs ?? 0,
                            Rate = l.Rate ?? 0
                        };
                    }).ToList();

                    return new PurEntryInfo
                    {
                        EntryNumber = h.EntryNumber ?? 0,
                        Date = h.EntryDate.HasValue ? h.EntryDate.Value.ToString("yyyy-MM-dd") : "",
                        TotalQty = h.Qty ?? 0,
                        TotalRemainingQty = items.Sum(i => i.RemainingQty),
                        PsdId = h.EntryRefNo,
                        RefCode = h.EntryRefCode,
                        Items = items
                    };
                }).ToList();

                var purEntriesDropdown = PurEntriesForJs
                    .OrderByDescending(e => e.Date)
                    .Select(e => new
                    {
                        e.EntryNumber,
                        DisplayText = "PUR-" + e.EntryNumber + 
                                     (!string.IsNullOrEmpty(e.RefCode) ? " , " + e.RefCode : "") 
                                    /* " (Rate: " + (e.Items.Any() ? e.Items.First().Rate.ToString("F4") : "0.0000") + ")" */
                                     //(e.Items.Any() ? " - " + string.Join(", ", e.Items.Select(i => i.ItemName).Distinct()) : "")
                    })
                    .ToList();
                PurchaseEntryList = new SelectList(purEntriesDropdown, "EntryNumber", "DisplayText");

                var ssdHeaders = await _context.Entries
                    .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId)
                    .OrderByDescending(e => e.EntryDate)
                    .Select(e => new
                    {
                        e.EntryId,
                        e.EntryNumber,
                        e.EntryDate,
                        e.EntryAttribute1, // CustomerID
                        e.EntryRefId, // PSD ID link
                        e.EntryRefCode, // Added Ref Code
                        e.Qty // Total Qty
                    })
                    .ToListAsync();

                // 3. Fetch SSDL Lines (Sale Soda Items)
                var ssdItemsRaw = await _context.Entries
                   .Where(e => e.EntryType == "SSDL" && e.EntryRefId != null)
                   .Join(_context.Entries, child => child.EntryRefId, parent => parent.EntryId, (child, parent) => new { parent.EntryId, child })
                   .Join(_context.ItemMasters, p => p.child.ItemId, i => i.ItemId, (p, i) => new
                   {
                       p.EntryId,
                       p.child.ItemId,
                       p.child.Qty,
                       p.child.Lbs,
                       p.child.Rate,
                       i.ItemName
                   })
                   .ToListAsync();

                var groupedSsdItems = ssdItemsRaw
                   .GroupBy(x => x.EntryId)
                   .ToDictionary(g => g.Key, g => g.ToList());

                // 4. Fetch SALL (Sale Invoice Lines) - These reduce the SSD Balance
                var salHeaders = await _context.Entries
                    .Where(e => e.EntryType == "SAL" && e.EntryRefId != null) // RefId is SSD ID
                    .Select(e => new { e.EntryId, e.EntryRefId })
                    .ToListAsync();

                var salMap = salHeaders.ToDictionary(k => k.EntryId, v => v.EntryRefId); // SAL ID -> SSD ID

                var salLines = await _context.Entries
                    .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
                    .ToListAsync();

                // Calculate Sold Qty per (SSD_ID, Item_ID)
                var soldMap = new Dictionary<(long, long), double>();

                foreach (var line in salLines)
                {
                    if (line.EntryRefId.HasValue && salMap.ContainsKey(line.EntryRefId.Value))
                    {
                        var ssdId = salMap[line.EntryRefId.Value]; // The Parent SSD
                        var itemId = line.ItemId ?? 0;

                        // Exclude current entry being edited from "Sold" count
                        if (Entry != null && Entry.EntryId == line.EntryRefId.Value) continue;

                        if (ssdId.HasValue)
                        {
                            var key = (ssdId.Value, itemId);
                            if (!soldMap.ContainsKey(key)) soldMap[key] = 0;
                            soldMap[key] += line.Qty ?? 0;
                        }
                    }
                }

                SsdEntriesForJs = ssdHeaders.Select(ssd =>
                {
                    var items = new List<SsdItemInfo>();
                    double totalRemaining = 0;

                    if (groupedSsdItems.ContainsKey(ssd.EntryId))
                    {
                        foreach (var si in groupedSsdItems[ssd.EntryId])
                        {
                            var sold = soldMap.ContainsKey((ssd.EntryId, si.ItemId ?? 0)) ? soldMap[(ssd.EntryId, si.ItemId ?? 0)] : 0;
                            var original = si.Qty ?? 0;
                            var rem = original - sold;
                            if (rem < 0) rem = 0;

                            items.Add(new SsdItemInfo
                            {
                                ItemId = si.ItemId ?? 0,
                                ItemName = si.ItemName,
                                OriginalQty = original,
                                RemainingQty = rem,
                                Rate = si.Rate ?? 0,
                                Lbs = si.Lbs ?? 0
                            });
                            totalRemaining += rem;
                        }
                    }
                    else
                    {
                        if (ssd.Qty > 0) totalRemaining = ssd.Qty.Value;
                    }

                    long.TryParse(ssd.EntryAttribute1, out long custId);

                    return new SsdEntryInfo
                    {
                        EntryId = ssd.EntryId,
                        EntryDate = ssd.EntryDate,
                        CustomerId = custId,
                        PsdId = ssd.EntryRefId,
                        RefCode = ssd.EntryRefCode,
                        RemainingQty = totalRemaining,
                        Items = items
                    };
                }).ToList();

                var dropdownList = SsdEntriesForJs.Select(e => new
                {
                    e.EntryId,
                    DisplayText = $"SSD-{_context.Entries.Where(x => x.EntryId == e.EntryId).Select(x => x.EntryNumber).FirstOrDefault()}" +
                                  (!string.IsNullOrEmpty(e.RefCode) ? $" [{e.RefCode}]" : "") +
                                  " , Rs. " + (e.Items.Any() ? e.Items.First().Rate.ToString("F4") : "0.0000") + 
                                  (e.Items.Any() ? " , " + string.Join(", ", e.Items.Select(i => i.ItemName).Distinct()) : "")
                }).ToList();

                SalesSodaList = new SelectList(dropdownList, "EntryId", "DisplayText");

                SalesSodaList = new SelectList(dropdownList, "EntryId", "DisplayText");
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var path = HttpContext.Request.Path.Value ?? "";
            bool isNew = Entry.EntryId == 0;

            if (isNew && !AuthHelper.CanCreate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to create Sales entries.";
                return RedirectToPage("./Index");
            }

            if (!isNew && !AuthHelper.CanUpdate(HttpContext, path))
            {
                TempData["ErrorMessage"] = "Access Denied: You do not have permission to update Sales entries.";
                return RedirectToPage("./Index");
            }

            // Check if posted
            if (Entry.EntryId > 0 && await _context.Transactions.AnyAsync(t => t.EntryId == Entry.EntryId))
            {
                TempData["ErrorMessage"] = "Cannot update: This sales entry has already been posted to the ledger.";
                return RedirectToPage("./Index");
            }

            if (!Entry.EntryRefNo.HasValue)
            {
                ModelState.AddModelError("Entry.EntryRefNo", "The Against Purchase (PUR) field is required.");
            }
            else
            {
                // Re-calculate stock for this specific PUR to prevent over-selling
                var purHeader = await _context.Entries.FirstOrDefaultAsync(e => e.EntryType == "PUR" && e.EntryNumber == Entry.EntryRefNo);
                if (purHeader != null)
                {
                    var purLines = await _context.Entries
                        .Where(e => e.EntryType == "PURL" && e.EntryRefId == purHeader.EntryId)
                        .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { l.ItemId, l.Qty, i.ItemName })
                        .ToListAsync();

                    var soldAgainstPur = await _context.Entries
                        .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
                        .Join(_context.Entries.Where(h => h.EntryType == "SAL" && h.EntryRefNo == Entry.EntryRefNo && h.EntryId != Entry.EntryId),
                              l => l.EntryRefId,
                              h => h.EntryId,
                              (l, h) => new { l.ItemId, l.Qty })
                        .GroupBy(e => e.ItemId)
                        .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Qty ?? 0) })
                        .ToDictionaryAsync(x => x.ItemId ?? 0, x => x.Qty);

                    foreach (var line in LineItems)
                    {
                        var itemId = line.ItemId ?? 0;
                        var purItem = purLines.FirstOrDefault(i => i.ItemId == itemId);
                        if (purItem != null)
                        {
                            var purchased = purItem.Qty ?? 0;
                            var alreadySold = soldAgainstPur.ContainsKey(itemId) ? soldAgainstPur[itemId] : 0;
                            var remaining = purchased - alreadySold;

                            if ((line.Qty ?? 0) > remaining)
                            {
                                ModelState.AddModelError("", $"Quantity for '{purItem.ItemName}' exceeds available Purchase balance ({remaining}).");
                            }
                        }
                    }
                }
            }

            // SSD Check
            if (Entry.EntryRefId.HasValue)
            {
                var ssdHeader = await _context.Entries.FirstOrDefaultAsync(e => e.EntryId == Entry.EntryRefId && e.EntryType == "SSD");
                if (ssdHeader != null)
                {
                    var ssdLines = await _context.Entries
                        .Where(e => e.EntryType == "SSDL" && e.EntryRefId == ssdHeader.EntryId)
                        .Join(_context.ItemMasters, l => l.ItemId, i => i.ItemId, (l, i) => new { l.ItemId, l.Qty, i.ItemName })
                        .ToListAsync();

                    var soldAgainstSsd = await _context.Entries
                        .Where(e => e.EntryType == "SALL" && e.EntryRefId != null)
                        .Join(_context.Entries.Where(h => h.EntryType == "SAL" && h.EntryRefId == Entry.EntryRefId && h.EntryId != Entry.EntryId),
                              l => l.EntryRefId,
                              h => h.EntryId,
                              (l, h) => new { l.ItemId, l.Qty })
                        .GroupBy(e => e.ItemId)
                        .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Qty ?? 0) })
                        .ToDictionaryAsync(x => x.ItemId ?? 0, x => x.Qty);

                    foreach (var line in LineItems)
                    {
                        var itemId = line.ItemId ?? 0;
                        var ssdItem = ssdLines.FirstOrDefault(i => i.ItemId == itemId);
                        if (ssdItem != null)
                        {
                            var budgeted = ssdItem.Qty ?? 0;
                            var alreadySold = soldAgainstSsd.ContainsKey(itemId) ? soldAgainstSsd[itemId] : 0;
                            var remaining = budgeted - alreadySold;

                            if ((line.Qty ?? 0) > remaining)
                            {
                                ModelState.AddModelError("", $"Quantity for '{ssdItem.ItemName}' exceeds available SSD balance ({remaining}).");
                            }
                        }
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                TempData["ErrorMessage"] = string.Join(" | ", errors);
                await LoadDropdowns();
                return Page();
            }

            // Get Active Company Profile from Session
            long? activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            // Calculation Logic
            double grandTotalQty = 0;
            double grandTotalAmount = 0; // Item Amounts + Tax
            double totalTax = 0;
            double totalItemValue = 0;

            foreach (var item in LineItems)
            {
                if (item.Rate.HasValue)
                {
                    double amt = 0;
                    if (item.Lbs.HasValue)
                    {
                        amt = item.Lbs.Value * item.Rate.Value;
                    }
                    else if (item.Qty.HasValue)
                    {
                        // Fallback if Lbs is missing? Or should we enforce Lbs? 
                        // Given request "multiply on lbs * rate", assuming Lbs is primary.
                        // If Lbs is 0 or null, amount might be 0.
                        amt = (item.Lbs ?? 0) * item.Rate.Value;
                    }

                    item.Amount = amt;

                    // Tax
                    double tax = 0;
                    if (item.GstPer.HasValue && item.GstPer > 0)
                    {
                        tax = (item.Amount.Value * item.GstPer.Value) / 100;
                    }
                    item.GstAmt = tax;
                    item.TotalAmount = item.Amount.Value + tax;

                    grandTotalQty += item.Qty.Value;
                    grandTotalAmount += item.TotalAmount.Value;
                    totalTax += tax;
                    totalItemValue += item.Amount.Value;
                }
            }

            // Commission
            // Comm Amt = (ItemValue * CommPer) / 100 ? Or on TotalAmount? Usually on ItemValue (Pre-tax)
            double commAmt = 0;
            if (Entry.EntryCommPer.HasValue && Entry.EntryCommPer > 0)
            {
                commAmt = (totalItemValue * Entry.EntryCommPer.Value) / 100;
            }
            Entry.EntryCommAmt = commAmt;

            // Sukri is usually deduction or addition? Assuming deduction from final? Or just info?
            // "Net Amount" (EntryAttribute2) usually = GrandTotal - Commission? 
            // Or GrandTotal + Commission?
            // Let's assume Net Amount = Grand Total (inc Tax)
            // But usually Invoice Amount is what Customer Pays.

            Entry.Amount = grandTotalAmount;
            Entry.Qty = grandTotalQty;
            Entry.TaxAmount = totalTax;

            // Net Amount stored in String Attribute 2 per request
            Entry.EntryAttribute2 = grandTotalAmount.ToString();

            if (Entry.EntryId == 0)
            {
                Entry.UcpId = activeUcpId;
                Entry.EntryType = "SAL";
                Entry.CreationOn = DateTime.Now;
                var lastEntry = await _context.Entries
                   .Where(e => e.EntryType == "SAL" && e.UcpId == activeUcpId)
                   .OrderByDescending(e => e.EntryNumber)
                   .FirstOrDefaultAsync();
                Entry.EntryNumber = (lastEntry?.EntryNumber ?? 0) + 1;

                _context.Entries.Add(Entry);
                await _context.SaveChangesAsync();

                // Children
                foreach (var lineItem in LineItems.Where(l => l.ItemId.HasValue))
                {
                    var child = new Entry
                    {
                        EntryId = 0, // Required field
                        EntryType = "SALL",
                        EntryRefId = Entry.EntryId,
                        EntryDate = Entry.EntryDate,
                        ItemId = lineItem.ItemId,
                        Qty = lineItem.Qty,
                        Lbs = lineItem.Lbs,
                        Rate = lineItem.Rate,
                        Amount = lineItem.Amount,
                        TaxAmount = lineItem.GstAmt,
                        EntryAttribute1 = lineItem.GstPer?.ToString(), // Store GST %
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
                existing.EntryAttribute1 = Entry.EntryAttribute1; // Customer
                existing.EntryRefId = Entry.EntryRefId; // SSD Ref
                existing.EntryRefNo = Entry.EntryRefNo; // PUR Ref
                existing.EntryRemarks = Entry.EntryRemarks;
                existing.EntryCommPer = Entry.EntryCommPer;
                existing.EntryCommAmt = Entry.EntryCommAmt;
                existing.EntryCommSukri = Entry.EntryCommSukri;
                existing.EntryTermsDays = Entry.EntryTermsDays;
                existing.EntryPostedToFbr = Entry.EntryPostedToFbr;
                existing.EntryFbrDiNo = Entry.EntryFbrDiNo;
                existing.EntryFbrError = Entry.EntryFbrError;

                existing.Qty = Entry.Qty;
                existing.Amount = Entry.Amount;
                existing.TaxAmount = Entry.TaxAmount;
                existing.EntryAttribute2 = Entry.EntryAttribute2;
                existing.UcpId = activeUcpId;

                existing.LastUpdateOn = DateTime.Now;

                // Handle Children
                var existingChildren = await _context.Entries
                   .Where(e => e.EntryRefId == Entry.EntryId && e.EntryType == "SALL")
                   .ToListAsync();

                var submittedIds = LineItems.Where(l => l.EntryId > 0).Select(l => l.EntryId).ToHashSet();

                // Delete
                foreach (var c in existingChildren)
                {
                    if (!submittedIds.Contains(c.EntryId)) _context.Entries.Remove(c);
                }

                // Update/Add
                foreach (var l in LineItems.Where(x => x.ItemId.HasValue))
                {
                    if (l.EntryId > 0)
                    {
                        var child = existingChildren.FirstOrDefault(c => c.EntryId == l.EntryId);
                        if (child != null)
                        {
                            child.ItemId = l.ItemId;
                            child.Qty = l.Qty;
                            child.Lbs = l.Lbs;
                            child.Rate = l.Rate;
                            child.Amount = l.Amount;
                            child.TaxAmount = l.GstAmt;
                            child.EntryAttribute1 = l.GstPer?.ToString();
                            child.UcpId = activeUcpId;
                            child.LastUpdateOn = DateTime.Now;
                        }
                    }
                    else
                    {
                        var child = new Entry
                        {
                            EntryId = 0, // Required field
                            EntryType = "SALL",
                            EntryRefId = Entry.EntryId,
                            EntryDate = Entry.EntryDate,
                            ItemId = l.ItemId,
                            Qty = l.Qty,
                            Lbs = l.Lbs,
                            Rate = l.Rate,
                            Amount = l.Amount,
                            TaxAmount = l.GstAmt,
                            EntryAttribute1 = l.GstPer?.ToString(),
                            UcpId = activeUcpId,
                            CreationOn = DateTime.Now
                        };
                        _context.Entries.Add(child);
                    }
                }
            }

            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = isNew ? "Sales entry created successfully!" : "Sales entry updated successfully!";
            return RedirectToPage("./Index");
        }
    }
}