using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("USER_MASTER")]
    public class UserMaster
    {
        // Primary Key: [UM_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("UM_ID")]
        public required long UmId { get; set; }

        // Maps to [UM_NAME] varchar(500) NULL
        [Column("UM_NAME")]
        public string? UmName { get; set; }

        // Maps to [UM_USERNAME] varchar(500) NULL
        [Column("UM_USERNAME")]
        public string? UmUsername { get; set; }

        // Maps to [UM_CONTACT] varchar(50) NULL
        [Column("UM_CONTACT")]
        public string? UmContact { get; set; }

        // Maps to [UM_PASSWORD] binary(350) NULL (For salted/hashed password storage)
        // Maps to [UM_PASSWORD] nvarchar(1000) NULL
        [Column("UM_PASSWORD", TypeName = "nvarchar(1000)")]
        public string? UmPassword { get; set; }

        // Maps to [UM_UNIQUE_ID] nvarchar(1000) NULL
        [Column("UM_UNIQUE_ID", TypeName = "nvarchar(1000)")]
        public string? UmUniqueId { get; set; }



        // Maps to [UM_ACTIVE_ON] date NULL
        [Column("UM_ACTIVE_ON", TypeName = "date")]
        public DateTime? UmActiveOn { get; set; }

        // Maps to [UM_DEACTIVE_ON] date NULL
        [Column("UM_DEACTIVE_ON", TypeName = "date")]
        public DateTime? UmDeactiveOn { get; set; }

        // Foreign Key: [UM_UR_ID] bigint NULL (Likely a link to a User Role table)
        [Column("UM_UR_ID")]
        public long? UmUrId { get; set; }

        // --- Permission Fields ---

        // Maps to [CAN_CREATE] bit NULL
        [Column("CAN_CREATE")]
        public bool CanCreate { get; set; }

        // Maps to [CAN_READ] bit NULL
        [Column("CAN_READ")]
        public bool CanRead { get; set; }

        // Maps to [CAN_UPDATE] bit NULL
        [Column("CAN_UPDATE")]
        public bool CanUpdate { get; set; }

        // Maps to [CAN_DELETE] bit NULL
        [Column("CAN_DELETE")]
        public bool CanDelete { get; set; }

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

        // Maps to [LAST_LOGIN] datetime NULL
        [Column("LAST_LOGIN")]
        public DateTime? LastLogin { get; set; }

        // Maps to [UM_UCP_FK] numeric(18,0) NULL
        [Column("UM_UCP_FK")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }

        public virtual ICollection<UserCompanyAssignment> CompanyAssignments { get; set; } = new List<UserCompanyAssignment>();
    }
}
