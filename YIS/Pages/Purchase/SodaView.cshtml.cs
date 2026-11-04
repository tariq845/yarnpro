using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Purchase
{
    public class SodaViewModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SodaViewModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public PurchaseContractPrintModel Contract { get; set; } = new();

        public class PurchaseContractPrintModel
        {
            public long EntryId { get; set; }
            public string ContractNo { get; set; } = "";
            public string Date { get; set; } = "";
            public string Remarks { get; set; } = "";
            public string PaymentMode { get; set; } = "Credit";
            public int? TermsDays { get; set; }
            public string BasisText { get; set; } = "Net Based";
            public bool IsCommissionBased { get; set; }
            public double? CommPer { get; set; }
            public double? CommAmt { get; set; }
            public double? CommSukri { get; set; }
            public string? CommAgent { get; set; }

            // Buyer Company
            public string CompanyName { get; set; } = "YARN PRO";
            public string? OwnerName { get; set; }
            public string CompanyNtn { get; set; } = "-";
            public string CompanyAddress { get; set; } = "-";
            public string CompanyProvince { get; set; } = "Punjab";
            public string? CompanyTermsDetails { get; set; }

            // Supplier
            public string SupplierName { get; set; } = "Unknown Supplier";
            public string SupplierNtn { get; set; } = "-";
            public string SupplierAddress { get; set; } = "-";
            public string SupplierContact { get; set; } = "-";

            // Totals
            public double TotalQty { get; set; }
            public double TotalLbs { get; set; }
            public double TotalBasic { get; set; }
            public double TotalTax { get; set; }
            public double GrandTotal { get; set; }

            public List<SodaLinePrintModel> Items { get; set; } = new();
        }

        public class SodaLinePrintModel
        {
            public string ItemName { get; set; } = "";
            public string HsCode { get; set; } = "-";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double GstPercent { get; set; }
            public double TaxAmount { get; set; }
            public double NetAmount { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (_context.Entries == null) return NotFound();

            var entry = await _context.Entries.FindAsync(id);
            if (entry == null || entry.EntryType != "PSD") return NotFound();

            SupplierMaster? supplier = null;
            if (long.TryParse(entry.EntryAttribute1, out long sid))
            {
                supplier = await _context.SupplierMasters.FirstOrDefaultAsync(s => s.SupplierId == sid);
            }

            var profile = await _context.UserCompanyProfiles.FirstOrDefaultAsync(ucp => ucp.UcpId == entry.UcpId)
                          ?? await _context.UserCompanyProfiles.FirstOrDefaultAsync();

            var linesQuery = await (from p in _context.Entries
                                    join i in _context.ItemMasters on p.ItemId equals i.ItemId into itemJoin
                                    from item in itemJoin.DefaultIfEmpty()
                                    where p.EntryRefId == id && p.EntryType == "PSDL"
                                    select new
                                    {
                                        ItemName = item != null ? item.ItemName : "Standard Yarn",
                                        HsCode = item != null ? item.ItemHsCode : "-",
                                        Qty = p.Qty ?? 0,
                                        Lbs = p.Lbs ?? ((p.Qty ?? 0) * 100),
                                        Rate = p.Rate ?? 0,
                                        Amount = p.Amount ?? 0,
                                        GstPer = p.EntryAttribute2,
                                        TaxAmt = p.TaxAmount ?? 0,
                                        NetAmt = p.EntryAttribute3
                                    }).ToListAsync();

            bool isComm = (entry.EntryAttribute5 != null && entry.EntryAttribute5.ToLower() == "yes") || ((entry.EntryCommAmt ?? 0) > 0);

            var items = linesQuery.Select(l =>
            {
                double gst = double.TryParse(l.GstPer, out double g) ? g : 0;
                double basicAmt = l.Amount > 0 ? l.Amount : (l.Lbs * l.Rate);
                double taxAmt = l.TaxAmt > 0 ? l.TaxAmt : (basicAmt * (gst / 100.0));
                double netAmt = basicAmt + taxAmt;

                return new SodaLinePrintModel
                {
                    ItemName = l.ItemName,
                    HsCode = string.IsNullOrWhiteSpace(l.HsCode) ? "-" : l.HsCode,
                    Qty = l.Qty,
                    Lbs = l.Lbs,
                    Rate = l.Rate,
                    Amount = basicAmt,
                    GstPercent = gst,
                    TaxAmount = taxAmt,
                    NetAmount = netAmt
                };
            }).ToList();

            // Fallback for single line in header
            if (!items.Any() && ((entry.Qty ?? 0) > 0 || (entry.Amount ?? 0) > 0))
            {
                string itemName = "Standard Yarn";
                if (entry.ItemId.HasValue)
                {
                    var it = await _context.ItemMasters.FirstOrDefaultAsync(i => i.ItemId == entry.ItemId.Value);
                    if (it != null) itemName = it.ItemName;
                }
                double basicAmt = entry.Amount ?? 0;
                double taxAmt = entry.TaxAmount ?? 0;
                items.Add(new SodaLinePrintModel
                {
                    ItemName = itemName,
                    Qty = entry.Qty ?? 0,
                    Lbs = entry.Lbs ?? ((entry.Qty ?? 0) * 100),
                    Rate = entry.Rate ?? 0,
                    Amount = basicAmt,
                    TaxAmount = taxAmt,
                    NetAmount = basicAmt + taxAmt
                });
            }

            Contract = new PurchaseContractPrintModel
            {
                EntryId = entry.EntryId,
                ContractNo = "PSD-" + entry.EntryNumber?.ToString().PadLeft(4, '0'),
                Date = entry.EntryDate?.ToString("dd-MM-yyyy") ?? "-",
                Remarks = entry.EntryRemarks ?? "",
                PaymentMode = !string.IsNullOrEmpty(entry.EntryAttribute2) ? entry.EntryAttribute2 : "Credit",
                TermsDays = entry.EntryTermsDays,
                IsCommissionBased = isComm,
                BasisText = isComm ? "Commission Based" : "Net Based",
                CommPer = entry.EntryCommPer,
                CommAmt = entry.EntryCommAmt,
                CommSukri = entry.EntryCommSukri,
                CommAgent = entry.EntryAttribute4,

                CompanyName = profile?.UcpCompanyName ?? "YARN PRO",
                OwnerName = profile?.UcpOwnerName,
                CompanyNtn = profile?.UcpCompanyNtn ?? "-",
                CompanyAddress = profile?.UcpCompanyAddress ?? "-",
                CompanyProvince = profile?.UcpCompanyProvince ?? "Punjab",
                CompanyTermsDetails = profile?.UcpTermsDetails,

                SupplierName = supplier?.SupplierName ?? "Unknown Supplier",
                SupplierNtn = supplier?.SupplierNtnNo ?? "-",
                SupplierAddress = supplier?.SupplierAddress ?? "-",
                SupplierContact = supplier?.SupplierContact ?? "-",

                TotalQty = items.Sum(x => x.Qty),
                TotalLbs = items.Sum(x => x.Lbs),
                TotalBasic = items.Sum(x => x.Amount),
                TotalTax = items.Sum(x => x.TaxAmount),
                GrandTotal = items.Sum(x => x.NetAmount),

                Items = items
            };

            return Page();
        }
    }
}

