using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YIS.Models
{
    [Table("USER_COMPANY_PROFILE")]
    public class UserCompanyProfile
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("UCP_ID")]
        public long UcpId { get; set; }   // bigint

        [StringLength(15)]
        [Column("UCP_COMPANY_NTN")]
        public string? UcpCompanyNtn { get; set; }

        [StringLength(50)]
        [Column("UCP_COMPANY_NAME")]
        public string? UcpCompanyName { get; set; }

        [StringLength(50)]
        [Column("UCP_COMPANY_PROVINCE")]
        public string? UcpCompanyProvince { get; set; }

        [StringLength(100)]
        [Column("UCP_COMPANY_ADDRESS")]
        public string? UcpCompanyAddress { get; set; }

        [StringLength(100)]
        [Column("UCP_OWNER_NAME")]
        public string? UcpOwnerName { get; set; }

        [Column("UCP_TERMS_DETAILS")]
        public string? UcpTermsDetails { get; set; }

        [StringLength(50)]
        [Column("UCP_IRIS_USERNAME")]
        public string? UcpIrisUsername { get; set; }

        [StringLength(500)]
        [Column("UCP_IRIS_PASSWORD")]
        public string? UcpIrisPassword { get; set; }

        [StringLength(100)]
        [Column("UCP_SANDBOX_TOKEN")]
        public string? UcpSandboxToken { get; set; }

        [StringLength(100)]
        [Column("UCP_PRODUCTION_TOKEN")]
        public string? UcpProductionToken { get; set; }

        [Column("UCP_IS_PRODUCTION_TOKEN")]
        public bool IsProductionToken { get; set; } = false;

        [StringLength(50)]
        [Column("UCP_ADD_TAG")]
        public string? UcpAddTag { get; set; }

        [Column("UCP_ADD_STAMP")]
        public DateTime? UcpAddStamp { get; set; }

        [StringLength(50)]
        [Column("UCP_EDIT_TAG")]
        public string? UcpEditTag { get; set; }

        [Column("UCP_EDIT_STAMP")]
        public DateTime? UcpEditStamp { get; set; }

        [StringLength(255)]
        [Column("UCP_PATH")]
        public string? UcpPath { get; set; }

        // 🔹 Foreign Key
        [Required]
        [Column("SELLER_ID")]
        public long SellerId { get; set; }

        // 🔹 Active Flag (default 0 in DB)
        [Required]
        [Column("UCP_IS_ACTIVE")]
        public bool UcpIsActive { get; set; } = false;

        // 🔹 Navigation Property
        [ForeignKey(nameof(SellerId))]
        public SellerMaster? Seller { get; set; }
    }
}
