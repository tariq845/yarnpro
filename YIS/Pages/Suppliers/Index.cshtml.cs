using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;



using YIS.Helpers;







namespace YIS.Pages.Suppliers



{



    public class IndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public IndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<SupplierMaster> Suppliers { get; set; } = default!;







        [BindProperty(SupportsGet = true)]



        public int CurrentPage { get; set; } = 1;







        [BindProperty(SupportsGet = true)]



        public int PageSize { get; set; } = 10;







        [BindProperty(SupportsGet = true)]



        public string? SearchTerm { get; set; }







        public PaginationViewModel Pagination { get; set; } = default!;







        public async Task OnGetAsync()



        {



            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);







            if (_context.SupplierMasters != null)



            {



                var query = _context.SupplierMasters



                    .Where(s => s.UcpId == activeUcpId)



                    .Include(s => s.Account)



                    .AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(s =>



                        s.SupplierName.ToLower().Contains(term) ||



                        (s.SupplierContact != null && s.SupplierContact.Contains(term)) ||



                        (s.SupplierEmail != null && s.SupplierEmail.ToLower().Contains(term)) ||



                        (s.SupplierNtnNo != null && s.SupplierNtnNo.Contains(term)) ||



                        (s.SupplierCnic != null && s.SupplierCnic.Contains(term))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                Suppliers = await query



                    .OrderBy(s => s.SupplierName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            if (_context.SupplierMasters == null)



            {



                return NotFound();



            }



            var supplier = await _context.SupplierMasters.FindAsync(id);







            if (supplier != null)



            {



                _context.SupplierMasters.Remove(supplier);



                await _context.SaveChangesAsync();



                TempData["StatusMessage"] = $"Supplier '{supplier.SupplierName}' deleted successfully.";



            }







            return RedirectToPage("./Index");



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.SupplierMasters == null) return RedirectToPage();

            var query = _context.SupplierMasters
                .Where(s => s.UcpId == activeUcpId)
                .Include(s => s.Account)
                .AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(s =>
                    s.SupplierName.ToLower().Contains(term) ||
                    (s.SupplierContact != null && s.SupplierContact.Contains(term)) ||
                    (s.SupplierEmail != null && s.SupplierEmail.ToLower().Contains(term)) ||
                    (s.SupplierNtnNo != null && s.SupplierNtnNo.Contains(term)) ||
                    (s.SupplierCnic != null && s.SupplierCnic.Contains(term))
                );
            }

            var suppliers = await query.OrderBy(s => s.SupplierName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Supplier Name", x => x.Name),
                new("Contact", x => x.Contact),
                new("Email", x => x.Email),
                new("NTN No", x => x.Ntn),
                new("STRN", x => x.Strn),
                new("Address", x => x.Address),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var exportList = suppliers.Select(s => new
            {
                Name = s.SupplierName,
                Contact = s.SupplierContact ?? "-",
                Email = s.SupplierEmail ?? "-",
                Ntn = s.SupplierNtnNo ?? "-",
                Strn = s.SupplierStrNo ?? "-",
                Address = s.SupplierAddress ?? "-",
                Status = s.SupplierDeactiveOn == null ? "Active" : "Inactive"
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("Suppliers Directory", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Suppliers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
