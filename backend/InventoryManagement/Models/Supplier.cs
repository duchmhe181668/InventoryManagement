using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Supplier
    {
        [Key] public int SupplierID { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)] public string? PhoneNumber { get; set; }
        [MaxLength(150)] public string? Email { get; set; }
        [MaxLength(300)] public string? Address { get; set; }

        public ICollection<PurchaseOrder>? PurchaseOrders { get; set; }
    }
}
