using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Sale
    {
        [Key] public int SaleID { get; set; }

        public int StoreLocationID { get; set; }
        public Location? StoreLocation { get; set; }

        public int? CustomerID { get; set; }
        public Customer? Customer { get; set; }

        public int CreatedBy { get; set; }
        public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal Discount { get; set; }

        // 'Draft','Completed','Cancelled'
        [Required, MaxLength(20)]
        public string Status { get; set; } = "Completed";

        public ICollection<SaleLine>? Lines { get; set; }
    }
}
