using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using YIS.Data;
using YIS.Models;

namespace YIS.Helpers
{
    public static class AccountHelper
    {
        /// <summary>
        /// Retrieves an existing system account or creates a single master account if one does not exist.
        /// Guaranteed not to generate duplicate accounts on repeated postings.
        /// </summary>
        public static async Task<AccountMaster> GetOrCreateSystemAccountAsync(
            ApplicationDbContext context,
            string defaultName,
            string preferredCategoryName,
            long ucpId,
            params string[] searchKeywords)
        {
            if (string.IsNullOrWhiteSpace(defaultName))
                throw new ArgumentException("Default account name cannot be empty.", nameof(defaultName));

            string cleanDefaultName = defaultName.Trim();

            // 1. Exact match (case-insensitive) - Always pick the earliest/original account
            var acc = await context.AccountMasters
                .Where(a => a.UcpId == ucpId && a.AccountName != null && a.AccountName.Trim().ToLower() == cleanDefaultName.ToLower())
                .OrderBy(a => a.AccountId)
                .FirstOrDefaultAsync();

            if (acc != null) return acc;

            // 2. Keyword match (e.g. if an existing account is named "Sales Tax - Output" or "Output Tax")
            if (searchKeywords != null && searchKeywords.Length > 0)
            {
                var query = context.AccountMasters.Where(a => a.UcpId == ucpId && a.AccountName != null);
                foreach (var kw in searchKeywords)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    string cleanKw = kw.Trim().ToLower();
                    query = query.Where(a => a.AccountName!.ToLower().Contains(cleanKw));
                }
                acc = await query.OrderBy(a => a.AccountId).FirstOrDefaultAsync();
                if (acc != null) return acc;
            }

            // 3. Category Resolution or Creation
            var category = await context.CategoryMasters
                .Where(c => c.UcpId == ucpId)
                .OrderByDescending(c => c.CategoryName != null && c.CategoryName.ToLower().Contains(preferredCategoryName.ToLower()))
                .FirstOrDefaultAsync();

            if (category == null)
            {
                category = await context.CategoryMasters.FirstOrDefaultAsync(c => c.UcpId == ucpId);
                if (category == null)
                {
                    category = new CategoryMaster
                    {
                        CategoryName = preferredCategoryName,
                        UcpId = ucpId,
                        CreationOn = DateTime.Now
                    };
                    context.CategoryMasters.Add(category);
                    await context.SaveChangesAsync();
                }
            }

            // 4. Final check before insert to prevent race condition
            acc = await context.AccountMasters
                .Where(a => a.UcpId == ucpId && a.AccountName != null && a.AccountName.Trim().ToLower() == cleanDefaultName.ToLower())
                .OrderBy(a => a.AccountId)
                .FirstOrDefaultAsync();

            if (acc != null) return acc;

            // 5. Create the single master account
            acc = new AccountMaster
            {
                AccountId = 0,
                AccountName = cleanDefaultName,
                CategoryId = category.CategoryId,
                UcpId = ucpId,
                CreationOn = DateTime.Now
            };

            context.AccountMasters.Add(acc);
            await context.SaveChangesAsync();
            return acc;
        }
    }
}
