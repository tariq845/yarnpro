using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("CATEGORY_TYPES")]
    public class CategoryType
    {
        // The primary key for the table. It is NOT NULL in SQL.
        // Maps to [CATEGORY_TYPE_ID] varchar NOT NULL
        [Key]
        [Column("CATEGORY_TYPE_ID")]
        // Since the SQL column is NOT NULL, the C# property should be non-nullable.
        public required string CategoryTypeId { get; set; }

        // Maps to [CATEGORY_TYPE] varchar NULL
        [Column("CATEGORY_TYPE")]
        // Since the SQL column is NULLABLE, the C# property should be nullable (string?).
        public string? CategoryTypeName { get; set; }

        // Maps to [CREATION_ON] datetime NULL
        [Column("CREATION_ON")]
        // Nullable datetime
        public DateTime? CreationOn { get; set; }

        // Maps to [CREATED_BY] bigint NULL
        [Column("CREATED_BY")]
        // Nullable long
        public long? CreatedBy { get; set; }

        // Maps to [LAST_UPDATE_ON] datetime NULL
        [Column("LAST_UPDATE_ON")]
        // Nullable datetime
        public DateTime? LastUpdateOn { get; set; }

        // Maps to [LAST_UPDATED_BY] bigint NULL
        [Column("LAST_UPDATED_BY")]
        // Nullable long
        public long? LastUpdatedBy { get; set; }

           // Maps to [UCP_ID] bigint NULL
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
