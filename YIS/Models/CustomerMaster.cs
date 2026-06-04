using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("CUSTOMER_MASTER")]
    public class CustomerMaster
    {
        // Primary Key: [CUSTOMER_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("CUSTOMER_ID")]
        public required long CustomerId { get; set; }

        // Maps to [CUSTOMER_NAME] varchar(max) NULL
        [Column("CUSTOMER_NAME")]
        public string? CustomerName { get; set; }

        // Maps to [CUSTOMER_ADDRESS] varchar(max) NULL
        [Column("CUSTOMER_ADDRESS")]
        public string? CustomerAddress { get; set; }

        // Maps to [CUSTOMER_CONTACT] varchar(50) NULL
        [Column("CUSTOMER_CONTACT")]
        public string? CustomerContact { get; set; }

        // Maps to [CUSTOMER_EMAIL] varchar(50) NULL
        [Column("CUSTOMER_EMAIL")]
        public string? CustomerEmail { get; set; }

        // Maps to [CUSTOMER_NTN_NO] varchar(50) NULL
        [Column("CUSTOMER_NTN_NO")]
        public string? CustomerNtnNo { get; set; }

        // Maps to [CUSTOMER_STR_NO] varchar(50) NULL
        [Column("CUSTOMER_STR_NO")]
        public string? CustomerStrNo { get; set; }

        // Maps to [CUSTOMER_CNIC] varchar(50) NULL
        [Column("CUSTOMER_CNIC")]
        public string? CustomerCnic { get; set; }

        // Maps to [PROVINCE] varchar(50) NULL
        [Column("PROVINCE", TypeName = "nvarchar(50)")]
        public string? Province { get; set; }

        // Maps to [REGISTRATION_TYPE] varchar(50) NULL
        [Column("RegistrationType", TypeName = "nvarchar(50)")]
        public string? RegistrationType { get; set; }

        // Maps to [CUSTOMER_DEACTIVE_ON] date NULL
        [Column("CUSTOMER_DEACTIVE_ON", TypeName = "date")]
        public DateTime? CustomerDeactiveOn { get; set; }

        // Maps to [ACCOUNT_ID] bigint NULL - Likely a Foreign Key
        [Column("ACCOUNT_ID")]
        public long? AccountId { get; set; }

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

        [ForeignKey("AccountId")]
        public virtual AccountMaster? Account { get; set; }

        // Maps to [UCP_ID] bigint NULL
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
