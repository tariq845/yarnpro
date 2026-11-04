using YIS.Services;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc;



using Microsoft.AspNetCore.Mvc.RazorPages;



using Microsoft.EntityFrameworkCore;



using YIS.Data;



using YIS.Models;



using YIS.Helpers;







namespace YIS.Pages.Items



{



    public class IndexModel : PageModel



    {



        private readonly ApplicationDbContext _context;







        public IndexModel(ApplicationDbContext context)



        {



            _context = context;



        }







        public IList<ItemMaster> Items { get; set; } = default!;







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







            if (_context.ItemMasters != null)



            {



                var query = _context.ItemMasters



                    .Where(i => i.UcpId == activeUcpId)



                    .AsQueryable();







                if (!string.IsNullOrEmpty(SearchTerm))



                {



                    var term = SearchTerm.ToLower();



                    query = query.Where(i =>



                        (i.ItemName != null && i.ItemName.ToLower().Contains(term)) ||



                        (i.ItemStCode != null && i.ItemStCode.ToString().Contains(term)) ||



                        (i.ItemHsCode != null && i.ItemHsCode.Contains(term))



                    );



                }







                if (PageSize <= 0) PageSize = 10;



                int totalItems = await query.CountAsync();



                Pagination = new PaginationViewModel(totalItems, CurrentPage, PageSize);







                Items = await query



                    .OrderBy(i => i.ItemName)



                    .Skip((CurrentPage - 1) * PageSize)



                    .Take(PageSize)



                    .ToListAsync();



            }



        }







        public async Task<IActionResult> OnPostDeleteAsync(long id)



        {



            if (_context.ItemMasters == null)



            {



                return NotFound();



            }



            var item = await _context.ItemMasters.FindAsync(id);







            if (item != null)



            {



                _context.ItemMasters.Remove(item);



                await _context.SaveChangesAsync();



                TempData["StatusMessage"] = $"Item '{item.ItemName}' deleted successfully.";



            }







            return RedirectToPage("./Index");



        }



                public async Task<IActionResult> OnGetDownloadExcelAsync()
        {
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue || _context.ItemMasters == null) return RedirectToPage();

            var query = _context.ItemMasters
                .Where(i => i.UcpId == activeUcpId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(i =>
                    (i.ItemName != null && i.ItemName.ToLower().Contains(term)) ||
                    (i.ItemStCode != null && i.ItemStCode.ToString().Contains(term)) ||
                    (i.ItemHsCode != null && i.ItemHsCode.Contains(term))
                );
            }

            var items = await query.OrderBy(i => i.ItemName).ToListAsync();

            var columns = new List<ExcelColumn<dynamic>>
            {
                new("Item Name", x => x.Name),
                new("UOM", x => x.Uom, null, XLAlignmentHorizontalValues.Center),
                new("ST Code", x => x.StCode, null, XLAlignmentHorizontalValues.Center),
                new("HS Code", x => x.HsCode, null, XLAlignmentHorizontalValues.Center),
                new("Status", x => x.Status, null, XLAlignmentHorizontalValues.Center)
            };

            var exportList = items.Select(i => new
            {
                Name = i.ItemName ?? "-",
                Uom = i.Uom ?? "-",
                StCode = i.ItemStCode?.ToString() ?? "-",
                HsCode = i.ItemHsCode ?? "-",
                Status = i.ItemDeactiveOn == null ? "Active" : "Inactive"
            }).ToList();

            var bytes = ExcelExportHelper.ExportToExcel("Item Catalog", columns, exportList, filterInfo: SearchTerm);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Items_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
