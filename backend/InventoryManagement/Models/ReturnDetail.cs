using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class ReturnDetail
    {
        [Key] public int ReturnDetailID { get; set; }

        public int ReturnID { get; set; }
        [ForeignKey(nameof(ReturnID))] public ReturnOrder? ReturnOrder { get; set; }

        public int GoodID { get; set; }
        [ForeignKey(nameof(GoodID))] public Good? Good { get; set; }

        public int? BatchID { get; set; }
        [ForeignKey(nameof(BatchID))] public Batch? Batch { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QuantityReturned { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitCost { get; set; }

        [MaxLength(200)] public string? Reason { get; set; }
        [MaxLength(300)] public string? Note { get; set; }
    }
}
