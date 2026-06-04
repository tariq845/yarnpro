using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Pages.Items
{
    public class CreateItemModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateItemModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public ItemMaster Item { get; set; } = default!;

        public string? Id { get; set; }

        public async Task<IActionResult> OnGetAsync(long? id)
        {
            if (id == null)
            {
                // Create mode
                Item = new ItemMaster { ItemId = 0 };
                return Page();
            }

            // Edit mode
            var item = await _context.ItemMasters.FindAsync(id);

            if (item == null)
            {
                return NotFound();
            }

            Item = item;
            Id = id.ToString();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                TempData["ErrorMessage"] = "Validation failed: " + string.Join(" | ", errors);
                return Page();
            }

            // Get Active Company Profile from Session
            var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
            if (!activeUcpId.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot save: No company selected. Please select a company first.";
                return RedirectToPage("/SelectCompany");
            }

            if (Item.ItemId == 0)
            {
                // New Item
                Item.CreationOn = DateTime.Now;
                Item.UcpId = activeUcpId;
                _context.ItemMasters.Add(Item);
                TempData["StatusMessage"] = $"Item '{Item.ItemName}' created successfully.";
            }
            else
            {
                // Update Item
                var existingItem = await _context.ItemMasters.FindAsync(Item.ItemId);
                if (existingItem == null)
                {
                    return NotFound();
                }

                existingItem.ItemName = Item.ItemName;
                existingItem.ItemStCode = Item.ItemStCode;
                existingItem.ItemHsCode = Item.ItemHsCode;
                existingItem.Uom = Item.Uom;
                existingItem.ItemDeactiveOn = Item.ItemDeactiveOn;
                existingItem.UcpId = activeUcpId;
                existingItem.LastUpdateOn = DateTime.Now;

                _context.ItemMasters.Update(existingItem);
                TempData["StatusMessage"] = $"Item '{existingItem.ItemName}' updated successfully.";
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
        public async Task<IActionResult> OnGetUomByHsCodeAsync(string hsCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hsCode))
                    return new JsonResult(new { success = false, message = "HS Code is empty" });

                // Get Active Company Profile from Session
                var activeUcpId = AuthHelper.GetActiveCompanyId(HttpContext);
                if (!activeUcpId.HasValue)
                {
                    return new JsonResult(new { success = false, message = "No active company selected." });
                }

                // Retrieve UserCompanyProfile to get tokens
                var ucp = await _context.UserCompanyProfiles
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(u => u.UcpId == activeUcpId.Value);

                if (ucp == null)
                    return new JsonResult(new { success = false, message = "Company profile not found" });

                bool useProduction = ucp.IsProductionToken;
                string fbrToken = useProduction
                    ? (ucp.UcpProductionToken?.Trim() ?? "")
                    : (ucp.UcpSandboxToken?.Trim() ?? "");

                if (string.IsNullOrEmpty(fbrToken))
                    return new JsonResult(new { success = false, message = "FBR token not configured" });

                string formattedHsCode = hsCode.Trim();
                string apiUrl = $"https://gw.fbr.gov.pk/pdi/v2/HS_UOM?hs_code={Uri.EscapeDataString(formattedHsCode)}&annexure_id=3";

                // _logger.LogInformation("Fetching UOM for HS Code: {HSCode} | URL: {ApiUrl}", formattedHsCode, apiUrl);

                using (var handler = new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                              System.Net.DecompressionMethods.Deflate |
                                              System.Net.DecompressionMethods.Brotli
                })
                using (var client = new HttpClient(handler)) // Use manual HttpClient with handler
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fbrToken);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                    var response = await client.GetAsync(apiUrl);
                    string json = await response.Content.ReadAsStringAsync();

                    // _logger.LogInformation("FBR Response for {HSCode}: {StatusCode} | Body: {Json}", formattedHsCode, response.StatusCode, json);

                    if (!response.IsSuccessStatusCode)
                    {
                        return new JsonResult(new { 
                            success = false, 
                            message = $"FBR API returned {response.StatusCode}",
                            debug_info = json 
                        });
                    }

                    var arr = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JArray>(json);

                    if (arr == null || arr.Count == 0)
                    {
                        return new JsonResult(new { 
                            success = false, 
                            message = "No UOM found in FBR database for this HS Code",
                            raw_response = json
                        });
                    }

                    var uomList = new List<string>();
                    foreach (var item in arr)
                    {
                        string uomVal = item["description"]?.ToString() 
                                      ?? item["uoM_ID"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(uomVal) && !uomList.Contains(uomVal))
                        {
                            uomList.Add(uomVal);
                        }
                    }

                    if (uomList.Count == 0)
                    {
                        return new JsonResult(new { 
                            success = false, 
                            message = "UOM data found but description is empty",
                            raw_data = arr.First()?.ToString()
                        });
                    }

                    return new JsonResult(new { success = true, uoms = uomList });
                }
            }
            catch (Exception ex)
            {
                // _logger.LogError(ex, "Error fetching UOM for HS Code {HSCode}", hsCode);
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }
    }
}
