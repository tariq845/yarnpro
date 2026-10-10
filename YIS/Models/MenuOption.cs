using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YIS.Models
{
    [Table("MENU_OPTIONS")]
    public class MenuOption
    {
        // Primary Key: [MO_ID] bigint IDENTITY(1,1) NOT NULL
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("MO_ID")]
        public required long MoId { get; set; }

        // Maps to [MO_NAME] varchar(200) NULL
        [Column("MO_NAME")]
        public string? MoName { get; set; }

        // Maps to [MO_TYPE] varchar(50) NULL (e.g., 'Page', 'Link', 'Header')
        [Column("MO_TYPE")]
        public string? MoType { get; set; }

        // Maps to [MO_URL] nvarchar(500) NULL
        [Column("MO_URL", TypeName = "nvarchar(500)")]
        public string? MoUrl { get; set; }

        // Maps to [MO_DEACTIVE_ON] date NULL
        [Column("MO_DEACTIVE_ON", TypeName = "date")]
        public DateTime? MoDeactiveOn { get; set; }
    }
}
