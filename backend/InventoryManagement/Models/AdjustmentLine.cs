using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class AdjustmentLine
    {
        [Key] public int AdjustmentLineID { get; set; }

        public int AdjustmentID { get; set; }
        public Adjustment? Adjustment { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QuantityDelta { get; set; }

        public int? BatchID { get; set; }
        public Batch? Batch { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitCost { get; set; }
    }
}
