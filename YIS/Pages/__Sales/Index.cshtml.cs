using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace YIS.Pages.Sales
{
    public class SalesModel : PageModel
    {
        public void OnGet()
        {
        }

        // HTMX Handlers
        public IActionResult OnGetList()
        {
            // Return list of sales
            return Partial("_SalesTableRows");
        }

        public IActionResult OnGetSearch(string query)
        {
            // Filter sales by search query
            return Partial("_SalesTableRows");
        }

        public IActionResult OnGetFilter(string status, DateTime? date)
        {
            // Filter sales by status and date
            return Partial("_SalesTableRows");
        }

        public IActionResult OnGetCreate()
        {
            // Return create form
            return Partial("_CreateSale");
        }
    }
}
