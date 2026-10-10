using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Pages.Sellers
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public SellerMaster Seller { get; set; } = default!;

        public bool IsEditMode { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // Check if a seller already exists
            var existingSeller = await _context.SellerMasters.FirstOrDefaultAsync();
            
            if (existingSeller != null)
            {
                // Edit mode - load the existing seller
                IsEditMode = true;
                Seller = existingSeller;
            }
            else
            {
                // Create mode
                IsEditMode = false;
                Seller = new SellerMaster();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var currentUser = HttpContext.Session.GetString("UserName") ?? "System";
            var existingSeller = await _context.SellerMasters.FirstOrDefaultAsync();

            if (existingSeller != null)
            {
                // Update existing seller
                existingSeller.SellerName = Seller.SellerName;
                existingSeller.SellerAddress = Seller.SellerAddress;
                existingSeller.SellerContact = Seller.SellerContact;
                existingSeller.SellerEmail = Seller.SellerEmail;
                existingSeller.LastUpdatedBy = currentUser;
                existingSeller.LastUpdateOn = DateTime.Now;

                TempData["StatusMessage"] = $"Seller '{Seller.SellerName}' updated successfully.";
            }
            else
            {
                // Create new seller (only if none exists)
                Seller.CreatedBy = currentUser;
                Seller.CreationOn = DateTime.Now;

                _context.SellerMasters.Add(Seller);
                TempData["StatusMessage"] = $"Seller '{Seller.SellerName}' created successfully.";
            }

            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
    }
}
