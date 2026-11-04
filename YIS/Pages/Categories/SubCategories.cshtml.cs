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



    public class SubCategoriesModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public SubCategoriesModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<SubCategory> SubCategories { get; set; } = default!;







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







            if (_context.SubCategories != null)



            {



                var query = _context.SubCategories



                    .Where(s => s.UcpId == activeUcpId)



                    .Include(s => s.MainCategory)



                    .AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(s =>



                        s.SCategoryName.ToLower().Contains(term) ||



                        (s.MainCategory != null && s.MainCategory.MCategoryName.ToLower().Contains(term))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                SubCategories = await query



                    .OrderBy(s => s.SCategoryName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            if (_context.SubCategories == null)



            {



                return NotFound();



            }



            var subCategory = await _context.SubCategories.FindAsync(id);







            if (subCategory != null)



            {



                _context.SubCategories.Remove(subCategory);



                await _context.SaveChangesAsync();



                TempData["StatusMessage"] = $"Sub Category '{subCategory.SCategoryName}' deleted successfully.";



            }







            return RedirectToPage("./SubCategories");



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.SubCategories == null) return RedirectToPage();

            var query = _context.SubCategories
                .Where(sc => sc.UcpId == activeUcpId)
                .Include(sc => sc.MainCategory)
                .AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(s =>
                    s.SCategoryName.ToLower().Contains(term) ||
                    (s.MainCategory != null && s.MainCategory.MCategoryName.ToLower().Contains(term))
                );
            }

            var list = await query.OrderBy(sc => sc.SCategoryName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Sub Category Name", x => x.Name),
                new("Main Category", x => x.MainCategory)
            };

            var exportList = list.Select(sc => new
            {
                Name = sc.SCategoryName,
                MainCategory = sc.MainCategory?.MCategoryName ?? "-"
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("Sub Categories", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Sub_Categories_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
