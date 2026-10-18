using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class PsdRecordTrackModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PsdRecordTrackModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public Entry? PsdHeader { get; set; }
        public string SupplierName { get; set; } = "Unknown";
        public string? SupplierNtn { get; set; }
        public string? SupplierAddress { get; set; }
        public string? SupplierContact { get; set; }
        public string CompanyName { get; set; } = "YARN PRO";
        public string? OwnerName { get; set; }
        public string? CompanyNtn { get; set; }
        public string? CompanyAddress { get; set; }
        public string? CompanyLogo { get; set; }
        public string? CompanyTermsDetails { get; set; }
        public int? TermsDays => PsdHeader?.EntryTermsDays;
        public string? PaymentMode => PsdHeader?.EntryAttribute2;

        // 1. Contract Lines
        public List<PsdContractItemViewModel> ContractItems { get; set; } = new();

        // 2. Purchases Realized
        public List<PurchaseMovementItemViewModel> PurchaseItems { get; set; } = new();

        // 3. Sales Sodas Booked
        public List<SalesSodaMovementItemViewModel> SalesSodaItems { get; set; } = new();

        // 4. Sales Invoices Delivered
        public List<SalesInvoiceMovementItemViewModel> SalesInvoiceItems { get; set; } = new();

        // Totals & Movement Summary
        public MovementSummaryViewModel Summary { get; set; } = new();

        public class PsdContractItemViewModel
        {
            public long EntryId { get; set; }
            public long? ItemId { get; set; }
            public string ItemName { get; set; } = "Unknown Item";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double TaxAmount { get; set; }
            public double NetAmount { get; set; }
        }

        public class PurchaseMovementItemViewModel
        {
            public long PurId { get; set; }
            public long? PurNumber { get; set; }
            public DateTime? PurDate { get; set; }
            public string? Remarks { get; set; }
            public long? ItemId { get; set; }
            public string ItemName { get; set; } = "Unknown Item";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double TaxAmount { get; set; }
            public double NetAmount { get; set; }
        }

        public class SalesSodaMovementItemViewModel
        {
            public long SsdId { get; set; }
            public long? SsdNumber { get; set; }
            public DateTime? SsdDate { get; set; }
            public string CustomerName { get; set; } = "Unknown Customer";
            public string? Remarks { get; set; }
            public long? ItemId { get; set; }
            public string ItemName { get; set; } = "Unknown Item";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double TaxAmount { get; set; }
            public double NetAmount { get; set; }
        }

        public class SalesInvoiceMovementItemViewModel
        {
            public long SalId { get; set; }
            public long? SalNumber { get; set; }
            public DateTime? SalDate { get; set; }
            public long? RefSsdNumber { get; set; }
            public string CustomerName { get; set; } = "Unknown Customer";
            public string? Remarks { get; set; }
            public long? ItemId { get; set; }
            public string ItemName { get; set; } = "Unknown Item";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double TaxAmount { get; set; }
            public double NetAmount { get; set; }
        }

        public class MovementSummaryViewModel
        {
            // Contract (PSD)
            public double ContractQty { get; set; }
            public double ContractLbs { get; set; }
            public double ContractAmount { get; set; }
            public double ContractAvgRate => (ContractLbs > 0) ? (ContractAmount / (ContractLbs / 10.0)) : (ContractQty > 0 ? (ContractAmount / ContractQty) : 0);

            // Purchases (PUR)
            public double PurchasedQty { get; set; }
            public double PurchasedLbs { get; set; }
            public double PurchasedAmount { get; set; }
            public double PurchasedAvgRate => (PurchasedLbs > 0) ? (PurchasedAmount / (PurchasedLbs / 10.0)) : (PurchasedQty > 0 ? (PurchasedAmount / PurchasedQty) : 0);

            // Pending Contract Balance (PSD - PUR)
            public double PendingContractQty => Math.Max(0, ContractQty - PurchasedQty);
            public double PendingContractLbs => Math.Max(0, ContractLbs - PurchasedLbs);
            public double PendingContractAmount => Math.Max(0, ContractAmount - PurchasedAmount);

            // Sales Soda (SSD)
            public double SsdQty { get; set; }
            public double SsdLbs { get; set; }
            public double SsdAmount { get; set; }
            public double SsdAvgRate => (SsdLbs > 0) ? (SsdAmount / (SsdLbs / 10.0)) : (SsdQty > 0 ? (SsdAmount / SsdQty) : 0);

            // Sales Invoices (SAL)
            public double SalQty { get; set; }
            public double SalLbs { get; set; }
            public double SalAmount { get; set; }
            public double SalAvgRate => (SalLbs > 0) ? (SalAmount / (SalLbs / 10.0)) : (SalQty > 0 ? (SalAmount / SalQty) : 0);

            // Physical Stock Balance in Hand (PUR Received - SAL Delivered)
            public double StockQty => Math.Max(0, PurchasedQty - SalQty);
            public double StockLbs => Math.Max(0, PurchasedLbs - SalLbs);

            // Unsold Contract Balance (PSD Contract - SSD Booked)
            public double UnsoldContractQty => Math.Max(0, ContractQty - SsdQty);
            public double UnsoldContractLbs => Math.Max(0, ContractLbs - SsdLbs);
        }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (_context.Entries == null)
            {
                return NotFound();
            }

            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue) return RedirectToPage("/SelectCompany");

            var company = await _context.UserCompanyProfiles.FirstOrDefaultAsync(c => c.UcpId == activeUcpId);
            if (company != null)
            {
                if (!string.IsNullOrWhiteSpace(company.UcpCompanyName))
                {
                    CompanyName = company.UcpCompanyName;
                }
                OwnerName = company.UcpOwnerName;
                CompanyNtn = company.UcpCompanyNtn;
                CompanyAddress = company.UcpCompanyAddress;
                CompanyLogo = company.UcpPath;
                CompanyTermsDetails = company.UcpTermsDetails;
            }

            // Load PSD Header
            PsdHeader = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == id && e.EntryType == "PSD" && e.UcpId == activeUcpId);

            if (PsdHeader == null)
            {
                return NotFound();
            }

            // Supplier Information
            if (!string.IsNullOrEmpty(PsdHeader.EntryAttribute1) &&
                long.TryParse(PsdHeader.EntryAttribute1, out long supplierId))
            {
                var supplier = await _context.SupplierMasters
                    .FirstOrDefaultAsync(s => s.SupplierId == supplierId && s.UcpId == activeUcpId);
                if (supplier != null)
                {
                    SupplierName = supplier.SupplierName ?? "Unknown";
                    SupplierNtn = supplier.SupplierNtnNo;
                    SupplierAddress = supplier.SupplierAddress;
                    SupplierContact = supplier.SupplierContact;
                }
            }

            // 1. PSD Contract Items
            var psdlRaw = await (from psdl in _context.Entries
                                 join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId)
                                 on psdl.ItemId equals item.ItemId into itemJoin
                                 from item in itemJoin.DefaultIfEmpty()
                                 where psdl.EntryRefId == id && psdl.EntryType == "PSDL" && psdl.UcpId == activeUcpId
                                 orderby psdl.EntryId
                                 select new PsdContractItemViewModel
                                 {
                                     EntryId = psdl.EntryId,
                                     ItemId = psdl.ItemId,
                                     ItemName = item != null ? item.ItemName : "Unknown Item",
                                     Qty = psdl.Qty ?? 0,
                                     Lbs = psdl.Lbs ?? 0,
                                     Rate = psdl.Rate ?? 0,
                                     Amount = psdl.Amount ?? 0,
                                     TaxAmount = psdl.TaxAmount ?? 0,
                                     NetAmount = (psdl.Amount ?? 0) + (psdl.TaxAmount ?? 0)
                                 }).ToListAsync();

            if (psdlRaw.Count == 0 && (PsdHeader.Qty.HasValue || PsdHeader.Amount.HasValue))
            {
                // Fallback for single-line header
                string itemName = "Standard Yarn";
                if (PsdHeader.ItemId.HasValue)
                {
                    var it = await _context.ItemMasters.FirstOrDefaultAsync(i => i.ItemId == PsdHeader.ItemId.Value && i.UcpId == activeUcpId);
                    if (it != null) itemName = it.ItemName;
                }
                psdlRaw.Add(new PsdContractItemViewModel
                {
                    EntryId = PsdHeader.EntryId,
                    ItemId = PsdHeader.ItemId,
                    ItemName = itemName,
                    Qty = PsdHeader.Qty ?? 0,
                    Lbs = PsdHeader.Lbs ?? 0,
                    Rate = PsdHeader.Rate ?? 0,
                    Amount = PsdHeader.Amount ?? 0,
                    TaxAmount = PsdHeader.TaxAmount ?? 0,
                    NetAmount = (PsdHeader.Amount ?? 0) + (PsdHeader.TaxAmount ?? 0)
                });
            }

            ContractItems = psdlRaw;

            // 2. Purchases Realized against this PSD (PUR & PURL)
            PurchaseItems = await (from pur in _context.Entries
                                   join purl in _context.Entries on pur.EntryId equals purl.EntryRefId
                                   join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId) on purl.ItemId equals item.ItemId into itemJoin
                                   from item in itemJoin.DefaultIfEmpty()
                                   where pur.EntryType == "PUR" && purl.EntryType == "PURL" &&
                                         pur.UcpId == activeUcpId && purl.UcpId == activeUcpId &&
                                         (pur.EntryRefNo == id || pur.EntryAttribute2 == id.ToString() || (PsdHeader.EntryNumber.HasValue && pur.EntryRefNo == PsdHeader.EntryNumber.Value))
                                   orderby pur.EntryDate, pur.EntryNumber, purl.EntryId
                                   select new PurchaseMovementItemViewModel
                                   {
                                       PurId = pur.EntryId,
                                       PurNumber = pur.EntryNumber,
                                       PurDate = pur.EntryDate,
                                       Remarks = !string.IsNullOrEmpty(pur.EntryRemarks) ? pur.EntryRemarks : pur.EntryAttribute3,
                                       ItemId = purl.ItemId,
                                       ItemName = item != null ? item.ItemName : "Unknown Item",
                                       Qty = purl.Qty ?? 0,
                                       Lbs = purl.Lbs ?? 0,
                                       Rate = purl.Rate ?? 0,
                                       Amount = purl.Amount ?? 0,
                                       TaxAmount = purl.TaxAmount ?? 0,
                                       NetAmount = (purl.Amount ?? 0) + (purl.TaxAmount ?? 0)
                                   }).ToListAsync();

            // 3. Sales Soda Booked against this PSD (SSD & SSDL)
            var ssdList = await (from ssd in _context.Entries
                                 join ssdl in _context.Entries on ssd.EntryId equals ssdl.EntryRefId
                                 join cust in _context.CustomerMasters.Where(cm => cm.UcpId == activeUcpId)
                                 on ssd.EntryAttribute1 equals cust.CustomerId.ToString() into custJoin
                                 from cust in custJoin.DefaultIfEmpty()
                                 join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId)
                                 on ssdl.ItemId equals item.ItemId into itemJoin
                                 from item in itemJoin.DefaultIfEmpty()
                                 where ssd.EntryType == "SSD" && ssdl.EntryType == "SSDL" &&
                                       ssd.UcpId == activeUcpId && ssdl.UcpId == activeUcpId &&
                                       (ssd.EntryRefId == id || ssd.EntryRefNo == id)
                                 orderby ssd.EntryDate, ssd.EntryNumber, ssdl.EntryId
                                 select new SalesSodaMovementItemViewModel
                                 {
                                     SsdId = ssd.EntryId,
                                     SsdNumber = ssd.EntryNumber,
                                     SsdDate = ssd.EntryDate,
                                     CustomerName = cust != null ? cust.CustomerName : "Unknown Customer",
                                     Remarks = ssd.EntryRemarks,
                                     ItemId = ssdl.ItemId,
                                     ItemName = item != null ? item.ItemName : "Unknown Item",
                                     Qty = ssdl.Qty ?? 0,
                                     Lbs = ssdl.Lbs ?? 0,
                                     Rate = ssdl.Rate ?? 0,
                                     Amount = ssdl.Amount ?? 0,
                                     TaxAmount = ssdl.TaxAmount ?? 0,
                                     NetAmount = (ssdl.Amount ?? 0) + (ssdl.TaxAmount ?? 0)
                                 }).ToListAsync();

            SalesSodaItems = ssdList;
            var ssdIds = ssdList.Select(x => x.SsdId).Distinct().ToList();

            // 4. Sales Invoices Delivered (SAL & SALL) against linked SSDs or PSD
            SalesInvoiceItems = await (from sal in _context.Entries
                                       join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                       join ssd in _context.Entries.Where(e => e.EntryType == "SSD" && e.UcpId == activeUcpId)
                                       on sal.EntryRefId equals ssd.EntryId into ssdJoin
                                       from ssd in ssdJoin.DefaultIfEmpty()
                                       join cust in _context.CustomerMasters.Where(cm => cm.UcpId == activeUcpId)
                                       on sal.EntryAttribute1 equals cust.CustomerId.ToString() into custJoin
                                       from cust in custJoin.DefaultIfEmpty()
                                       join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId)
                                       on sall.ItemId equals item.ItemId into itemJoin
                                       from item in itemJoin.DefaultIfEmpty()
                                       where sal.EntryType == "SAL" && sall.EntryType == "SALL" &&
                                             sal.UcpId == activeUcpId && sall.UcpId == activeUcpId &&
                                             ((sal.EntryRefId.HasValue && ssdIds.Contains(sal.EntryRefId.Value)) || sal.EntryRefNo == id)
                                       orderby sal.EntryDate, sal.EntryNumber, sall.EntryId
                                       select new SalesInvoiceMovementItemViewModel
                                       {
                                           SalId = sal.EntryId,
                                           SalNumber = sal.EntryNumber,
                                           SalDate = sal.EntryDate,
                                           RefSsdNumber = ssd != null ? ssd.EntryNumber : null,
                                           CustomerName = cust != null ? cust.CustomerName : "Unknown Customer",
                                           Remarks = sal.EntryRemarks,
                                           ItemId = sall.ItemId,
                                           ItemName = item != null ? item.ItemName : "Unknown Item",
                                           Qty = sall.Qty ?? 0,
                                           Lbs = sall.Lbs ?? 0,
                                           Rate = sall.Rate ?? 0,
                                           Amount = sall.Amount ?? 0,
                                           TaxAmount = sall.TaxAmount ?? 0,
                                           NetAmount = (sall.Amount ?? 0) + (sall.TaxAmount ?? 0)
                                       }).ToListAsync();

            // 5. Populate Movement Summary
            Summary = new MovementSummaryViewModel
            {
                ContractQty = ContractItems.Sum(x => x.Qty),
                ContractLbs = ContractItems.Sum(x => x.Lbs),
                ContractAmount = ContractItems.Sum(x => x.Amount),

                PurchasedQty = PurchaseItems.Sum(x => x.Qty),
                PurchasedLbs = PurchaseItems.Sum(x => x.Lbs),
                PurchasedAmount = PurchaseItems.Sum(x => x.Amount),

                SsdQty = SalesSodaItems.Sum(x => x.Qty),
                SsdLbs = SalesSodaItems.Sum(x => x.Lbs),
                SsdAmount = SalesSodaItems.Sum(x => x.Amount),

                SalQty = SalesInvoiceItems.Sum(x => x.Qty),
                SalLbs = SalesInvoiceItems.Sum(x => x.Lbs),
                SalAmount = SalesInvoiceItems.Sum(x => x.Amount)
            };

            return Page();
        }
    }
}
