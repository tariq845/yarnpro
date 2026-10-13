using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("ENTRY")]
    public class Entry
    {
        // Primary Key: [ENTRY_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("ENTRY_ID")]
        public required long EntryId { get; set; }

        // --- Core Entry Fields ---

        // Maps to [ENTRY_NUMBER] bigint NULL
        [Column("ENTRY_NUMBER")]
        public long? EntryNumber { get; set; }

        // Maps to [ENTRY_TYPE] varchar(50) NULL
        [Column("ENTRY_TYPE")]
        public string? EntryType { get; set; }

        // Maps to [ENTRY_DATE] date NULL
        [Column("ENTRY_DATE", TypeName = "date")]
        public DateTime? EntryDate { get; set; }

        // --- Reference Fields ---

        // Maps to [ENTRY_REF_NO] bigint NULL
        [Column("ENTRY_REF_NO")]
        public long? EntryRefNo { get; set; }

        // Maps to [ENTRY_REF_ID] bigint NULL
        [Column("ENTRY_REF_ID")]
        public long? EntryRefId { get; set; }

        // Maps to [ENTRY_REF_TYPE] varchar(50) NULL
        [Column("ENTRY_REF_TYPE")]
        public string? EntryRefType { get; set; }

        // Maps to [ENTRY_REF_DATE] date NULL
        [Column("ENTRY_REF_DATE", TypeName = "date")]
        public DateTime? EntryRefDate { get; set; }

        // Maps to [ENTRY_REF_CODE] nvarchar(50) NULL
        [Column("ENTRY_REF_CODE")]
        public string? EntryRefCode { get; set; }

        // --- Attribute/Custom Fields ---

        // Maps to [ENTRY_ATTRIBUTE1] varchar(50) NULL
        [Column("ENTRY_ATTRIBUTE1")]
        public string? EntryAttribute1 { get; set; }

        // Maps to [ENTRY_ATTRIBUTE2] varchar(50) NULL
        [Column("ENTRY_ATTRIBUTE2")]
        public string? EntryAttribute2 { get; set; }

        // Maps to [ENTRY_ATTRIBUTE3] varchar(50) NULL
        [Column("ENTRY_ATTRIBUTE3")]
        public string? EntryAttribute3 { get; set; }

        // Maps to [ENTRY_ATTRIBUTE4] varchar(50) NULL
        [Column("ENTRY_ATTRIBUTE4")]
        public string? EntryAttribute4 { get; set; }

        // Maps to [ENTRY_ATTRIBUTE5] varchar(50) NULL
        [Column("ENTRY_ATTRIBUTE5")]
        public string? EntryAttribute5 { get; set; }

        // --- Financial/Inventory Fields ---

        // Maps to [ITEM_ID] bigint NULL
        [Column("ITEM_ID")]
        public long? ItemId { get; set; }

        // Maps to [QTY] float NULL (Using double for float in C#)
        [Column("QTY")]
        public double? Qty { get; set; }

        // Maps to [LBS] float NULL (Using double for float in C#)
        [Column("LBS")]
        public double? Lbs { get; set; }

        // Maps to [RATE] float NULL (Using double for float in C#)
        [Column("RATE")]
        public double? Rate { get; set; }

        // Maps to [AMOUNT] float NULL (Using double for float in C#)
        [Column("AMOUNT")]
        public double? Amount { get; set; }

        // Maps to [TAX_AMOUNT] float NULL (Using double for float in C#)
        [Column("TAX_AMOUNT")]
        public double? TaxAmount { get; set; }

        // --- Other Transaction Details ---

        // Maps to [ENTRY_ADJUST_ID] bigint NULL
        [Column("ENTRY_ADJUST_ID")]
        public long? EntryAdjustId { get; set; }

        // Maps to [ENTRY_REMARKS] varchar(max) NULL
        [Column("ENTRY_REMARKS")]
        public string? EntryRemarks { get; set; }

        // Maps to [ENTRY_COMM_ID] bigint NULL
        [Column("ENTRY_COMM_ID")]
        public long? EntryCommId { get; set; }

        // Maps to [ENTRY_COMM_PER] float NULL
        [Column("ENTRY_COMM_PER")]
        public double? EntryCommPer { get; set; }

        // Maps to [ENTRY_COMM_AMT] float NULL
        [Column("ENTRY_COMM_AMT")]
        public double? EntryCommAmt { get; set; }

        // Maps to [ENTRY_TERMS_DAYS] int NULL
        [Column("ENTRY_TERMS_DAYS")]
        public int? EntryTermsDays { get; set; }

        // Maps to [ENTRY_COMM_SUKRI] float NULL
        [Column("ENTRY_COMM_SUKRI")]
        public double? EntryCommSukri { get; set; } = 0;

        // --- FBR Integration Fields (Assuming Federal Board of Revenue) ---

        // Maps to [ENTRY_POSTED_TO_FBR] bit NULL (bit maps to bool?)
        [Column("ENTRY_POSTED_TO_FBR")]
        public bool? EntryPostedToFbr { get; set; }

        // Maps to [ENTRY_FBR_DI_NO] varchar(50) NULL
        [Column("ENTRY_FBR_DI_NO")]
        public string? EntryFbrDiNo { get; set; }

        // Maps to [ENTRY_FBR_ERROR] varchar(1000) NULL
        [Column("ENTRY_FBR_ERROR")]
        public string? EntryFbrError { get; set; }

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

        // --- Company Profile Link ---

        // Maps to [UCP_ID] bigint NULL
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}

