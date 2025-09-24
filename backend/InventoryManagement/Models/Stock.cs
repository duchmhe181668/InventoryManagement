using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    // PK: LocationID + GoodID + BatchID
    public class Stock
    {
        public int LocationID { get; set; }
        public Location? Location { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        public int BatchID { get; set; }
        public Batch? Batch { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OnHand { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Reserved { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal InTransit { get; set; }

        // rowversion
        public byte[] RowVersion { get; set; } = System.Array.Empty<byte>();
    }
}
