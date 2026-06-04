using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YIS.Data;
using YIS.Models;
using YIS.Helpers;

namespace YIS.Services
{
    public class FbrValidationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FbrValidationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _sandboxApiUrl;
        private readonly string _productionApiUrl;

        // Add this for cleaner JSON output (camelCase as expected by FBR)
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public FbrValidationService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<FbrValidationService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
            _sandboxApiUrl = _configuration["FBR:SandboxApiUrl"] ?? "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata_sb";
            _productionApiUrl = _configuration["FBR:ProductionApiUrl"] ?? "https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata";
        }

        /// <summary>
        /// Query FBR to retrieve DI number for a submitted invoice
        /// </summary>
        public async Task<string?> QueryDiNumberAsync(long entryId)
        {
            try
            {
                var entry = await _context.Entries
                    .FirstOrDefaultAsync(e => e.EntryId == entryId && e.EntryType == "SAL");

                if (entry == null)
                {
                    _logger.LogWarning("Invoice SAL-{No} not found", entry.EntryNumber);
                    return null;
                }

                var ucp = await _context.UserCompanyProfiles
                    .FirstOrDefaultAsync(u => u.UcpId == entry.UcpId);

                if (ucp == null)
                {
                    _logger.LogWarning("Company profile not found for invoice SAL-{No}", entry.EntryNumber);
                    return null;
                }

                bool useProduction = ucp.IsProductionToken;
                var fbrToken = useProduction
                    ? (ucp.UcpProductionToken?.Trim() ?? "")
                    : (ucp.UcpSandboxToken?.Trim() ?? "");

                if (string.IsNullOrWhiteSpace(fbrToken))
                {
                    _logger.LogWarning("FBR token not found for invoice SAL-{No}", entry.EntryNumber);
                    return null;
                }

                // Try common FBR query endpoints
                var baseUrl = useProduction
                    ? "https://gw.fbr.gov.pk/di_data/v1/di"
                    : "https://gw.fbr.gov.pk/di_data/v1/di";

                string invoiceRefNo = "SAL-" + entry.EntryNumber;

                var queryEndpoints = new[]
                {
                    $"{baseUrl}/getinvoicedata?invoiceRefNo={Uri.EscapeDataString(invoiceRefNo)}",
                    $"{baseUrl}/getinvoice?invoiceRefNo={Uri.EscapeDataString(invoiceRefNo)}",
                    $"{baseUrl}/queryinvoice?referenceNumber={Uri.EscapeDataString(invoiceRefNo)}",
                    $"{baseUrl}/getinvoicedata_sb?invoiceRefNo={Uri.EscapeDataString(invoiceRefNo)}", // Sandbox variant
                };

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fbrToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Add scenarioId header for sandbox testing only
                if (!useProduction)
                {
                    client.DefaultRequestHeaders.Add("scenarioId", "SN001");
                    _logger.LogInformation("Added scenarioId header: SN001 for sandbox query");
                }

                foreach (var endpoint in queryEndpoints)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to query DI number from: {Endpoint}", endpoint);
                        var response = await client.GetAsync(endpoint);
                        var responseContent = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(responseContent))
                        {
                            _logger.LogInformation("Query response received: {Content}", responseContent.Substring(0, Math.Min(500, responseContent.Length)));

                            // Try to parse DI number from response
                            if (responseContent.TrimStart().StartsWith("{"))
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(responseContent);
                                    var root = doc.RootElement;

                                    // Try to extract DI number
                                    var diNo = ExtractDiNumberFromJson(root);
                                    if (!string.IsNullOrWhiteSpace(diNo))
                                    {
                                        _logger.LogInformation("✓ DI Number found via query: {DiNo}", diNo);
                                        return diNo.Trim();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to parse query response as JSON");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Query endpoint {Endpoint} failed, trying next", endpoint);
                    }
                }

                _logger.LogWarning("Could not retrieve DI number from any query endpoint for invoice SAL-{No}", entry.EntryNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying DI number for invoice SAL-{Id}", entryId);
                return null;
            }
        }

        private string? ExtractDiNumberFromJson(JsonElement element)
        {
            var fieldNames = new[] { "diNo", "diNumber", "fbrDiNo", "invoiceNumber", "digitalInvoiceNumber",
                                     "fbrInvoiceNumber", "di", "invoiceNo", "referenceNumber", "refNo",
                                     "digitalInvoiceNo", "fbrDi", "invoiceRefNo", "data", "result" };

            foreach (var fieldName in fieldNames)
            {
                if (element.TryGetProperty(fieldName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String)
                    {
                        var value = prop.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            return value.Trim();
                    }
                    else if (prop.ValueKind == JsonValueKind.Object)
                    {
                        var nested = ExtractDiNumberFromJson(prop);
                        if (!string.IsNullOrWhiteSpace(nested))
                            return nested;
                    }
                }
            }

            return null;
        }

        public async Task ValidatePendingInvoicesAsync()
        {
            _logger.LogInformation("===== FBR Send Service started at {Time} =====", DateTime.Now);
            var pendingEntries = await _context.Entries
                .Where(e => e.EntryType == "SAL" &&
                           (e.EntryPostedToFbr != true || string.IsNullOrEmpty(e.EntryFbrDiNo)))
                .OrderBy(e => e.EntryDate)
                .ToListAsync();

            foreach (var entry in pendingEntries)
            {
                await ValidateSingleInvoiceAsync(entry.EntryId);
            }

            _logger.LogInformation("===== FBR Send Service finished at {Time} =====", DateTime.Now);
        }

        public async Task<bool> ValidateSingleInvoiceAsync(long entryId)
        {
            var entry = await _context.Entries
                .FirstOrDefaultAsync(e => e.EntryId == entryId && e.EntryType == "SAL");

            if (entry == null)
            {
                _logger.LogWarning("Invoice with ID {Id} not found", entryId);
                return false;
            }

            return await ValidateSingleInvoiceAsync(entry);
        }

        private async Task<bool> ValidateSingleInvoiceAsync(Entry entry)
        {
            try
            {
                // Check if invoice has already been successfully sent to FBR
                if (entry.EntryPostedToFbr == true && !string.IsNullOrWhiteSpace(entry.EntryFbrDiNo))
                {
                    _logger.LogInformation("Invoice SAL-{No} has already been sent to FBR. DI No: {DiNo}",
                        entry.EntryNumber, entry.EntryFbrDiNo);
                    return true;
                }

                // If posted is explicitly FALSE, allow re-sending
                if (entry.EntryPostedToFbr == false)
                {
                    _logger.LogInformation("Re-sending invoice SAL-{No} to FBR (previous attempt failed)", entry.EntryNumber);
                }

                // Get Line Items
                var lineItems = await _context.Entries
                    .Where(e => e.EntryRefId == entry.EntryId && e.EntryType == "SALL")
                    .ToListAsync();

                if (!lineItems.Any())
                {
                    _logger.LogWarning("Invoice SAL-{No} has no products", entry.EntryNumber);
                    entry.EntryPostedToFbr = false;
                    entry.EntryFbrError = "No products found in invoice";
                    entry.LastUpdateOn = DateTime.Now;
                    await _context.SaveChangesAsync();
                    return false;
                }

                // Get Company Profile
                var ucp = await _context.UserCompanyProfiles
                    .Include(u => u.Seller)
                    .FirstOrDefaultAsync(u => u.UcpId == entry.UcpId);

                if (ucp == null)
                {
                    _logger.LogError("Company profile not found for invoice SAL-{No}", entry.EntryNumber);
                    entry.EntryPostedToFbr = false;
                    entry.EntryFbrError = "Company profile mapping missing";
                    entry.LastUpdateOn = DateTime.Now;
                    await _context.SaveChangesAsync();
                    return false;
                }

                bool useProduction = ucp.IsProductionToken;
                string fbrToken = useProduction
                    ? (ucp.UcpProductionToken?.Trim() ?? "")
                    : (ucp.UcpSandboxToken?.Trim() ?? "");

                string apiUrl = useProduction ? _productionApiUrl : _sandboxApiUrl;

                _logger.LogInformation("Using FBR {Environment} Environment - API URL: {ApiUrl}",
                    useProduction ? "PRODUCTION" : "SANDBOX", apiUrl);

                if (string.IsNullOrEmpty(fbrToken))
                {
                    _logger.LogError("FBR token not found for seller {SellerId}. UseProduction: {UseProd}", ucp.SellerId, useProduction);
                    entry.EntryPostedToFbr = false;
                    entry.EntryFbrError = $"FBR {(useProduction ? "Production" : "Sandbox")} token not configured";
                    entry.LastUpdateOn = DateTime.Now;
                    await _context.SaveChangesAsync();
                    return false;
                }

                // Get Customer
                CustomerMaster? customer = null;
                if (long.TryParse(entry.EntryAttribute1, out long cid))
                {
                    customer = await _context.CustomerMasters.FindAsync(cid);
                }

                var items = new List<object>();

                foreach (var line in lineItems)
                {
                    var itemMaster = await _context.ItemMasters.FindAsync(line.ItemId);

                    // Note: We are using properties from ItemMaster directly.
                    // If fetching from FBR API is desired dynamically per item based on HS Code, logic could go here,
                    // but usually storing it is better.
                    // string uomFromFbr = ... GetUomFromFbrAsync ... if needed.

                    var item = new
                    {
                        hsCode = itemMaster?.ItemHsCode ?? "0000.00.00",
                        productDescription = CleanText(itemMaster?.ItemName) is { Length: > 0 } pd ? pd : "Unknown Product",
                        rate = (line.EntryAttribute1 ?? "0") + "%",
                        uoM = CleanText(itemMaster?.Uom) is { Length: > 0 } uom ? uom : "Units",
                        //quantity = Math.Round(line.Qty ?? 0, 2),
                        quantity = Math.Round((line.Lbs / 2.2046) ?? 0, 2),
                        totalValues = Math.Round((line.Amount ?? 0) + (line.TaxAmount ?? 0), 2), // Total incl Tax
                        valueSalesExcludingST = Math.Round(line.Amount ?? 0, 2), // Net Amount
                        fixedNotifiedValueOrRetailPrice = 0.0,
                        salesTaxApplicable = Math.Round(line.TaxAmount ?? 0, 2),
                        discount = 0.0,
                        salesTaxWithheldAtSource = 0.0,
                        extraTax = 0.0,
                        furtherTax = 0.0,
                        sroScheduleNo = "",
                        fedPayable = 0.0,
                        saleType = "Goods at standard rate (default)",
                        sroItemSerialNo = "SN001"
                    };

                    items.Add(item);
                    _logger.LogDebug("Added item: HS Code: {HsCode}, Product: {Product}, Quantity: {Qty}, Total: {Total}",
                        item.hsCode, item.productDescription, item.quantity, item.totalValues);
                }

                string invoiceRefNo = "SAL-" + entry.EntryNumber;

                // Build payload
                var payload = new
                {
                    invoiceType = "Sale Invoice",
                    invoiceDate = entry.EntryDate?.ToString("yyyy-MM-dd"),
                    sellerNTNCNIC = FormatFbrId(ucp.UcpCompanyNtn),
                    sellerBusinessName = CleanText(ucp.UcpCompanyName),
                    sellerProvince = CleanText(ucp.UcpCompanyProvince) is { Length: > 0 } sp ? sp : "SINDH",
                    sellerAddress = CleanText(ucp.UcpCompanyAddress),
                    reason = CleanText(entry.EntryRemarks),
                    buyerNTNCNIC = !string.IsNullOrWhiteSpace(customer?.CustomerNtnNo) ? FormatFbrId(customer?.CustomerNtnNo) : FormatFbrId(customer?.CustomerCnic),
                    buyerBusinessName = CleanText(customer?.CustomerName) is { Length: > 0 } bn ? bn : "Unknown Buyer",
                    buyerProvince = CleanText(customer?.Province) is { Length: > 0 } bp ? bp : "SINDH",
                    buyerAddress = CleanText(customer?.CustomerAddress),
                    buyerRegistrationType = CleanText(customer?.RegistrationType) is { Length: > 0 } rt ? rt : "Un-Registered",
                    invoiceRefNo = invoiceRefNo,
                    sourceInvoiceNo= invoiceRefNo,
                    scenarioId = useProduction ? null : "SN001",
                    items
                };

                // Using Newtonsoft.Json here to match user preference/logic where applicable
                string json = JsonConvert.SerializeObject(payload);

                var responseContent = await SendToFbrAsync(json, fbrToken, apiUrl);
                string result = ParseFbrResponse(responseContent, out string? fbrDiNo);
                // Consider only explicit success as valid
                bool isValid = result.Contains("Validated Successfully") ||
                              (responseContent.Contains("Empty response (Status: OK)") && !result.Contains("Error") && !result.Contains("StatusCode:"));

                if (isValid && string.IsNullOrWhiteSpace(fbrDiNo) && (responseContent.Contains("Empty response (Status: OK)") || string.IsNullOrWhiteSpace(responseContent)))
                {
                    _logger.LogInformation("FBR accepted invoice with empty response. Attempting to query DI number separately...");
                    try
                    {
                        var queriedDiNo = await QueryDiNumberAsync(entry.EntryId);
                        if (!string.IsNullOrWhiteSpace(queriedDiNo))
                        {
                            fbrDiNo = queriedDiNo;
                            _logger.LogInformation("✓ Successfully queried FBR DI Number: {DiNo}", fbrDiNo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to query DI number for invoice SAL-{No}", entry.EntryNumber);
                    }
                }

                // Update Entry
                entry.EntryPostedToFbr = isValid;
                if (isValid)
                {
                    entry.EntryFbrError = ""; // Clear error on success
                }
                else
                {
                    entry.EntryFbrError = result.Length > 1000 ? result.Substring(0, 997) + "..." : result;
                }

                if (!string.IsNullOrWhiteSpace(fbrDiNo))
                {
                    entry.EntryFbrDiNo = fbrDiNo.Trim();
                    _logger.LogInformation("✓ FBR DI Number received and stored: {DiNo} for invoice SAL-{No}", fbrDiNo, entry.EntryNumber);
                }
                else if (isValid)
                {
                    _logger.LogWarning("⚠ FBR accepted invoice SAL-{No} but DI number not found in response.", entry.EntryNumber);
                }
                if (entry.EntryFbrError == "Validated Successfully")
                {
                    entry.EntryFbrError = "";
                }
                entry.LastUpdateOn = DateTime.Now;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Invoice SAL-{No} → {Result}", entry.EntryNumber, result);
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed for entry ID: {Id}", entry.EntryId);
                entry.EntryPostedToFbr = false;
                entry.EntryFbrError = $"Error: {ex.Message}".TakeMax(1000);
                entry.LastUpdateOn = DateTime.Now;
                await _context.SaveChangesAsync();
                return false;
            }
        }

        private async Task<string> SendToFbrAsync(string json, string fbrToken, string apiUrl)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip |
                                             DecompressionMethods.Deflate |
                                             DecompressionMethods.Brotli
                };

                using (var httpClient = new HttpClient(handler))
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(500);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fbrToken);
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    _logger.LogInformation("Sending request to FBR API: {ApiUrl}", apiUrl);

                    HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

                    _logger.LogInformation("FBR API Response Status: {StatusCode} {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);

                    string responseContent = await response.Content.ReadAsStringAsync();

                    // Check headers for DI number
                    string? diNoFromHeader = null;
                    if (response.Headers.Contains("X-FBR-DI-No"))
                        diNoFromHeader = string.Join(", ", response.Headers.GetValues("X-FBR-DI-No"));
                    else if (response.Headers.Contains("FBR-DI-No"))
                        diNoFromHeader = string.Join(", ", response.Headers.GetValues("FBR-DI-No"));
                    else if (response.Headers.Contains("DI-No"))
                        diNoFromHeader = string.Join(", ", response.Headers.GetValues("DI-No"));

                    if (!string.IsNullOrWhiteSpace(diNoFromHeader))
                    {
                        _logger.LogInformation("FBR DI Number found in response header: {DiNo}", diNoFromHeader);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        if (!string.IsNullOrWhiteSpace(responseContent))
                        {
                            return responseContent;
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(diNoFromHeader))
                            {
                                return $"Empty response (Status: {response.StatusCode}) - DI Number in header: {diNoFromHeader}";
                            }
                            return $"Empty response (Status: {response.StatusCode}) - FBR may have accepted the request but returned no content";
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(responseContent))
                            return $"HTTP {response.StatusCode} {response.ReasonPhrase}: No response body";

                        if (responseContent.TrimStart().StartsWith("{"))
                            return responseContent; // Return JSON for parsing

                        return $"HTTP {response.StatusCode} {response.ReasonPhrase}: {responseContent}";
                    }
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "FBR API request timed out after 5 minutes");
                return "Request timeout: FBR API did not respond within 5 minutes";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error when calling FBR API: {Message}", ex.Message);
                return $"Error: {ex.Message}";
            }
        }

        private string ParseFbrResponse(string response, out string? fbrDiNo)
        {
            fbrDiNo = null;

            if (string.IsNullOrWhiteSpace(response))
            {
                return "No response from FBR";
            }

            if (response.StartsWith("HTTP ") || response.StartsWith("Request timeout") || response.StartsWith("Error:") || response.Contains("Empty response"))
            {
                // Check if DI number is in the response string (from header logic in SendToFbrAsync)
                if (response.Contains("DI Number in header:"))
                {
                    var match = Regex.Match(response, @"DI Number in header:\s*([^\s,]+)");
                    if (match.Success) fbrDiNo = match.Groups[1].Value.Trim();
                }

                if (response.Contains("Empty response (Status: OK)"))
                {
                    return "Validated Successfully - FBR accepted the invoice (empty response with OK status)";
                }
                return response;
            }

            if (response.TrimStart().StartsWith("<")) return "FBR returned HTML/XML";

            try
            {
                // Use lenient JSON options
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var parsedResponse = System.Text.Json.JsonSerializer.Deserialize<FbrResponse>(response, options);

                if (parsedResponse == null) return "No response from FBR PRD";

                fbrDiNo = parsedResponse.InvoiceNumber;

                if (!string.IsNullOrEmpty(fbrDiNo)) return "Validated Successfully";

                string prmsg = "";
                if (parsedResponse.ValidationResponse?.InvoiceStatuses != null && parsedResponse.ValidationResponse.InvoiceStatuses.Any())
                {
                    prmsg = string.Join(" ", parsedResponse.ValidationResponse.InvoiceStatuses.Select(s => $"StatusCode:{s.ErrorCode} - {s.Error}"));
                }
                else if (!string.IsNullOrEmpty(parsedResponse.ValidationResponse?.Error))
                {
                    prmsg = $"StatusCode:{parsedResponse.ValidationResponse.StatusCode} - {parsedResponse.ValidationResponse.Error}";
                }
                else if (!string.IsNullOrEmpty(parsedResponse.Message))
                {
                    prmsg = parsedResponse.Message;
                }
                else
                {
                    prmsg = "No response from FBR PRD";
                }

                return prmsg.Length > 150 ? prmsg.Substring(0, 150) : prmsg;
            }
            catch (Exception ex)
            {
                return $"Parse error: {ex.Message}";
            }
        }

        private async Task<string> GetUomFromFbrAsync(string hsCode, string fbrToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hsCode)) return "";

                string apiUrl = $"https://gw.fbr.gov.pk/pdi/v2/HS_UOM?hs_code={hsCode}&annexure_id=3";

                using (var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
                })
                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fbrToken);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                    var response = await client.GetAsync(apiUrl);

                    if (!response.IsSuccessStatusCode) return "";

                    string json = await response.Content.ReadAsStringAsync();
                    var arr = JsonConvert.DeserializeObject<JArray>(json);

                    if (arr == null || arr.Count == 0) return "";

                    string uom = arr.First()?["description"]?.ToString()
                               ?? arr.First()?["uoM_ID"]?.ToString()
                               ?? "";

                    return uom;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUOMFromFBRAsync: Exception while fetching UOM for HS Code {HSCode}", hsCode);
                return "";
            }
        }
        private static string CleanText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        public string FormatFbrId(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "0000000";
            var digits = new string(input.Where(char.IsDigit).ToArray());

            // If 13 digits (CNIC), return as is
            if (digits.Length == 13) return digits;

            // If NTN (usually 7, sometimes 8 with check digit), force to 7
            return digits.Length >= 7 ? digits.Substring(0, 7) : digits.PadRight(7, '0');
        }

        public string RemoveDashes(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input.Replace("-", "");
        }

        private string GetNtn7Digits(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "0000000";

            // Extract only digits
            var digits = new string(input.Where(char.IsDigit).ToArray());

            // Return first 7 digits
            return digits.Length >= 7 ? digits.Substring(0, 7) : digits.PadRight(7, '0');
        }
    }

    public static class StringExtensions
    {
        public static string TakeMax(this string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
    }

    // --- Response Models ---
    public class FbrResponse
    {
        [JsonPropertyName("invoiceNumber")]
        public string? InvoiceNumber { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("validationResponse")]
        public FbrValidationResponse? ValidationResponse { get; set; }
    }

    public class FbrValidationResponse
    {
        [JsonPropertyName("statusCode")]
        public string? StatusCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("invoiceStatuses")]
        public List<FbrInvoiceStatus>? InvoiceStatuses { get; set; }
    }

    public class FbrInvoiceStatus
    {
        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
