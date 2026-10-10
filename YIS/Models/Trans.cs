using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("TRANS")]
    public class Trans
    {
        // Primary Key: [TRANS_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("TRANS_ID")]
        public required long TransId { get; set; }

        // Maps to [TRANS_NO] bigint NULL
        [Column("TRANS_NO")]
        public long? TransNo { get; set; }

        // Maps to [TRANS_TYPE] varchar(50) NULL
        [Column("TRANS_TYPE")]
        public string? TransType { get; set; }

        // Maps to [ACCOUNT_ID] bigint NULL (Link to the Chart of Accounts)
        [Column("ACCOUNT_ID")]
        public long? AccountId { get; set; }

        // Maps to [DEBIT] float NULL (Mapped to double?)
        [Column("DEBIT")]
        public double? Debit { get; set; }

        // Maps to [CREDIT] float NULL (Mapped to double?)
        [Column("CREDIT")]
        public double? Credit { get; set; }

        // Maps to [REMARKS] varchar(max) NULL
        [Column("REMARKS")]
        public string? Remarks { get; set; }

        // Foreign Key: [ENTRY_ID] bigint NULL (Link to the parent ENTRY table)
        [Column("ENTRY_ID")]
        public long? EntryId { get; set; }

        // Maps to [UCP_ID] bigint NULL (Link to the active company profile)
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
