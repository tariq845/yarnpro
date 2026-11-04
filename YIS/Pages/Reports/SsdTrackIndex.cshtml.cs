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



    public class SsdTrackIndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public SsdTrackIndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<ContractSellSummaryViewModel> Entries { get; set; } = new List<ContractSellSummaryViewModel>();







        [BindProperty(SupportsGet = true)]



        public string? SearchTerm { get; set; }







        public class ContractSellSummaryViewModel



        {



            public long EntryId { get; set; }



            public long? EntryNumber { get; set; }



            public DateTime? EntryDate { get; set; }



            public string? CustomerName { get; set; }



            public string? LinkedPsdNumber { get; set; }



            public long? LinkedPsdId { get; set; }



            public bool IsCommissionBased { get; set; }



            public string ContractBasis => IsCommissionBased ? "Commission Based" : "Net Based";



            public int ItemCount { get; set; }



            public double ContractQty { get; set; }



            public double ContractLbs { get; set; }



            public double ContractAmount { get; set; }



            public double DeliveredQty { get; set; }



            public double DeliveredLbs { get; set; }



            public double DeliveredAmount { get; set; }



            public double PendingQty => Math.Max(0, ContractQty - DeliveredQty);



            public double PendingLbs => Math.Max(0, ContractLbs - DeliveredLbs);



            public string Status



            {



                get



                {



                    if (DeliveredQty <= 0) return "Pending";



                    if (DeliveredQty >= ContractQty) return "Completed";



                    return "Partial";



                }



            }



        }







        public async Task OnGetAsync()



        {



            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);



            if (_context.Entries == null) return;







            var ssdHeadersQuery = _context.Entries



                .Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId);







            var rawHeaders = await (from e in ssdHeadersQuery



                                    join c in _context.CustomerMasters.Where(cm => cm.UcpId == activeUcpId)



                                    on e.EntryAttribute1 equals c.CustomerId.ToString() into custJoin



                                    from c in custJoin.DefaultIfEmpty()



                                    join psd in _context.Entries.Where(pe => pe.EntryType == "PSD" && pe.UcpId == activeUcpId)



                                    on e.EntryRefId equals psd.EntryId into psdJoin



                                    from psd in psdJoin.DefaultIfEmpty()



                                    select new



                                    {



                                        e.EntryId,



                                        e.EntryNumber,



                                        e.EntryDate,



                                        CustomerName = c != null ? c.CustomerName : "Unknown",



                                        LinkedPsdNumber = psd != null ? ("PSD-" + psd.EntryNumber) : (e.EntryRefId.HasValue ? ("PSD-" + e.EntryRefId) : "-"),



                                        LinkedPsdId = psd != null ? psd.EntryId : e.EntryRefId,



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



                    (!string.IsNullOrEmpty(x.CustomerName) && x.CustomerName.ToLower().Contains(term)) ||



                    (!string.IsNullOrEmpty(x.LinkedPsdNumber) && x.LinkedPsdNumber.ToLower().Contains(term))



                ).ToList();



            }







            var ssdIds = rawHeaders.Select(x => x.EntryId).ToList();







            // SSDL lines for these SSDs



            var ssdlAgg = await (from ssdl in _context.Entries



                                 where ssdl.EntryType == "SSDL" && ssdl.UcpId == activeUcpId && ssdl.EntryRefId.HasValue && ssdIds.Contains(ssdl.EntryRefId.Value)



                                 group ssdl by ssdl.EntryRefId!.Value into g



                                 select new



                                 {



                                     SsdId = g.Key,



                                     ItemCount = g.Count(),



                                     TotalQty = g.Sum(x => x.Qty ?? 0),



                                     TotalLbs = g.Sum(x => x.Lbs ?? 0),



                                     TotalAmount = g.Sum(x => x.Amount ?? 0)



                                 }).ToDictionaryAsync(x => x.SsdId, x => x);







            // SAL lines linked to these SSDs (EntryRefId == SSD.EntryId)



            var salLines = await (from sal in _context.Entries



                                  join sall in _context.Entries on sal.EntryId equals sall.EntryRefId



                                  where sal.EntryType == "SAL" && sall.EntryType == "SALL" &&



                                        sal.UcpId == activeUcpId && sall.UcpId == activeUcpId &&



                                        sal.EntryRefId.HasValue && ssdIds.Contains(sal.EntryRefId.Value)



                                  group sall by sal.EntryRefId!.Value into g



                                  select new



                                  {



                                      SsdId = g.Key,



                                      DeliveredQty = g.Sum(x => x.Qty ?? 0),



                                      DeliveredLbs = g.Sum(x => x.Lbs ?? 0),



                                      DeliveredAmount = g.Sum(x => x.Amount ?? 0)



                                  }).ToDictionaryAsync(x => x.SsdId, x => x);







            Entries = rawHeaders.Select(h =>



            {



                ssdlAgg.TryGetValue(h.EntryId, out var ssdl);



                salLines.TryGetValue(h.EntryId, out var sal);







                double cQty = (ssdl != null && ssdl.TotalQty > 0) ? ssdl.TotalQty : h.HeaderQty;



                double cLbs = (ssdl != null && ssdl.TotalLbs > 0) ? ssdl.TotalLbs : h.HeaderLbs;



                double cAmt = (ssdl != null && ssdl.TotalAmount > 0) ? ssdl.TotalAmount : h.HeaderAmount;







                return new ContractSellSummaryViewModel



                {



                    EntryId = h.EntryId,



                    EntryNumber = h.EntryNumber,



                    EntryDate = h.EntryDate,



                    CustomerName = h.CustomerName,



                    LinkedPsdNumber = h.LinkedPsdNumber,



                    LinkedPsdId = h.LinkedPsdId,



                    IsCommissionBased = h.IsCommissionBased,



                    ItemCount = ssdl?.ItemCount ?? (cQty > 0 ? 1 : 0),



                    ContractQty = cQty,



                    ContractLbs = cLbs,



                    ContractAmount = cAmt,



                    DeliveredQty = sal?.DeliveredQty ?? 0,



                    DeliveredLbs = sal?.DeliveredLbs ?? 0,



                    DeliveredAmount = sal?.DeliveredAmount ?? 0



                };



            }).ToList();



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            await OnGetAsync();
            var columns = new List<ExcelColumn<ContractSellSummaryViewModel>>
            {
                new("SSD #", x => $"SSD-{x.EntryNumber}", null, XLAlignmentHorizontalValues.Center),
                new("Date", x => x.EntryDate, "dd-MM-yyyy", XLAlignmentHorizontalValues.Center),
                new("Customer Name", x => x.CustomerName ?? "-"),
                new("Contract Basis", x => x.ContractBasis, null, XLAlignmentHorizontalValues.Center),
                new("Contract Bags", x => x.ContractQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Contract Lbs", x => x.ContractLbs, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Contract Amount", x => x.ContractAmount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Delivered Bags", x => x.DeliveredQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Delivered Lbs", x => x.DeliveredLbs, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Delivered Amount", x => x.DeliveredAmount, "#,##0.00", XLAlignmentHorizontalValues.Right),
                new("Pending Bags", x => x.PendingQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var bytes = ExcelExportHelper.ExportToExcel("SSD Movement Tracking Summary", columns, Entries, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"SSD_Tracking_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
