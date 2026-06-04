using Microsoft.AspNetCore.Http;
using System.Text.Json;

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
                normalizedUrl.Equals("/SelectCompany", StringComparison.OrdinalIgnoreCase))
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

        public static long GetUserId(System.Security.Claims.ClaimsPrincipal user)
        {
            if (user == null) return 0;
            var userIdClaim = user.Claims.FirstOrDefault(c => c.Type == "UserId");
            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out long userId))
            {
                return userId;
            }
            return 0;
        }
    }
}
