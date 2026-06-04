using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("SUB_CATEGORY")]
    public class SubCategory
    {
        // Primary Key: [SCATEGORY_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("SCATEGORY_ID")]
        public long? SCategoryId { get; set; }

        // Maps to [SCATEGORY_NAME] varchar(150) NULL
        [Column("SCATEGORY_NAME")]
        public string? SCategoryName { get; set; }

        // Foreign Key: [MCATEGORY_ID] bigint NULL (Links to MAIN_CATEGORY)
        [Column("MCATEGORY_ID")]
        public long? MCategoryId { get; set; }

        // --- Audit Fields ---

        // Maps to [CREATED_BY] bigint NULL
        [Column("CREATED_BY")]
        public long? CreatedBy { get; set; }

        // Maps to [CREATION_ON] datetime NULL
        [Column("CREATION_ON")]
        public DateTime? CreationOn { get; set; }

        // Maps to [LAST_UPDATED_BY] bigint NULL
        [Column("LAST_UPDATED_BY")]
        public long? LastUpdatedBy { get; set; }

        // Maps to [LAST_UPDATE_ON] datetime NULL
        [Column("LAST_UPDATE_ON")]
        public DateTime? LastUpdateOn { get; set; }

        // Navigation Property
        [ForeignKey("MCategoryId")]
        public virtual MainCategory? MainCategory { get; set; }

           // Maps to [UCP_ID] bigint NULL
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
