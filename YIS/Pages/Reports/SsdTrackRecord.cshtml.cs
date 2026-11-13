using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Reports
{
    public class SsdTrackRecordModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SsdTrackRecordModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public Entry? SsdHeader { get; set; }
        public string CustomerName { get; set; } = "Unknown";
        public string? CustomerNtn { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CustomerContact { get; set; }
        public string CompanyName { get; set; } = "YARN PRO";
        public string? OwnerName { get; set; }
        public string? CompanyNtn { get; set; }
        public string? CompanyAddress { get; set; }
        public string? CompanyLogo { get; set; }
        public string? CompanyTermsDetails { get; set; }
        public int? TermsDays => SsdHeader?.EntryTermsDays;
        public string? PaymentMode => SsdHeader?.EntryAttribute2;
        public bool IsCommissionBased =>
            string.Equals(SsdHeader?.EntryAttribute5, "Yes", StringComparison.OrdinalIgnoreCase) ||
            ((SsdHeader?.EntryCommAmt ?? 0) > 0);
        public string ContractBasis => IsCommissionBased ? "Commission Based" : "Net Based";

        // Parent PSD Contract Info (if linked)
        public LinkedPsdViewModel? ParentPsd { get; set; }

        // 1. Contract Booked Lines (SSDL)
        public List<SsdContractItemViewModel> ContractItems { get; set; } = new();

        // 2. Sales Invoices Delivered (SAL & SALL)
        public List<SalesDeliveryItemViewModel> DeliveryItems { get; set; } = new();

        // 2b. Sale Returns (CN & CNL)
        public List<SaleReturnItemViewModel> ReturnItems { get; set; } = new();

        // Totals & Movement Summary
        public SsdMovementSummaryViewModel Summary { get; set; } = new();

        public class LinkedPsdViewModel
        {
            public long PsdId { get; set; }
            public long? PsdNumber { get; set; }
            public DateTime? PsdDate { get; set; }
            public string SupplierName { get; set; } = "Unknown";
            public double? Qty { get; set; }
            public double? Lbs { get; set; }
            public double? Rate { get; set; }
            public double? Amount { get; set; }
        }

        public class SsdContractItemViewModel
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

        public class SalesDeliveryItemViewModel
        {
            public long SalId { get; set; }
            public long? SalNumber { get; set; }
            public DateTime? SalDate { get; set; }
            public string? Remarks { get; set; }
            public bool IsCommissionBased { get; set; }
            public string BasisText => IsCommissionBased ? "Commission Based" : "Net Based";
            public long? ItemId { get; set; }
            public string ItemName { get; set; } = "Unknown Item";
            public double Qty { get; set; }
            public double Lbs { get; set; }
            public double Rate { get; set; }
            public double Amount { get; set; }
            public double TaxAmount { get; set; }
            public double NetAmount { get; set; }
        }

        public class SaleReturnItemViewModel
        {
            public long CnId { get; set; }
            public long? CnNumber { get; set; }
            public DateTime? CnDate { get; set; }
            public long? AgainstSalNumber { get; set; }
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

        public class SsdMovementSummaryViewModel
        {
            // Sales Soda Contract (SSD)
            public double ContractQty { get; set; }
            public double ContractLbs { get; set; }
            public double ContractAmount { get; set; }
            public double ContractAvgRate => (ContractLbs > 0) ? (ContractAmount / (ContractLbs / 10.0)) : (ContractQty > 0 ? (ContractAmount / ContractQty) : 0);

            // Invoices Delivered (SAL)
            public double DeliveredQty { get; set; }
            public double DeliveredLbs { get; set; }
            public double DeliveredAmount { get; set; }
            public double DeliveredAvgRate => (DeliveredLbs > 0) ? (DeliveredAmount / (DeliveredLbs / 10.0)) : (DeliveredQty > 0 ? (DeliveredAmount / DeliveredQty) : 0);

            // Returns (CN)
            public double ReturnedQty { get; set; }
            public double ReturnedLbs { get; set; }
            public double ReturnedAmount { get; set; }
            public double ReturnedAvgRate => (ReturnedLbs > 0) ? (ReturnedAmount / (ReturnedLbs / 10.0)) : (ReturnedQty > 0 ? (ReturnedAmount / ReturnedQty) : 0);

            // Net Delivered (SAL - CN)
            public double NetDeliveredQty => Math.Max(0, DeliveredQty - ReturnedQty);
            public double NetDeliveredLbs => Math.Max(0, DeliveredLbs - ReturnedLbs);
            public double NetDeliveredAmount => Math.Max(0, DeliveredAmount - ReturnedAmount);
            public double NetDeliveredAvgRate => (NetDeliveredLbs > 0) ? (NetDeliveredAmount / (NetDeliveredLbs / 10.0)) : (NetDeliveredQty > 0 ? (NetDeliveredAmount / NetDeliveredQty) : 0);

            // Pending Delivery to Customer (SSD - Net Delivered)
            public double PendingDeliveryQty => Math.Max(0, ContractQty - NetDeliveredQty);
            public double PendingDeliveryLbs => Math.Max(0, ContractLbs - NetDeliveredLbs);
            public double PendingDeliveryAmount => Math.Max(0, ContractAmount - NetDeliveredAmount);

            // Linked PSD Info
            public double LinkedPsdQty { get; set; }
            public double LinkedPsdLbs { get; set; }
            public double LinkedPsdAmount { get; set; }
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

            // Load SSD Header
            SsdHeader = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == id && e.EntryType == "SSD" && e.UcpId == activeUcpId);

            if (SsdHeader == null)
            {
                return NotFound();
            }

            // Customer Information
            if (!string.IsNullOrEmpty(SsdHeader.EntryAttribute1) &&
                long.TryParse(SsdHeader.EntryAttribute1, out long customerId))
            {
                var customer = await _context.CustomerMasters
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.UcpId == activeUcpId);
                if (customer != null)
                {
                    CustomerName = customer.CustomerName ?? "Unknown";
                    CustomerNtn = customer.CustomerNtnNo;
                    CustomerAddress = customer.CustomerAddress;
                    CustomerContact = customer.CustomerContact;
                }
            }

            // Parent PSD Information
            if (SsdHeader.EntryRefId.HasValue && SsdHeader.EntryRefId > 0)
            {
                var psd = await _context.Entries
                    .FirstOrDefaultAsync(e => e.EntryId == SsdHeader.EntryRefId.Value && e.EntryType == "PSD" && e.UcpId == activeUcpId);

                if (psd != null)
                {
                    string supplierName = "Unknown";
                    if (!string.IsNullOrEmpty(psd.EntryAttribute1) && long.TryParse(psd.EntryAttribute1, out long suppId))
                    {
                        var supp = await _context.SupplierMasters.FirstOrDefaultAsync(s => s.SupplierId == suppId && s.UcpId == activeUcpId);
                        if (supp != null) supplierName = supp.SupplierName ?? "Unknown";
                    }

                    ParentPsd = new LinkedPsdViewModel
                    {
                        PsdId = psd.EntryId,
                        PsdNumber = psd.EntryNumber,
                        PsdDate = psd.EntryDate,
                        SupplierName = supplierName,
                        Qty = psd.Qty,
                        Lbs = psd.Lbs,
                        Rate = psd.Rate,
                        Amount = psd.Amount
                    };
                }
            }

            // 1. SSD Contract Items
            var ssdlRaw = await (from ssdl in _context.Entries
                                 join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId)
                                 on ssdl.ItemId equals item.ItemId into itemJoin
                                 from item in itemJoin.DefaultIfEmpty()
                                 where ssdl.EntryRefId == id && ssdl.EntryType == "SSDL" && ssdl.UcpId == activeUcpId
                                 orderby ssdl.EntryId
                                 select new SsdContractItemViewModel
                                 {
                                     EntryId = ssdl.EntryId,
                                     ItemId = ssdl.ItemId,
                                     ItemName = item != null ? item.ItemName : "Unknown Item",
                                     Qty = ssdl.Qty ?? 0,
                                     Lbs = ssdl.Lbs ?? 0,
                                     Rate = ssdl.Rate ?? 0,
                                     Amount = ssdl.Amount ?? 0,
                                     TaxAmount = ssdl.TaxAmount ?? 0,
                                     NetAmount = (ssdl.Amount ?? 0) + (ssdl.TaxAmount ?? 0)
                                 }).ToListAsync();

            if (ssdlRaw.Count == 0 && (SsdHeader.Qty.HasValue || SsdHeader.Amount.HasValue))
            {
                string itemName = "Standard Yarn";
                if (SsdHeader.ItemId.HasValue)
                {
                    var it = await _context.ItemMasters.FirstOrDefaultAsync(i => i.ItemId == SsdHeader.ItemId.Value && i.UcpId == activeUcpId);
                    if (it != null) itemName = it.ItemName;
                }
                ssdlRaw.Add(new SsdContractItemViewModel
                {
                    EntryId = SsdHeader.EntryId,
                    ItemId = SsdHeader.ItemId,
                    ItemName = itemName,
                    Qty = SsdHeader.Qty ?? 0,
                    Lbs = SsdHeader.Lbs ?? 0,
                    Rate = SsdHeader.Rate ?? 0,
                    Amount = SsdHeader.Amount ?? 0,
                    TaxAmount = SsdHeader.TaxAmount ?? 0,
                    NetAmount = (SsdHeader.Amount ?? 0) + (SsdHeader.TaxAmount ?? 0)
                });
            }

            ContractItems = ssdlRaw;

            // 2. Sales Invoices Delivered against this SSD (SAL & SALL)
            DeliveryItems = await (from sal in _context.Entries
                                   join sall in _context.Entries on sal.EntryId equals sall.EntryRefId
                                   join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId) on sall.ItemId equals item.ItemId into itemJoin
                                   from item in itemJoin.DefaultIfEmpty()
                                   where sal.EntryType == "SAL" && sall.EntryType == "SALL" &&
                                         sal.UcpId == activeUcpId && sall.UcpId == activeUcpId &&
                                         sal.EntryRefId == id
                                   orderby sal.EntryDate, sal.EntryNumber, sall.EntryId
                                   select new SalesDeliveryItemViewModel
                                   {
                                       SalId = sal.EntryId,
                                       SalNumber = sal.EntryNumber,
                                       SalDate = sal.EntryDate,
                                       Remarks = sal.EntryRemarks,
                                       IsCommissionBased = (sal.EntryAttribute5 != null && sal.EntryAttribute5.ToLower() == "yes") || ((sal.EntryCommAmt ?? 0) > 0),
                                       ItemId = sall.ItemId,
                                       ItemName = item != null ? item.ItemName : "Unknown Item",
                                       Qty = sall.Qty ?? 0,
                                       Lbs = sall.Lbs ?? 0,
                                       Rate = sall.Rate ?? 0,
                                       Amount = sall.Amount ?? 0,
                                       TaxAmount = sall.TaxAmount ?? 0,
                                       NetAmount = (sall.Amount ?? 0) + (sall.TaxAmount ?? 0)
                                   }).ToListAsync();

            // 2b. Sales Invoices Returned against this SSD (CN & CNL)
            var salIds = DeliveryItems.Select(d => d.SalId).Distinct().ToList();
            if (salIds.Any())
            {
                ReturnItems = await (from cn in _context.Entries
                                     join cnl in _context.Entries on cn.EntryId equals cnl.EntryRefId
                                     join item in _context.ItemMasters.Where(im => im.UcpId == activeUcpId) on cnl.ItemId equals item.ItemId into itemJoin
                                     from item in itemJoin.DefaultIfEmpty()
                                     where cn.EntryType == "CN" && cnl.EntryType == "CNL" &&
                                           cn.UcpId == activeUcpId && cnl.UcpId == activeUcpId &&
                                           cn.EntryRefId.HasValue && salIds.Contains(cn.EntryRefId.Value)
                                     orderby cn.EntryDate, cn.EntryNumber, cnl.EntryId
                                     select new SaleReturnItemViewModel
                                     {
                                         CnId = cn.EntryId,
                                         CnNumber = cn.EntryNumber,
                                         CnDate = cn.EntryDate,
                                         AgainstSalNumber = cn.EntryRefNo,
                                         Remarks = cn.EntryRemarks,
                                         ItemId = cnl.ItemId,
                                         ItemName = item != null ? item.ItemName : "Unknown Item",
                                         Qty = cnl.Qty ?? 0,
                                         Lbs = cnl.Lbs ?? 0,
                                         Rate = cnl.Rate ?? 0,
                                         Amount = cnl.Amount ?? 0,
                                         TaxAmount = cnl.TaxAmount ?? 0,
                                         NetAmount = (cnl.Amount ?? 0) + (cnl.TaxAmount ?? 0)
                                     }).ToListAsync();
            }

            // 3. Populate Movement Summary
            Summary = new SsdMovementSummaryViewModel
            {
                ContractQty = ContractItems.Sum(x => x.Qty),
                ContractLbs = ContractItems.Sum(x => x.Lbs),
                ContractAmount = ContractItems.Sum(x => x.Amount),

                DeliveredQty = DeliveryItems.Sum(x => x.Qty),
                DeliveredLbs = DeliveryItems.Sum(x => x.Lbs),
                DeliveredAmount = DeliveryItems.Sum(x => x.Amount),

                ReturnedQty = ReturnItems.Sum(x => x.Qty),
                ReturnedLbs = ReturnItems.Sum(x => x.Lbs),
                ReturnedAmount = ReturnItems.Sum(x => x.Amount),

                LinkedPsdQty = ParentPsd?.Qty ?? 0,
                LinkedPsdLbs = ParentPsd?.Lbs ?? 0,
                LinkedPsdAmount = ParentPsd?.Amount ?? 0
            };

            return Page();
        }
    }
}
