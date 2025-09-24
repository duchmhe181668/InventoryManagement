using System;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Reservation
    {
        [Key] public int ReservationID { get; set; }

        public int LocationID { get; set; }
        public Location? Location { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        public decimal Quantity { get; set; }

        // 'Sales','Transfer'
        [Required, MaxLength(20)]
        public string Reason { get; set; } = "Sales";

        [Required, MaxLength(40)]
        public string RefTable { get; set; } = string.Empty;

        public int RefID { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
