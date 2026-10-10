using Microsoft.EntityFrameworkCore;
using YIS.Models;

namespace YIS.Data
{
    public class ApplicationDbContext : DbContext
    {
        // The constructor is necessary for dependency injection in ASP.NET Core
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public override int SaveChanges()
        {
            ApplyAuditStamps();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditStamps();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyAuditStamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            long currentUserId = 0;
            var user = _httpContextAccessor?.HttpContext?.User;
            if (user != null)
            {
                currentUserId = YIS.Helpers.AuthHelper.GetUserId(user);
            }

            DateTime now = DateTime.Now;

            foreach (var entry in entries)
            {
                var entity = entry.Entity;
                var type = entity.GetType();

                if (entry.State == EntityState.Added)
                {
                    SetPropertyValue(entity, type, "CreationOn", now);
                    SetPropertyValue(entity, type, "CreatedBy", currentUserId);
                }

                SetPropertyValue(entity, type, "LastUpdateOn", now);
                SetPropertyValue(entity, type, "LastUpdatedBy", currentUserId);
            }
        }

        private void SetPropertyValue(object entity, Type type, string propertyName, object value)
        {
            var prop = type.GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                try
                {
                    if (propType == typeof(long) || propType == typeof(int))
                    {
                        prop.SetValue(entity, Convert.ChangeType(value, propType));
                    }
                    else if (propType == typeof(string))
                    {
                        prop.SetValue(entity, value.ToString());
                    }
                    else if (propType == typeof(DateTime))
                    {
                        prop.SetValue(entity, value);
                    }
                }
                catch
                {
                    // Ignore if conversion fails - safer for dynamic auditing
                }
            }
        }

        // --- Core Entity Sets ---

        public DbSet<CategoryType> CategoryTypes { get; set; } = default!;
        public DbSet<CustomerMaster> CustomerMasters { get; set; } = default!;
        public DbSet<Entry> Entries { get; set; } = default!;
        public DbSet<ItemMaster> ItemMasters { get; set; } = default!;
        public DbSet<MainCategory> MainCategories { get; set; } = default!;
        public DbSet<MenuOption> MenuOptions { get; set; } = default!;
        public DbSet<SubCategory> SubCategories { get; set; } = default!;
        public DbSet<SupplierMaster> SupplierMasters { get; set; } = default!;
        public DbSet<Trans> Transactions { get; set; } = default!;
        public DbSet<CategoryMaster> CategoryMasters { get; set; } = default!;
        public DbSet<AccountMaster> AccountMasters { get; set; } = default!;

        // --- User/Security Entity Sets ---

        public DbSet<UserMaster> UserMasters { get; set; } = default!;
        public DbSet<UserRole> UserRoles { get; set; } = default!;
        public DbSet<UserRoleAssignment> UserRoleAssignments { get; set; } = default!;
        public DbSet<UserCompanyAssignment> UserCompanyAssignments { get; set; } = default!;
        public DbSet<UserCompanyProfile> UserCompanyProfiles { get; set; } = default!;
        public DbSet<SellerMaster> SellerMasters { get; set; } = default!;

        // --- Global Type Conversions ---
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // Reverted global conversion as it was causing issues with bigint columns like UM_ID
        }

        // --- Fluent API Configuration (Optional but recommended) ---

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure UserCompanyAssignment relationships
            modelBuilder.Entity<UserCompanyAssignment>()
                .HasOne(uca => uca.User)
                .WithMany(u => u.CompanyAssignments)
                .HasForeignKey(uca => uca.UmId)
                .OnDelete(DeleteBehavior.Cascade);

            // Explicitly ignore the relationship to avoid Type Mismatch (Int vs Decimal)
            // The DB has INT for UCA and DECIMAL for UCP, causing crashes in EF Core validation.
            // We handle this via explicit Joins in the application code.
            modelBuilder.Entity<UserCompanyAssignment>()
                 .Ignore(uca => uca.Company);

            // Example configuration using Fluent API if you didn't use Data Annotations

            // 1. Ensure keys are non-nullable using the 'required' mapping
            modelBuilder.Entity<CategoryType>()
                .Property(ct => ct.CategoryTypeId)
                .IsRequired();

            // 2. Configure composite keys or complex relationships here

            // 3. Configure the precision of the 'float' properties that mapped to 'double'
            // This explicitly sets precision and scale for important financial fields
            modelBuilder.Entity<Trans>()
                .Property(t => t.Debit)
                .HasColumnType("float"); // or "decimal(18, 2)" if using decimal in C#

            modelBuilder.Entity<Trans>()
                .Property(t => t.Credit)
                .HasColumnType("float"); // or "decimal(18, 2)" if using decimal in C#

            // 4. Configure Seller -> Company Profiles one-to-many relationship
            modelBuilder.Entity<UserCompanyProfile>()
                .HasOne(ucp => ucp.Seller)
                .WithMany(s => s.CompanyProfiles)
                .HasForeignKey(ucp => ucp.SellerId)
                .OnDelete(DeleteBehavior.Cascade); // Delete company profiles when seller is deleted

            // 5. Surgical Type Conversions for DECIMAL columns
            // Apply only to columns confirmed to be numeric/decimal in the DB.
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    var name = property.Name;
                    var entityName = entityType.ClrType.Name;

                    // 5. Surgical Type Conversions for UcpId
                    if (name == "UcpId")
                    {
                        // A. Specific entities where UcpId is DECIMAL in DB
                        if (entityName == "UserMaster" || entityName == "UserCompanyProfile")
                        {
                            if (property.ClrType == typeof(long))
                            {
                                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<long, decimal>(
                                    v => (decimal)v,
                                    v => (long)v));
                            }
                            else if (property.ClrType == typeof(long?))
                            {
                                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<long?, decimal>(
                                    v => (decimal)(v ?? 0),
                                    v => (long)v));
                            }
                        }
                        // B. UserCompanyAssignment where UcpId is INT in DB
                        else if (entityName == "UserCompanyAssignment")
                        {
                            if (property.ClrType == typeof(long))
                            {
                                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<long, int>(
                                    v => (int)v,
                                    v => (long)v));
                            }
                        }
                        // C. EVERYTHING ELSE (MainCategory, SubCategory, etc.) is BIGINT in DB
                        // We must explicitly set this because EF Core infers decimal from the FK relationship.
                        else
                        {
                            property.SetColumnType("bigint");
                        }
                    }
                }
            }
        }
    }
}
