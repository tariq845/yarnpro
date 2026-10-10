using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace YIS.Filters
{
    public class RbacFilter : IPageFilter
    {
        public void OnPageHandlerSelected(PageHandlerSelectedContext context)
        {
        }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            var method = context.HttpContext.Request.Method;
            var handlerName = context.HandlerMethod?.Name ?? "";

            // 1. Allow bypass for public/system pages
            if (IsPublicPage(path))
            {
                return;
            }

            // CRITICAL: Check for Session Expiry
            // If the session is missing crucial data (UserId), force logout/login even if the auth cookie is valid.
            //  var sessionUserId = context.HttpContext.Session.GetString("UserId");
            // if (string.IsNullOrEmpty(sessionUserId))
            //{
            // Session has expired
            //  context.Result = new RedirectToPageResult("/Login");
            //return;
            //}

            // 2. Check if user is authenticated
            if (!context.HttpContext.User.Identity?.IsAuthenticated ?? true)
            {
                // Standard Auth middleware usually handles this, but explicit check doesn't hurt
                context.Result = new RedirectToPageResult("/Login");
                return;
            }

            // 3. Force Company Selection if missing in session
            var activeUcpId = context.HttpContext.Session.GetString("ActiveUcpId");
            if (string.IsNullOrEmpty(activeUcpId) && !path.Equals("/SelectCompany", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new RedirectToPageResult("/SelectCompany");
                return;
            }

            // 3. Map Operation to Permission
            string requiredPermission = "CanRead"; // Default

            bool isPost = method.Equals("POST", StringComparison.OrdinalIgnoreCase);
            bool isCrudPage = path.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Add", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Entry", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Soda", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("Voucher", StringComparison.OrdinalIgnoreCase);

            if (isPost)
            {
                if (handlerName.Contains("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    requiredPermission = "CanDelete";
                }
                else if (isCrudPage)
                {
                    // Improved ID detection for Model-prefixed fields (e.g., Customer.CustomerId)
                    bool hasId = context.RouteData.Values.ContainsKey("id") ||
                                 context.HttpContext.Request.Query.ContainsKey("id") ||
                                 context.HttpContext.Request.Form.ContainsKey("id") ||
                                 context.HttpContext.Request.Form.Keys.Any(k => (k.EndsWith(".Id") || k.EndsWith("Id")) &&
                                    !string.IsNullOrEmpty(context.HttpContext.Request.Form[k]) &&
                                    context.HttpContext.Request.Form[k] != "0") ||
                                 (context.HandlerArguments.TryGetValue("id", out var idArg) && idArg != null && idArg.ToString() != "0");

                    requiredPermission = hasId ? "CanUpdate" : "CanCreate";
                }
                else
                {
                    requiredPermission = "CanUpdate";
                }
            }
            else // GET
            {
                if (isCrudPage)
                {
                    var hasId = context.RouteData.Values.ContainsKey("id") ||
                                 context.HttpContext.Request.Query.ContainsKey("id") ||
                                 (context.HandlerArguments.TryGetValue("id", out var idArg) && idArg != null && idArg.ToString() != "0");

                    requiredPermission = hasId ? "CanUpdate" : "CanCreate";
                }
            }

            // 4. Validate Access using centralized AuthHelper
            if (!YIS.Helpers.AuthHelper.HasPermission(context.HttpContext, path, requiredPermission))
            {
                if (isPost)
                {
                    // For POST requests, show a toast message instead of redirecting to a full error page
                    var pageModel = context.HandlerInstance as Microsoft.AspNetCore.Mvc.RazorPages.PageModel;
                    if (pageModel != null)
                    {
                        pageModel.TempData["ErrorMessage"] = $"Access Denied: You do not have permission to {requiredPermission.Substring(3).ToLower()} records in this module.";
                    }

                    // Try to redirect back to referer or same page
                    var referer = context.HttpContext.Request.Headers["Referer"].ToString();
                    if (!string.IsNullOrEmpty(referer))
                    {
                        context.Result = new RedirectResult(referer);
                    }
                    else
                    {
                        context.Result = new RedirectToPageResult("./Index");
                    }
                }
                else
                {
                    // For GET requests, redirect to AccessDenied
                    context.Result = new RedirectToPageResult("/AccessDenied");
                }
            }
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
        {
        }

        private bool IsPublicPage(string path)
        {
            var publicPaths = new[]
            {
                "/",
                "/Login",
                "/Logout",
                "/AccessDenied",
                "/Error",
                "/Index",
                "/Privacy",
                "/Profile",
                "/SelectCompany"
            };

            if (publicPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (path.StartsWith("/Dashboard", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.Contains(".") || path.StartsWith("/lib/") || path.StartsWith("/css/") || path.StartsWith("/js/"))
                return true;

            return false;
        }
    }
}
