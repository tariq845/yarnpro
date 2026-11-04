using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;



using YIS.Helpers;







namespace YIS.Pages.Reports



{



    public class PsdTrackIndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public PsdTrackIndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<ContractMovementSummaryViewModel> Entries { get; set; } = new List<ContractMovementSummaryViewModel>();







        [BindProperty(SupportsGet = true)]



        public string? SearchTerm { get; set; }







        public class ContractMovementSummaryViewModel



        {



            public long EntryId { get; set; }



            public long? EntryNumber { get; set; }



            public DateTime? EntryDate { get; set; }



            public string? SupplierName { get; set; }



            public bool IsCommissionBased { get; set; }



            public string ContractBasis => IsCommissionBased ? "Commission Based" : "Net Based";



            public int ItemCount { get; set; }



            public double ContractQty { get; set; }



            public double ContractLbs { get; set; }



            public double ContractAmount { get; set; }



            public double PurchasedQty { get; set; }



            public double PurchasedLbs { get; set; }



            public double PurchasedAmount { get; set; }



            public double PendingQty => Math.Max(0, ContractQty - PurchasedQty);



            public double PendingLbs => Math.Max(0, ContractLbs - PurchasedLbs);



            public double SoldQty { get; set; }



            public double SoldLbs { get; set; }



            public string Status



            {



                get



                {



                    if (PurchasedQty <= 0) return "Pending";



                    if (PurchasedQty >= ContractQty) return "Completed";



                    return "Partial";



                }



            }



        }







        public async Task OnGetAsync()



        {



            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);



            if (_context.Entries == null) return;







            var psdHeadersQuery = _context.Entries



                .Where(e => e.EntryType == "PSD" && e.UcpId == activeUcpId);







            var rawHeaders = await (from e in psdHeadersQuery



                                    join s in _context.SupplierMasters.Where(sm => sm.UcpId == activeUcpId)



                                    on e.EntryAttribute1 equals s.SupplierId.ToString() into suppJoin



                                    from s in suppJoin.DefaultIfEmpty()



                                    select new



                                    {



                                        e.EntryId,



                                        e.EntryNumber,



                                        e.EntryDate,



                                        SupplierName = s != null ? s.SupplierName : "Unknown",



                                        IsCommissionBased = (e.EntryAttribute5 != null && e.EntryAttribute5.ToLower() == "yes") || ((e.EntryCommAmt ?? 0) > 0),



                                        HeaderQty = e.Qty ?? 0,



                                        HeaderLbs = e.Lbs ?? 0,



                                        HeaderAmount = e.Amount ?? 0



                                    })



                                    .OrderByDescending(x => x.EntryDate)



                                    .ThenByDescending(x => x.EntryNumber)



                                    .ToListAsync();







            if (!string.IsNullOrEmpty(SearchTerm))



            {



                var term = SearchTerm.Trim().ToLower();



                rawHeaders = rawHeaders.Where(x =>



                    (x.EntryNumber.HasValue && x.EntryNumber.Value.ToString().Contains(term)) ||



                    (!string.IsNullOrEmpty(x.SupplierName) && x.SupplierName.ToLower().Contains(term))



                ).ToList();



            }







            var psdIds = rawHeaders.Select(x => x.EntryId).ToList();







            // PSDL lines for these PSDs



            var psdlAgg = await (from psdl in _context.Entries



                                 where psdl.EntryType == "PSDL" && psdl.UcpId == activeUcpId && psdl.EntryRefId.HasValue && psdIds.Contains(psdl.EntryRefId.Value)



                                 group psdl by psdl.EntryRefId!.Value into g



                                 select new



                                 {



                                     PsdId = g.Key,



                                     ItemCount = g.Count(),



                                     TotalQty = g.Sum(x => x.Qty ?? 0),



                                     TotalLbs = g.Sum(x => x.Lbs ?? 0),



                                     TotalAmount = g.Sum(x => x.Amount ?? 0)



                                 }).ToDictionaryAsync(x => x.PsdId, x => x);







            // PUR lines linked to these PSDs (EntryRefNo == PSD.EntryId or EntryAttribute2 == PSD.EntryId)



            var purLines = await (from pur in _context.Entries



                                  join purl in _context.Entries on pur.EntryId equals purl.EntryRefId



                                  where pur.EntryType == "PUR" && purl.EntryType == "PURL" &&



                                        pur.UcpId == activeUcpId && purl.UcpId == activeUcpId &&



                                        pur.EntryRefNo.HasValue && psdIds.Contains(pur.EntryRefNo.Value)



                                  group purl by pur.EntryRefNo!.Value into g



                                  select new



                                  {



                                      PsdId = g.Key,



                                      PurchasedQty = g.Sum(x => x.Qty ?? 0),



                                      PurchasedLbs = g.Sum(x => x.Lbs ?? 0),



                                      PurchasedAmount = g.Sum(x => x.Amount ?? 0)



                                  }).ToDictionaryAsync(x => x.PsdId, x => x);







            // SSD lines linked to these PSDs (EntryRefId == PSD.EntryId)



            var ssdLines = await (from ssd in _context.Entries



                                  join ssdl in _context.Entries on ssd.EntryId equals ssdl.EntryRefId



                                  where ssd.EntryType == "SSD" && ssdl.EntryType == "SSDL" &&



                                        ssd.UcpId == activeUcpId && ssdl.UcpId == activeUcpId &&



                                        ssd.EntryRefId.HasValue && psdIds.Contains(ssd.EntryRefId.Value)



                                  group ssdl by ssd.EntryRefId!.Value into g



                                  select new



                                  {



                                      PsdId = g.Key,



                                      SoldQty = g.Sum(x => x.Qty ?? 0),



                                      SoldLbs = g.Sum(x => x.Lbs ?? 0)



                                  }).ToDictionaryAsync(x => x.PsdId, x => x);







            Entries = rawHeaders.Select(h =>



            {



                psdlAgg.TryGetValue(h.EntryId, out var psdl);



                purLines.TryGetValue(h.EntryId, out var pur);



                ssdLines.TryGetValue(h.EntryId, out var ssd);







                double cQty = (psdl != null && psdl.TotalQty > 0) ? psdl.TotalQty : h.HeaderQty;



                double cLbs = (psdl != null && psdl.TotalLbs > 0) ? psdl.TotalLbs : h.HeaderLbs;



                double cAmt = (psdl != null && psdl.TotalAmount > 0) ? psdl.TotalAmount : h.HeaderAmount;







                return new ContractMovementSummaryViewModel



                {



                    EntryId = h.EntryId,



                    EntryNumber = h.EntryNumber,



                    EntryDate = h.EntryDate,



                    SupplierName = h.SupplierName,



                    IsCommissionBased = h.IsCommissionBased,



                    ItemCount = psdl?.ItemCount ?? (cQty > 0 ? 1 : 0),



                    ContractQty = cQty,



                    ContractLbs = cLbs,



                    ContractAmount = cAmt,



                    PurchasedQty = pur?.PurchasedQty ?? 0,



                    PurchasedLbs = pur?.PurchasedLbs ?? 0,



                    PurchasedAmount = pur?.PurchasedAmount ?? 0,



                    SoldQty = ssd?.SoldQty ?? 0,



                    SoldLbs = ssd?.SoldLbs ?? 0



                };



            }).ToList();



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            await OnGetAsync();
            var columns = new List<ExcelColumn<ContractMovementSummaryViewModel>>
            {
                new("PSD #", x => $"PSD-{x.EntryNumber}", null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.EntryDate, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("Supplier Name", x => x.SupplierName ?? "-"),
                new("Contract Basis", x => x.ContractBasis, null, XLAlignmentHorizontalValues.Center),
                new("Contract Bags", x => x.ContractQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Contract Lbs", x => x.ContractLbs, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Contract Amount", x => x.ContractAmount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Purchased Bags", x => x.PurchasedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Purchased Lbs", x => x.PurchasedLbs, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Purchased Amount", x => x.PurchasedAmount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Pending Bags", x => x.PendingQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Sold Bags", x => x.SoldQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var bytes = ExcelExportHelper.ExportToExcel("PSD Movement Tracking Summary", columns, Entries, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PSD_Tracking_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
