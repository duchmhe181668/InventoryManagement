using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class Good
    {
        [Key]
        public int GoodID { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Unit { get; set; } = string.Empty;

        public DateTime? DateIn { get; set; }
        public string? ImageURL { get; set; }

        public decimal Quantity { get; set; } = 0;
        public decimal PriceCost { get; set; } = 0;
        public decimal PriceSell { get; set; } = 0;

        [ForeignKey("Category")]
        public int? CategoryID { get; set; }
        public Category? Category { get; set; }

        [ForeignKey("Store")]
        public int StoreID { get; set; }
        public Store? Store { get; set; }

        [ForeignKey("Supplier")]
        public int? SupplierID { get; set; }
        public Supplier? Supplier { get; set; }
    }
}