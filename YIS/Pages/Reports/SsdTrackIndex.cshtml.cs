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

public double ReturnedQty { get; set; }

public double ReturnedLbs { get; set; }

public double ReturnedAmount { get; set; }

public double NetDeliveredQty => Math.Max(0, DeliveredQty - ReturnedQty);

public double NetDeliveredLbs => Math.Max(0, DeliveredLbs - ReturnedLbs);

public double NetDeliveredAmount => Math.Max(0, DeliveredAmount - ReturnedAmount);

public double PendingQty => Math.Max(0, ContractQty - NetDeliveredQty);

public double PendingLbs => Math.Max(0, ContractLbs - NetDeliveredLbs);

public string Status

{

get

{

if (NetDeliveredQty <= 0) return "Pending";

if (NetDeliveredQty >= ContractQty) return "Completed";

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

            // SAL invoices linked to these SSDs (EntryRefId == SSD.EntryId)
            var salInvoices = await (from sal in _context.Entries
                                     where sal.EntryType == "SAL" &&
                                           sal.UcpId == activeUcpId &&
                                           sal.EntryRefId.HasValue && ssdIds.Contains(sal.EntryRefId.Value)
                                     select new
                                     {
                                         SalId = sal.EntryId,
                                         SalNumber = sal.EntryNumber,
                                         SsdId = sal.EntryRefId.Value
                                     }).ToListAsync();

            var salIds = salInvoices.Select(x => x.SalId).ToList();
            var salNumbers = salInvoices.Where(x => x.SalNumber.HasValue).Select(x => x.SalNumber!.Value).ToList();
            var salToSsd = salInvoices.ToDictionary(x => x.SalId, x => x.SsdId);
            var salNumToSsd = salInvoices.Where(x => x.SalNumber.HasValue).ToDictionary(x => x.SalNumber!.Value, x => x.SsdId);

            // SAL lines (Delivered)
            var salLineItems = await (from sall in _context.Entries
                                      where sall.EntryType == "SALL" && sall.UcpId == activeUcpId &&
                                            sall.EntryRefId.HasValue && salIds.Contains(sall.EntryRefId.Value)
                                      select new
                                      {
                                          SalId = sall.EntryRefId.Value,
                                          Qty = sall.Qty ?? 0,
                                          Lbs = sall.Lbs ?? 0,
                                          Amount = sall.Amount ?? 0
                                      }).ToListAsync();

            var deliveredMap = new Dictionary<long, (double Qty, double Lbs, double Amount)>();
            foreach (var item in salLineItems)
            {
                if (salToSsd.TryGetValue(item.SalId, out long ssdId))
                {
                    var cur = deliveredMap.TryGetValue(ssdId, out var v) ? v : (0, 0, 0);
                    deliveredMap[ssdId] = (cur.Qty + item.Qty, cur.Lbs + item.Lbs, cur.Amount + item.Amount);
                }
            }

            // CN lines (Returned)
            var cnLineItems = await (from cn in _context.Entries
                                     join cnl in _context.Entries on cn.EntryId equals cnl.EntryRefId
                                     where cn.EntryType == "CN" && cnl.EntryType == "CNL" &&
                                           cn.UcpId == activeUcpId && cnl.UcpId == activeUcpId &&
                                           ((cn.EntryRefId.HasValue && salIds.Contains(cn.EntryRefId.Value)) ||
                                            (cn.EntryRefNo.HasValue && salNumbers.Contains(cn.EntryRefNo.Value)))
                                     select new
                                     {
                                         cn.EntryRefId,
                                         cn.EntryRefNo,
                                         Qty = cnl.Qty ?? 0,
                                         Lbs = cnl.Lbs ?? 0,
                                         Amount = cnl.Amount ?? 0
                                     }).ToListAsync();

            var returnedMap = new Dictionary<long, (double Qty, double Lbs, double Amount)>();
            foreach (var item in cnLineItems)
            {
                long? ssdId = null;
                if (item.EntryRefId.HasValue && salToSsd.TryGetValue(item.EntryRefId.Value, out long s1))
                {
                    ssdId = s1;
                }
                else if (item.EntryRefNo.HasValue && salNumToSsd.TryGetValue(item.EntryRefNo.Value, out long s2))
                {
                    ssdId = s2;
                }

                if (ssdId.HasValue)
                {
                    var cur = returnedMap.TryGetValue(ssdId.Value, out var v) ? v : (0, 0, 0);
                    returnedMap[ssdId.Value] = (cur.Qty + item.Qty, cur.Lbs + item.Lbs, cur.Amount + item.Amount);
                }
            }

            Entries = rawHeaders.Select(h =>
            {
                ssdlAgg.TryGetValue(h.EntryId, out var ssdl);
                deliveredMap.TryGetValue(h.EntryId, out var del);
                returnedMap.TryGetValue(h.EntryId, out var ret);

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
                    DeliveredQty = del.Qty,
                    DeliveredLbs = del.Lbs,
                    DeliveredAmount = del.Amount,
                    ReturnedQty = ret.Qty,
                    ReturnedLbs = ret.Lbs,
                    ReturnedAmount = ret.Amount
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
                new("Returned Bags (CN)", x => x.ReturnedQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Net Delivered Bags", x => x.NetDeliveredQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Pending Bags", x => x.PendingQty, "#,##0", XLAlignmentHorizontalValues.Right),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var bytes = ExcelExportHelper.ExportToExcel("SSD Movement Tracking Summary", columns, Entries, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"SSD_Tracking_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
