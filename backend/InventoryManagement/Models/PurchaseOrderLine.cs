using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class PurchaseOrderLine
    {
        [Key] public int POLineID { get; set; }

        public int POID { get; set; }
        public PurchaseOrder? PO { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }
    }
}
