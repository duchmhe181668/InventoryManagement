using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Transfer
    {
        [Key] public int TransferID { get; set; }

        public int FromLocationID { get; set; }
        public Location? FromLocation { get; set; }

        public int ToLocationID { get; set; }
        public Location? ToLocation { get; set; }

        public int CreatedBy { get; set; }
        public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; }

        // 'Draft','Approved','Shipped','Received','Cancelled'
        [Required, MaxLength(20)]
        public string Status { get; set; } = "Draft";

        public ICollection<TransferItem>? Items { get; set; }
    }
}
