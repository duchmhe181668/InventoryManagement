using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class Good
    {
        [Key] public int GoodID { get; set; }

        [Required, MaxLength(64)]
        public string SKU { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Unit { get; set; } = string.Empty;

        [MaxLength(64)] public string? Barcode { get; set; }
        [MaxLength(500)] public string? ImageURL { get; set; }

        [Column(TypeName = "decimal(18,2)")] public decimal PriceCost { get; set; }
        [Column(TypeName = "decimal(18,2)")] public decimal PriceSell { get; set; }

        [ForeignKey("Category")]
        public int? CategoryID { get; set; }
        public Category? Category { get; set; }

        [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // 🔹 Navigation properties
        public ICollection<Batch>? Batches { get; set; }
        public ICollection<Stock>? Stocks { get; set; }
        public ICollection<StockMovement>? StockMovements { get; set; }   // 👈 thêm dòng này
    }
}
