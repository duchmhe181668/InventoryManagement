using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Adjustment
    {
        [Key] public int AdjustmentID { get; set; }

        public int LocationID { get; set; }
        public Location? Location { get; set; }

        public int CreatedBy { get; set; }
        public User? CreatedByUser { get; set; }

        // 'Expired','Damage','CountDiff','PriceUpdate','Other'
        [Required, MaxLength(30)]
        public string Reason { get; set; } = "Other";

        public DateTime CreatedAt { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }

        public ICollection<AdjustmentLine>? Lines { get; set; }
    }
}
