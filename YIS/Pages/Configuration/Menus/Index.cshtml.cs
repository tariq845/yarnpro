using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;







namespace YIS.Pages.Configuration.Menus



{



    public class IndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public IndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<MenuOption> Menus { get; set; } = default!;







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







            if (_context.MenuOptions != null)



            {



                var query = _context.MenuOptions.AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(m => 



                        m.MoName.ToLower().Contains(term) ||



                        (m.MoUrl != null && m.MoUrl.ToLower().Contains(term))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                Menus = await query



                    .OrderBy(m => m.MoName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            if (_context.MenuOptions == null) return RedirectToPage();

            var query = _context.MenuOptions.AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(m =>
                    m.MoName.ToLower().Contains(term) ||
                    (m.MoUrl != null && m.MoUrl.ToLower().Contains(term))
                );
            }

            var list = await query.OrderBy(m => m.MoName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Menu ID", x => x.Id, null, XLAlignmentHorizontalValues.Center),
                new("Menu Name", x => x.Title),
                new("Type", x => x.Type, null, XLAlignmentHorizontalValues.Center),
                new("URL", x => x.Url),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var exportList = list.Select(m => new
            {
                Id = m.MoId,
                Title = m.MoName,
                Type = m.MoType ?? "-",
                Url = m.MoUrl ?? "-",
                Status = m.MoDeactiveOn == null ? "Active" : "Inactive"
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("System Menus", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Menus_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
