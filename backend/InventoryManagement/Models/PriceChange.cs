using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class PriceChange
    {
        [Key] public int PriceChangeID { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        public int ChangedBy { get; set; }
        public User? ChangedByUser { get; set; }

        // 'COST','SELL'
        [Required, MaxLength(10)]
        public string ChangeType { get; set; } = "SELL";

        [Column(TypeName = "decimal(18,2)")]
        public decimal OldPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal NewPrice { get; set; }

        public DateTime ChangedAt { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }
    }
}
