using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using YIS.Data;
using YIS.Models;

namespace YIS.Helpers
{
    public static class AuthHelper
    {
        public static bool HasPermission(HttpContext context, string url, string permission)
        {
            if (string.IsNullOrEmpty(url)) return false;

            // Normalize URL for matching: Remove extensions and trailing /Index
            var normalizedUrl = url.Split('?')[0]; // Remove query string
            if (normalizedUrl.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
                normalizedUrl = normalizedUrl.Substring(0, normalizedUrl.Length - 7);

            if (normalizedUrl.EndsWith("/Index", StringComparison.OrdinalIgnoreCase))
                normalizedUrl = normalizedUrl.Substring(0, normalizedUrl.Length - 6);

            if (string.IsNullOrEmpty(normalizedUrl)) normalizedUrl = "/";

            // Whitelisted paths
            if (normalizedUrl == "/" || normalizedUrl.Equals("/Dashboard", StringComparison.OrdinalIgnoreCase) ||
                normalizedUrl.Equals("/Profile", StringComparison.OrdinalIgnoreCase) ||
                normalizedUrl.Equals("/SelectCompany", StringComparison.OrdinalIgnoreCase) ||
                normalizedUrl.Equals("/StopImpersonation", StringComparison.OrdinalIgnoreCase))
                return true;

            var rolePermissionsJson = context.Session.GetString("RolePermissions");
            if (string.IsNullOrEmpty(rolePermissionsJson)) return false;

            try
            {
                var permissionsMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rolePermissionsJson);
                if (permissionsMap == null) return false;

                // Normalize all keys in the map for reliable comparison
                var normalizedMap = permissionsMap.ToDictionary(
                    k =>
                    {
                        var key = k.Key;
                        if (key.EndsWith("/Index", StringComparison.OrdinalIgnoreCase)) key = key.Substring(0, key.Length - 6);
                        return key;
                    },
                    v => v.Value,
                    StringComparer.OrdinalIgnoreCase
                );

                // 1. Direct Match
                if (normalizedMap.TryGetValue(normalizedUrl, out var permDirect))
                {
                    return permDirect.TryGetProperty(permission, out var p) && p.GetBoolean();
                }

                // 2. Hierarchical Match (e.g., /Customers/Create matching /Customers)
                // We find the longest key that is a prefix of our normalizedUrl
                var matchedKey = normalizedMap.Keys
                    .Where(k => normalizedUrl.StartsWith(k + "/", StringComparison.OrdinalIgnoreCase) || normalizedUrl.Equals(k, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (matchedKey != null)
                {
                    var perm = normalizedMap[matchedKey];
                    return perm.TryGetProperty(permission, out var permProp) && permProp.GetBoolean();
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        // Convenience methods
        public static bool CanRead(HttpContext context, string url) => HasPermission(context, url, "CanRead");
        public static bool CanCreate(HttpContext context, string url) => HasPermission(context, url, "CanCreate");
        public static bool CanUpdate(HttpContext context, string url) => HasPermission(context, url, "CanUpdate");
        public static bool CanDelete(HttpContext context, string url) => HasPermission(context, url, "CanDelete");

        public static long? GetActiveCompanyId(HttpContext context)
        {
            var companyIdStr = context.Session.GetString("ActiveUcpId");
            if (long.TryParse(companyIdStr, out long companyId))
            {
                return companyId;
            }

            // Fallback to integer if it was stored that way
            var companyIdInt = context.Session.GetInt32("ActiveUcpId");
            if (companyIdInt.HasValue)
            {
                return (long)companyIdInt.Value;
            }

            return null;
        }

        public static long GetUserId(ClaimsPrincipal user)
        {
            if (user == null) return 0;
            var userIdClaim = user.Claims.FirstOrDefault(c => c.Type == "UserId");
            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out long userId))
            {
                return userId;
            }
            return 0;
        }

        public static bool IsAdmin(ClaimsPrincipal user)
        {
            if (user == null || user.Identity?.IsAuthenticated != true) return false;
            
            var name = user.Identity?.Name ?? "";
            if (name.Equals("admin", StringComparison.OrdinalIgnoreCase) || 
                name.Equals("yarnsys", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var userId = GetUserId(user);
            if (userId == 1 || userId == 6) return true;

            var roleIdClaim = user.FindFirst("RoleId")?.Value;
            if (roleIdClaim == "-1" || roleIdClaim == "1") return true;

            if (user.IsInRole("Admin") || user.IsInRole("SuperAdmin") || user.IsInRole("Administrator"))
                return true;

            if (user.Claims.Any(c => c.Type == ClaimTypes.Role && 
                (c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase) || 
                 c.Value.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                 c.Value.Equals("Administrator", StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            return false;
        }

        public static bool IsImpersonating(HttpContext context)
        {
            if (context == null) return false;
            return context.Session.GetString("IsImpersonating") == "true" ||
                   context.User.HasClaim(c => c.Type == "IsImpersonating" && c.Value == "true");
        }

        public static long? GetOriginalAdminUserId(HttpContext context)
        {
            if (context == null) return null;
            var sessionVal = context.Session.GetString("OriginalAdminUserId");
            if (long.TryParse(sessionVal, out long sessionUserId)) return sessionUserId;

            var claimVal = context.User.FindFirst("OriginalAdminUserId")?.Value;
            if (long.TryParse(claimVal, out long claimUserId)) return claimUserId;

            return null;
        }

        public static string? GetOriginalAdminName(HttpContext context)
        {
            if (context == null) return null;
            return context.Session.GetString("OriginalAdminName") ?? context.User.FindFirst("OriginalAdminName")?.Value;
        }

        public static async Task SetUserSessionAndSignInAsync(
            HttpContext httpContext,
            ApplicationDbContext dbContext,
            UserMaster user,
            bool isImpersonating = false,
            long? originalAdminUserId = null,
            string? originalAdminName = null)
        {
            // 1. Build Claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.UmName ?? user.UmUsername ?? "User"),
                new Claim("UserId", user.UmId.ToString()),
                new Claim("RoleId", user.UmUrId?.ToString() ?? "0")
            };

            if (user.UmUrId.HasValue && user.UmUrId.Value > 0)
            {
                var role = await dbContext.UserRoles.FindAsync(user.UmUrId.Value);
                if (role != null)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role.UrName ?? "User"));
                }
                else
                {
                    claims.Add(new Claim(ClaimTypes.Role, "User"));
                }
            }
            else if (user.UmUrId == -1 || user.UmUsername?.ToLower() == "admin" || user.UmName?.ToLower() == "admin" || user.UmId == 1 || user.UmId == 6)
            {
                claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }
            else
            {
                claims.Add(new Claim(ClaimTypes.Role, "User"));
            }

            if (isImpersonating && originalAdminUserId.HasValue)
            {
                claims.Add(new Claim("IsImpersonating", "true"));
                claims.Add(new Claim("OriginalAdminUserId", originalAdminUserId.Value.ToString()));
                claims.Add(new Claim("OriginalAdminName", originalAdminName ?? "Admin"));
            }

            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTime.UtcNow.AddHours(8)
            };

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // 2. Cache Permissions
            var assignments = await dbContext.UserRoleAssignments
                .Where(ura => ura.UraUmId == user.UmId && (ura.UraDisableOn == null || ura.UraDisableOn > DateTime.Today))
                .Join(dbContext.MenuOptions,
                    ura => ura.UraMoId,
                    mo => mo.MoId,
                    (ura, mo) => new
                    {
                        mo.MoUrl,
                        ura.CanRead,
                        ura.CanCreate,
                        ura.CanUpdate,
                        ura.CanDelete
                    })
                .Where(a => !string.IsNullOrEmpty(a.MoUrl))
                .ToListAsync();

            if (assignments.Any())
            {
                var permissions = assignments.ToDictionary(
                    a => a.MoUrl!,
                    a => new { a.CanRead, a.CanCreate, a.CanUpdate, a.CanDelete }
                );
                string jsonPermissions = JsonSerializer.Serialize(permissions);
                httpContext.Session.SetString("RolePermissions", jsonPermissions);
            }
            else
            {
                httpContext.Session.Remove("RolePermissions");
            }

            // 3. Cache Active Company
            if (user.UcpId.HasValue)
            {
                httpContext.Session.SetString("ActiveUcpId", user.UcpId.Value.ToString());
                var profile = await dbContext.UserCompanyProfiles.FindAsync(user.UcpId.Value);
                if (profile != null)
                {
                    httpContext.Session.SetString("ActiveCompanyName", profile.UcpCompanyName ?? "Unknown");
                }
            }
            else
            {
                // Fallback to first assigned company
                var assignedCompany = await (from uca in dbContext.UserCompanyAssignments
                                             join ucp in dbContext.UserCompanyProfiles on uca.UcpId equals ucp.UcpId
                                             where uca.UmId == user.UmId
                                             select ucp).FirstOrDefaultAsync();
                if (assignedCompany != null)
                {
                    httpContext.Session.SetString("ActiveUcpId", assignedCompany.UcpId.ToString());
                    httpContext.Session.SetString("ActiveCompanyName", assignedCompany.UcpCompanyName ?? "Unknown");
                }
                else if (user.UmId == 1)
                {
                    var firstCompany = await dbContext.UserCompanyProfiles.FirstOrDefaultAsync();
                    if (firstCompany != null)
                    {
                        httpContext.Session.SetString("ActiveUcpId", firstCompany.UcpId.ToString());
                        httpContext.Session.SetString("ActiveCompanyName", firstCompany.UcpCompanyName ?? "Unknown");
                    }
                }
                else
                {
                    httpContext.Session.Remove("ActiveUcpId");
                    httpContext.Session.Remove("ActiveCompanyName");
                }
            }

            // 4. Impersonation Session State
            if (isImpersonating && originalAdminUserId.HasValue)
            {
                httpContext.Session.SetString("IsImpersonating", "true");
                httpContext.Session.SetString("OriginalAdminUserId", originalAdminUserId.Value.ToString());
                httpContext.Session.SetString("OriginalAdminName", originalAdminName ?? "Admin");
            }
            else
            {
                httpContext.Session.Remove("IsImpersonating");
                httpContext.Session.Remove("OriginalAdminUserId");
                httpContext.Session.Remove("OriginalAdminName");
            }
        }
    }
}

