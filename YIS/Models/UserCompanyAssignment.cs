using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YIS.Models
{
    [Table("USER_COMPANY_ASSIGNMENT")]
    public class UserCompanyAssignment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("UCA_ID")]
        public long UcaId { get; set; }

        [Required]
        [Column("UM_ID")]
        public long UmId { get; set; }

        [Required]
        [Column("UCP_ID")]
        public long UcpId { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(UmId))]
        public virtual UserMaster? User { get; set; }

        [ForeignKey(nameof(UcpId))]
        public virtual UserCompanyProfile? Company { get; set; }
    }
}
