using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Receipt
    {
        [Key] public int ReceiptID { get; set; }

        public int? POID { get; set; }
        public PurchaseOrder? PO { get; set; }

        public int? SupplierID { get; set; }
        public Supplier? Supplier { get; set; }

        public int LocationID { get; set; }
        public Location? Location { get; set; }

        public int ReceivedBy { get; set; }
        public User? ReceivedByUser { get; set; }

        public DateTime CreatedAt { get; set; }

        // 'Draft','Confirmed'
        [Required, MaxLength(20)]
        public string Status { get; set; } = "Confirmed";

        public ICollection<ReceiptDetail>? Details { get; set; }
    }
}
