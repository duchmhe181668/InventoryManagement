using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Customer
    {
        [Key] public int CustomerID { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)] public string? PhoneNumber { get; set; }
        [MaxLength(150)] public string? Email { get; set; }
    }
}
