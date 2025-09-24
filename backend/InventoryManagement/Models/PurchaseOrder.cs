using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class PurchaseOrder
    {
        [Key] public int POID { get; set; }

        public int SupplierID { get; set; }
        public Supplier? Supplier { get; set; }

        public int CreatedBy { get; set; }
        public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; }

        // 'Draft','Submitted','Received','Cancelled'
        [Required, MaxLength(20)]
        public string Status { get; set; } = "Draft";

        public ICollection<PurchaseOrderLine>? Lines { get; set; }
    }
}
