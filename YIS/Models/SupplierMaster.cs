using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("SUPPLIER_MASTER")]
    public class SupplierMaster
    {
        // Primary Key: [SUPPLIER_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("SUPPLIER_ID")]
        public required long SupplierId { get; set; }

        // Maps to [SUPPLIER_NAME] varchar(200) NULL
        [Column("SUPPLIER_NAME")]
        public string? SupplierName { get; set; }

        // Maps to [SUPPLIER_ADDRESS] varchar(max) NULL
        [Column("SUPPLIER_ADDRESS")]
        public string? SupplierAddress { get; set; }

        // Maps to [SUPPLIER_CONTACT] varchar(50) NULL
        [Column("SUPPLIER_CONTACT")]
        public string? SupplierContact { get; set; }

        // Maps to [SUPPLIER_EMAIL] varchar(50) NULL
        [Column("SUPPLIER_EMAIL")]
        public string? SupplierEmail { get; set; }

        // Maps to [SUPPLIER_DEACTIVE_ON] date NULL
        [Column("SUPPLIER_DEACTIVE_ON", TypeName = "date")]
        public DateTime? SupplierDeactiveOn { get; set; }

        // Maps to [SUPPLIER_NTN_NO] varchar(50) NULL (National Tax Number)
        [Column("SUPPLIER_NTN_NO")]
        public string? SupplierNtnNo { get; set; }

        // Maps to [SUPPLIER_STR_NO] varchar(50) NULL (Sales Tax Registration Number)
        [Column("SUPPLIER_STR_NO")]
        public string? SupplierStrNo { get; set; }

        // Maps to [SUPPLIER_CNIC] varchar(50) NULL (National Identity Card)
        [Column("SUPPLIER_CNIC")]
        public string? SupplierCnic { get; set; }

        // Maps to [ACCOUNT_ID] bigint NULL (Likely a link to a GL/Chart of Accounts)
        [Column("ACCOUNT_ID")]
        public long? AccountId { get; set; }

        // Maps to [PURCHASE_ACCOUNT_ID] bigint NULL (Default Purchase / Inventory Account)
        [Column("PURCHASE_ACCOUNT_ID")]
        public long? PurchaseAccountId { get; set; }

        // Maps to [TAX_ACCOUNT_ID] bigint NULL (Default Input Tax Account)
        [Column("TAX_ACCOUNT_ID")]
        public long? TaxAccountId { get; set; }

        // Maps to [COMM_EXPENSE_ACCOUNT_ID] bigint NULL (Default Commission Expense Account)
        [Column("COMM_EXPENSE_ACCOUNT_ID")]
        public long? CommExpenseAccountId { get; set; }

        // Maps to [COMM_PAYABLE_ACCOUNT_ID] bigint NULL (Default Commission Payable to Agent Account)
        [Column("COMM_PAYABLE_ACCOUNT_ID")]
        public long? CommPayableAccountId { get; set; }

        [ForeignKey("PurchaseAccountId")]
        public virtual AccountMaster? PurchaseAccount { get; set; }

        [ForeignKey("TaxAccountId")]
        public virtual AccountMaster? TaxAccount { get; set; }

        [ForeignKey("CommExpenseAccountId")]
        public virtual AccountMaster? CommExpenseAccount { get; set; }

        [ForeignKey("CommPayableAccountId")]
        public virtual AccountMaster? CommPayableAccount { get; set; }

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
