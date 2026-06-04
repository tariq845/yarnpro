using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("CATEGORY_MASTER")]
    public class CategoryMaster
    {
        // Primary Key: [CATEGORY_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("CATEGORY_ID")]
        public long? CategoryId { get; set; }

        // Maps to [CATEGORY_NAME] varchar(150) NULL
        [Column("CATEGORY_NAME")]
        public string? CategoryName { get; set; }

        // Foreign Key: [SCATEGORY_ID] bigint NULL (Links to SUB_CATEGORY)
        [Column("SCATEGORY_ID")]
        public long? SCategoryId { get; set; }

        // Foreign Key: [CATEGORY_TYPE_ID] varchar(50) NULL (Links to CATEGORY_TYPES)
        [Column("CATEGORY_TYPE_ID")]
        // Since CATEGORY_TYPE_ID is varchar in SQL, it maps to a string in C#
        public string? CategoryTypeId { get; set; }

        // --- Audit Fields ---

        // Maps to [CREATION_ON] datetime NULL
        [Column("CREATION_ON")]
        public DateTime? CreationOn { get; set; }

        // Maps to [CREATED_BY] bigint NULL
        [Column("CREATED_BY")]
        public long? CreatedBy { get; set; }

        // Maps to [LAST_UPDATE_ON] datetime NULL
        [Column("LAST_UPDATE_ON")]
        public DateTime? LastUpdateOn { get; set; }

        // Maps to [LAST_UPDATED_BY] bigint NULL
        [Column("LAST_UPDATED_BY")]
        public long? LastUpdatedBy { get; set; }

        // Navigation Properties
        [ForeignKey("SCategoryId")]
        public virtual SubCategory? SubCategory { get; set; }

        [ForeignKey("CategoryTypeId")]
        public virtual CategoryType? CategoryType { get; set; }

        // Maps to [UCP_ID] bigint NULL (Link to the active company profile)
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
