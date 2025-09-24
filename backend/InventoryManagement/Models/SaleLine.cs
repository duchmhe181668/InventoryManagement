using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class SaleLine
    {
        [Key] public int SaleLineID { get; set; }

        public int SaleID { get; set; }
        public Sale? Sale { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        public int? BatchID { get; set; }
        public Batch? Batch { get; set; }
    }
}
