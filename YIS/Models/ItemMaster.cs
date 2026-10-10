using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("ITEM_MASTER")]
    public class ItemMaster
    {
        // Primary Key: [ITEM_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("ITEM_ID")]
        public long ItemId { get; set; }

        // Maps to [ITEM_NAME] varchar(max) NULL
        [Column("ITEM_NAME")]
        public string? ItemName { get; set; }

        // Maps to [ITEM_ST_CODE] float NULL (Mapped to double?)
        [Column("ITEM_ST_CODE")]
        public double? ItemStCode { get; set; }

        // Maps to [ITEM_HSCODE] varchar(50) NULL (HS Code: Harmonized System Code)
        [Column("ITEM_HSCODE")]
        public string? ItemHsCode { get; set; }

        // Maps to [UOM] varchar(50) NULL (Unit of Measure)
        [Column("UOM", TypeName = "nvarchar(50)")]
        public string? Uom { get; set; }

        // Maps to [ITEM_DEACTIVE_ON] date NULL
        [Column("ITEM_DEACTIVE_ON", TypeName = "date")]
        public DateTime? ItemDeactiveOn { get; set; }

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

           // Maps to [UCP_ID] bigint NULL
        [Column("UCP_ID")]
        public long? UcpId { get; set; }

        [ForeignKey("UcpId")]
        public virtual UserCompanyProfile? UserCompanyProfile { get; set; }
    }
}
