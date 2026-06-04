using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Sales
{
    public class InvoiceViewModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public InvoiceViewModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public SalesInvoicePrintModel Invoice { get; set; } = new();

        public class SalesInvoicePrintModel
        {
            public long EntryId { get; set; }
            public string InvoiceNo { get; set; }
            public string Date { get; set; }
            public string Remarks { get; set; }
            public string? FbrDiNo { get; set; }
            
            // Seller Info
            public string SellerName { get; set; }
            public string SellerNtn { get; set; }
            public string SellerAddress { get; set; }
            public string SellerProvince { get; set; }
            public string? SellerLogo { get; set; }

            // Buyer Info
            public string BuyerName { get; set; }
            public string BuyerNtn { get; set; }
            public string BuyerAddress { get; set; }
            public string BuyerProvince { get; set; }

            // Totals
            public double TotalQty { get; set; }
            public double TotalTax { get; set; }
            public double GrandTotal { get; set; }

            public List<InvoiceItemModel> Items { get; set; } = new();
        }

        public class InvoiceItemModel
        {
            public string ItemName { get; set; }
            public string HsCode { get; set; }
            public double Qty { get; set; }
            public string Uom { get; set; } // Lbs/Meter
            public double Rate { get; set; }
            public double ValueExcl { get; set; }
            public double TaxPer { get; set; }
            public double TaxAmt { get; set; }
            public double ValueIncl { get; set; }
            
            // New Placeholders
            public double RetailPrice { get; set; }
            public double ExtraTax { get; set; }
            public double FurtherTax { get; set; }
            public double Fed { get; set; }
            public double StWht { get; set; }
            public double Discount { get; set; }
            public double Lbs { get; set; }
            public double Kgs { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (_context.Entries == null) return NotFound();

            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "SAL") return NotFound();

            // Fetch Customer
            CustomerMaster? customer = null;
            if (long.TryParse(entry.EntryAttribute1, out long cid))
            {
                customer = await _context.CustomerMasters.FirstOrDefaultAsync(c => c.CustomerId == cid);
            }

            // Fetch Seller (Company Profile)
            var sellerProfile = await _context.UserCompanyProfiles.FirstOrDefaultAsync(ucp => ucp.UcpId == entry.UcpId);
            if (sellerProfile == null)
            {
                sellerProfile = await _context.UserCompanyProfiles.FirstOrDefaultAsync();
            }

            // Fetch Line Items
            var itemsQuery = await (from s in _context.Entries
                               join i in _context.ItemMasters on s.ItemId equals i.ItemId into itemJoin
                               from item in itemJoin.DefaultIfEmpty()
                               where s.EntryRefId == id && s.EntryType == "SALL"
                               select new
                               {
                                   s.ItemId,
                                   ItemName = item != null ? item.ItemName : "Unknown",
                                   HsCode = item != null ? item.ItemHsCode : "-",
                                   ItemUom = item != null ? item.Uom : null,
                                   Qty = s.Qty ?? 0,
                                   Lbs = s.Lbs,
                                   Rate = s.Rate ?? 0,
                                   Amount = s.Amount ?? 0,
                                   GstPer = s.EntryAttribute1,
                                   GstAmt = s.TaxAmount ?? 0
                               }).ToListAsync();

            // Map to ViewModel
            Invoice = new SalesInvoicePrintModel
            {
                EntryId = entry.EntryId,
                InvoiceNo = "SAL-" + entry.EntryNumber?.ToString().PadLeft(4, '0'),
                Date = entry.EntryDate?.ToString("dd-MMM-yyyy") ?? "-",
                Remarks = entry.EntryRemarks ?? "",
                TotalQty = entry.Qty ?? 0,
                TotalTax = entry.TaxAmount ?? 0,
                GrandTotal = entry.Amount ?? 0,
                FbrDiNo = entry.EntryFbrDiNo,
                
                // Seller Mapping
                SellerName = sellerProfile?.UcpCompanyName ?? "YIS Company",
                SellerNtn = sellerProfile?.UcpCompanyNtn ?? "-",
                SellerAddress = sellerProfile?.UcpCompanyAddress ?? "-",
                SellerProvince = sellerProfile?.UcpCompanyProvince ?? "-",
                SellerLogo = sellerProfile?.UcpPath,

                BuyerName = customer?.CustomerName ?? "Unknown",
                BuyerNtn = customer?.CustomerNtnNo ?? "-",
                BuyerAddress = customer?.CustomerAddress ?? "-",
                BuyerProvince = customer?.Province?? "Sindh", // Default
                
                Items = itemsQuery.Select(i => new InvoiceItemModel
                {
                    ItemName = i.ItemName,
                    HsCode = i.HsCode,
                    Qty = i.Qty,
                    //Uom = !string.IsNullOrEmpty(i.ItemUom) ? i.ItemUom : ((i.Lbs.HasValue && i.Lbs.Value > 0) ? "LBS" : "KG"),
                    Lbs=i.Lbs ?? 0,
                    Kgs = (i.Lbs ?? 0) / 2.2046,
                    Rate = i.Rate,
                    ValueExcl = (i.Lbs ?? 0) * i.Rate,
                    TaxPer = double.TryParse(i.GstPer, out double tp) ? tp : 0,
                    TaxAmt = i.GstAmt,
                    ValueIncl = ((i.Lbs ?? 0) * i.Rate) + i.GstAmt,
                    
                    // Placeholders
                    RetailPrice = i.Rate, // Assuming Retail Price same as Rate for now? Or 0? Let's keep it same as Rate
                    ExtraTax = 0,
                    FurtherTax = 0,
                    Fed = 0,
                    StWht = 0,
                    Discount = 0
                }).ToList()
            };

            return Page();
        }
    }
}
