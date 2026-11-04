using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;



using YIS.Helpers;







namespace YIS.Pages.Customers



{



    public class IndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public IndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<CustomerMaster> Customers { get; set; } = default!;







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







            if (_context.CustomerMasters != null)



            {



                var query = _context.CustomerMasters



                    .Where(c => c.UcpId == activeUcpId)



                    .Include(c => c.Account)



                    .AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(c =>



                        c.CustomerName.ToLower().Contains(term) ||



                        (c.CustomerContact != null && c.CustomerContact.Contains(term)) ||



                        (c.CustomerEmail != null && c.CustomerEmail.ToLower().Contains(term)) ||



                        (c.CustomerNtnNo != null && c.CustomerNtnNo.Contains(term)) ||



                        (c.CustomerCnic != null && c.CustomerCnic.Contains(term))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                Customers = await query



                    .OrderBy(c => c.CustomerName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            if (_context.CustomerMasters == null)



            {



                return NotFound();



            }



            var customer = await _context.CustomerMasters.FindAsync(id);







            if (customer != null)



            {



                _context.CustomerMasters.Remove(customer);



                await _context.SaveChangesAsync();



                TempData["StatusMessage"] = $"Customer '{customer.CustomerName}' deleted successfully.";



            }







            return RedirectToPage("./Index");



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.CustomerMasters == null) return RedirectToPage();

            var query = _context.CustomerMasters
                .Where(c => c.UcpId == activeUcpId)
                .Include(c => c.Account)
                .AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(c =>
                    c.CustomerName.ToLower().Contains(term) ||
                    (c.CustomerContact != null && c.CustomerContact.Contains(term)) ||
                    (c.CustomerEmail != null && c.CustomerEmail.ToLower().Contains(term)) ||
                    (c.CustomerNtnNo != null && c.CustomerNtnNo.Contains(term)) ||
                    (c.CustomerCnic != null && c.CustomerCnic.Contains(term))
                );
            }

            var customers = await query.OrderBy(c => c.CustomerName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Customer Name", x => x.Name),
                new("Contact", x => x.Contact),
                new("Email", x => x.Email),
                new("NTN No", x => x.Ntn),
                new("STRN", x => x.Strn),
                new("Address", x => x.Address),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var exportList = customers.Select(c => new
            {
                Name = c.CustomerName,
                Contact = c.CustomerContact ?? "-",
                Email = c.CustomerEmail ?? "-",
                Ntn = c.CustomerNtnNo ?? "-",
                Strn = c.CustomerStrNo ?? "-",
                Address = c.CustomerAddress ?? "-",
                Status = c.CustomerDeactiveOn == null ? "Active" : "Inactive"
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("Customers Directory", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Customers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
