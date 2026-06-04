using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("USER_ROLE_ASSIGNMENTS")]
    public class UserRoleAssignment
    {
        // Primary Key: [URA_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("URA_ID")]
        public required long UraId { get; set; }

        // Foreign Key: [URA_UM_ID] bigint NULL (Links to USER_MASTER)
        [Column("URA_UM_ID")]
        public long? UraUmId { get; set; }

        // Foreign Key: [URA_MO_ID] bigint NULL (Links to MENU_OPTIONS)
        [Column("URA_MO_ID")]
        public long? UraMoId { get; set; }

        // Maps to [URA_RIGHTS] int NULL (An integer representing access level, e.g., 1=Read, 2=Write, 4=Delete)
        [Column("URA_RIGHTS")]
        public int? UraRights { get; set; }

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


        // Maps to [URA_DISABLE_ON] date NULL
        [Column("URA_DISABLE_ON", TypeName = "date")]
        public DateTime? UraDisableOn { get; set; }

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
    }
}
