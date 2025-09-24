using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class StockMovement
    {
        [Key] public long MovementID { get; set; }

        public DateTime CreatedAt { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        public int? FromLocationID { get; set; }
        public Location? FromLocation { get; set; }

        public int? ToLocationID { get; set; }
        public Location? ToLocation { get; set; }

        public int? BatchID { get; set; }
        public Batch? Batch { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitCost { get; set; }

        // 'RECEIPT','SALE','RETURN','TRANSFER_SHIP','TRANSFER_RECEIVE','ADJUST_POS','ADJUST_NEG'
        [Required, MaxLength(30)]
        public string MovementType { get; set; } = "RECEIPT";

        [MaxLength(40)] public string? RefTable { get; set; }
        public int? RefID { get; set; }

        [MaxLength(300)] public string? Note { get; set; }
    }
}
