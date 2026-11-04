using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Purchase
{
    public class InvoiceViewModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public InvoiceViewModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public PurchaseInvoicePrintModel Invoice { get; set; } = new();

        public class PurchaseInvoicePrintModel
        {
            public long EntryId { get; set; }
            public string InvoiceNo { get; set; } = "";
            public string Date { get; set; } = "";
            public string Remarks { get; set; } = "";
            public string? RefPsdNo { get; set; }
            public long? RefPsdId { get; set; }
            public string? SupplierBillNo { get; set; }
            public string? BasisText { get; set; }
            public bool IsCommissionBased { get; set; }
            public double? CommPer { get; set; }
            public double? CommAmt { get; set; }
            public string? CommAgent { get; set; }

            // Buyer (Our Company)
            public string BuyerName { get; set; } = "";
            public string? BuyerOwnerName { get; set; }
            public string BuyerNtn { get; set; } = "";
            public string BuyerAddress { get; set; } = "";
            public string BuyerProvince { get; set; } = "";
            public string? BuyerLogo { get; set; }
            public string? BuyerTermsDetails { get; set; }

            // Terms
            public int? TermsDays { get; set; }
            public string PaymentMode { get; set; } = "Credit";

            // Supplier (Seller)
            public string SupplierName { get; set; } = "";
            public string SupplierNtn { get; set; } = "";
            public string SupplierAddress { get; set; } = "";
            public string SupplierContact { get; set; } = "";

            // Totals
            public double TotalQty { get; set; }
            public double TotalLbs { get; set; }
            public double TotalBasic { get; set; }
            public double TotalTax { get; set; }
            public double GrandTotal { get; set; }

            public List<PurchaseItemPrintModel> Items { get; set; } = new();
        }

        public class PurchaseItemPrintModel
        {
            public string ItemName { get; set; } = "";
            public string HsCode { get; set; } = "-";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double ValueExcl { get; set; }
            public double TaxPer { get; set; }
            public double TaxAmt { get; set; }
            public double ValueIncl { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (_context.Entries == null) return NotFound();

            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "PUR") return NotFound();

            // Fetch Supplier
            SupplierMaster? supplier = null;
            if (long.TryParse(entry.EntryAttribute1, out long sid))
            {
                supplier = await _context.SupplierMasters.FirstOrDefaultAsync(s => s.SupplierId == sid);
            }

            // Fetch Company Profile (Buyer)
            var profile = await _context.UserCompanyProfiles.FirstOrDefaultAsync(ucp => ucp.UcpId == entry.UcpId)
                          ?? await _context.UserCompanyProfiles.FirstOrDefaultAsync();

            // Fetch Parent PSD if linked
            Entry? psd = null;
            if (entry.EntryRefNo.HasValue && entry.EntryRefNo.Value > 0)
            {
                psd = await _context.Entries.FirstOrDefaultAsync(e => e.EntryType == "PSD" &&
                    (e.EntryId == entry.EntryRefNo.Value || e.EntryNumber == entry.EntryRefNo.Value));
            }

            // Fetch Lines
            var itemsQuery = await (from p in _context.Entries
                                    join i in _context.ItemMasters on p.ItemId equals i.ItemId into itemJoin
                                    from item in itemJoin.DefaultIfEmpty()
                                    where p.EntryRefId == id && p.EntryType == "PURL"
                                    select new
                                    {
                                        p.ItemId,
                                        ItemName = item != null ? item.ItemName : "Item",
                                        HsCode = item != null ? item.ItemHsCode : "-",
                                        Qty = p.Qty ?? 0,
                                        Lbs = p.Lbs ?? ((p.Qty ?? 0) * 100),
                                        Rate = p.Rate ?? 0,
                                        Amount = p.Amount ?? 0,
                                        GstPer = p.EntryAttribute2,
                                        TaxAmt = p.TaxAmount ?? 0
                                    }).ToListAsync();

            bool isComm = (entry.EntryAttribute5 != null && entry.EntryAttribute5.ToLower() == "yes") || ((entry.EntryCommAmt ?? 0) > 0);

            var items = itemsQuery.Select(i =>
            {
                double taxPer = double.TryParse(i.GstPer, out double tp) ? tp : 0;
                double valExcl = i.Amount > 0 ? i.Amount : (i.Lbs * i.Rate);
                double taxAmt = i.TaxAmt > 0 ? i.TaxAmt : (valExcl * (taxPer / 100.0));
                double valIncl = valExcl + taxAmt;

                return new PurchaseItemPrintModel
                {
                    ItemName = i.ItemName,
                    HsCode = string.IsNullOrWhiteSpace(i.HsCode) ? "-" : i.HsCode,
                    Qty = i.Qty,
                    Lbs = i.Lbs,
                    Rate = i.Rate,
                    ValueExcl = valExcl,
                    TaxPer = taxPer,
                    TaxAmt = taxAmt,
                    ValueIncl = valIncl
                };
            }).ToList();

            Invoice = new PurchaseInvoicePrintModel
            {
                EntryId = entry.EntryId,
                InvoiceNo = "PUR-" + entry.EntryNumber?.ToString().PadLeft(4, '0'),
                Date = entry.EntryDate?.ToString("dd-MM-yyyy") ?? "-",
                Remarks = entry.EntryRemarks ?? "",
                SupplierBillNo = entry.EntryRefCode,
                RefPsdNo = psd != null ? $"PSD-{psd.EntryNumber}" : (entry.EntryRefNo.HasValue ? $"PSD-{entry.EntryRefNo}" : null),
                RefPsdId = psd?.EntryId ?? entry.EntryRefNo,
                IsCommissionBased = isComm,
                BasisText = isComm ? "Commission Based" : "Net Based",
                CommPer = entry.EntryCommPer,
                CommAmt = entry.EntryCommAmt,
                CommAgent = entry.EntryAttribute4,

                // Buyer Info
                BuyerName = profile?.UcpCompanyName ?? "YARN PRO",
                BuyerOwnerName = profile?.UcpOwnerName,
                BuyerNtn = profile?.UcpCompanyNtn ?? "-",
                BuyerAddress = profile?.UcpCompanyAddress ?? "-",
                BuyerProvince = profile?.UcpCompanyProvince ?? "Punjab",
                BuyerLogo = profile?.UcpPath,
                BuyerTermsDetails = profile?.UcpTermsDetails,

                // Terms
                TermsDays = entry.EntryTermsDays,
                PaymentMode = !string.IsNullOrEmpty(entry.EntryAttribute2) ? entry.EntryAttribute2 : "Credit",

                // Supplier Info
                SupplierName = supplier?.SupplierName ?? "Unknown Supplier",
                SupplierNtn = supplier?.SupplierNtnNo ?? "-",
                SupplierAddress = supplier?.SupplierAddress ?? "-",
                SupplierContact = supplier?.SupplierContact ?? "-",

                // Totals
                TotalQty = items.Sum(x => x.Qty),
                TotalLbs = items.Sum(x => x.Lbs),
                TotalBasic = items.Sum(x => x.ValueExcl),
                TotalTax = items.Sum(x => x.TaxAmt),
                GrandTotal = items.Sum(x => x.ValueIncl),

                Items = items
            };

            return Page();
        }
    }
}

