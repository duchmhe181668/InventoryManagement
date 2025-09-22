using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class Outbound
    {
        [Key]
        public int OutboundID { get; set; }

        [ForeignKey("Staff")]
        public int StaffID { get; set; }
        public User? Staff { get; set; }

        public DateTime CreatedAt { get; set; }

        public ICollection<OutboundDetail>? OutboundDetails { get; set; }
        //
    }
}
