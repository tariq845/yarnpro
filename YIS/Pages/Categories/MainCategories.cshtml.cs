using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;



using YIS.Helpers;







namespace YIS.Pages.Categories



{



    public class MainCategoriesModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public MainCategoriesModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<MainCategory> MainCategories { get; set; } = default!;







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







            if (_context.MainCategories != null)



            {



                var query = _context.MainCategories



                    .Where(mc => mc.UcpId == activeUcpId)



                    .AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(mc => mc.MCategoryName.ToLower().Contains(term));



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                MainCategories = await query



                    .OrderBy(mc => mc.MCategoryName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            if (_context.MainCategories == null)



            {



                return NotFound();



            }



            var mainCategory = await _context.MainCategories.FindAsync(id);







            if (mainCategory != null)



            {



                _context.MainCategories.Remove(mainCategory);



                await _context.SaveChangesAsync();



                TempData["StatusMessage"] = $"Main Category '{mainCategory.MCategoryName}' deleted successfully.";



            }







            return RedirectToPage("./MainCategories");



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.MainCategories == null) return RedirectToPage();

            var query = _context.MainCategories
                .Where(mc => mc.UcpId == activeUcpId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(mc => mc.MCategoryName.ToLower().Contains(term));
            }

            var list = await query.OrderBy(mc => mc.MCategoryName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Main Category Name", x => x.Name)
            };

            var exportList = list.Select(mc => new
            {
                Name = mc.MCategoryName
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("Main Categories", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Main_Categories_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
