using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YIS.Models
{
    [Table("SELLER_MASTER")]
    public class SellerMaster
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("SELLER_ID")]
        public long SellerId { get; set; }   // bigint

        [Required]
        [StringLength(100)]
        [Column("SELLER_NAME")]
        public string SellerName { get; set; }

        [StringLength(500)]
        [Column("SELLER_ADDRESS")]
        public string? SellerAddress { get; set; }

        [StringLength(50)]
        [Column("SELLER_CONTACT")]
        public string? SellerContact { get; set; }

        [StringLength(100)]
        [Column("SELLER_EMAIL")]
        public string? SellerEmail { get; set; }

        [StringLength(100)]
        [Column("CREATED_BY")]
        public string? CreatedBy { get; set; }

        [Column("CREATION_ON")]
        public DateTime? CreationOn { get; set; }

        [StringLength(100)]
        [Column("LAST_UPDATED_BY")]
        public string? LastUpdatedBy { get; set; }

        [Column("LAST_UPDATE_ON")]
        public DateTime? LastUpdateOn { get; set; }

        // Navigation Property: One Seller has many Company Profiles
        public ICollection<UserCompanyProfile> CompanyProfiles { get; set; } = new List<UserCompanyProfile>();
    }
}
