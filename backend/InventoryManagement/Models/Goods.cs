using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Good
    {
        [Key]
        public int GoodID { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Unit { get; set; } = string.Empty;

        public DateTime? DateIn { get; set; }

        public string? ImageURL { get; set; }

        public decimal Quantity { get; set; } = 0;

        public decimal PriceCost { get; set; } = 0;

        public decimal PriceSell { get; set; } = 0;
    }
}
