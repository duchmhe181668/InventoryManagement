using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Customer
    {
        [Key]
        public int CustomerID { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }

        public ICollection<Receipt>? Receipts { get; set; }

        //
    }
}
