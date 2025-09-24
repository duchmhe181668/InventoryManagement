using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class TransferItem
    {
        [Key] public int TransferItemID { get; set; }

        public int TransferID { get; set; }
        public Transfer? Transfer { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        public int? BatchID { get; set; }
        public Batch? Batch { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ShippedQty { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReceivedQty { get; set; }
    }
}
