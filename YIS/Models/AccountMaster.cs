using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("ACCOUNT_MASTER")]
    public class AccountMaster
    {
        // Primary Key: [ACCOUNT_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("ACCOUNT_ID")]
        public required long AccountId { get; set; }

        // Maps to [ACCOUNT_NAME] varchar(150) NULL (e.g., Cash, Accounts Receivable, Sales)
        [Column("ACCOUNT_NAME")]
        public string? AccountName { get; set; }

        // Foreign Key: [CATEGORY_ID] bigint NULL (Links to CATEGORY_MASTER to define account type/group)
        [Column("CATEGORY_ID")]
        public long? CategoryId { get; set; }

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

        [ForeignKey("CategoryId")]
        public virtual CategoryMaster? Category { get; set; }

        // Maps to [UCP_ID] bigint NULL (Link to the active company profile)
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
