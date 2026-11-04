using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;







namespace YIS.Pages.Configuration.UserRoles



{



    public class IndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public IndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<UserRole> UserRoles { get; set; } = default!;







        [BindProperty(SupportsGet = true)]



        public int CurrentPage { get; set; } = 1;







        [BindProperty(SupportsGet = true)]



        public int PageSize { get; set; } = 10;







        [BindProperty(SupportsGet = true)]



        public string? SearchTerm { get; set; }







        public PaginationViewModel Pagination { get; set; } = default!;







        public async Task OnGetAsync()



        {



            // Set breadcrumb



            ViewData["BreadcrumbParent"] = "Configuration";



            ViewData["BreadcrumbParentUrl"] = "#";



            



            if (_context.UserRoles != null)



            {



                var query = _context.UserRoles.AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(r => r.UrName.ToLower().Contains(term));



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                UserRoles = await query



                    .OrderBy(r => r.UrName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            if (_context.UserRoles == null)



            {



                return NotFound();



            }



            var role = await _context.UserRoles.FindAsync(id);







            if (role != null)



            {



                _context.UserRoles.Remove(role);



                await _context.SaveChangesAsync();



                TempData["StatusMessage"] = $"Role '{role.UrName}' deleted successfully.";



            }







            return RedirectToPage("./Index");



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            if (_context.UserRoles == null) return RedirectToPage();

            var query = _context.UserRoles.AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(r => r.UrName.ToLower().Contains(term));
            }

            var list = await query.OrderBy(r => r.UrName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Role ID", x => x.Id, null, XLAlignmentHorizontalValues.Center),
                new("Role Name", x => x.Name)
            };

            var exportList = list.Select(r => new
            {
                Id = r.UrId,
                Name = r.UrName
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("User Roles", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"User_Roles_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
