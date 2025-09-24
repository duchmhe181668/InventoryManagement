using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Store
    {
        [Key] public int StoreID { get; set; }

        public int LocationID { get; set; }
        public Location? Location { get; set; }

        [MaxLength(50)] public string? PhoneNumber { get; set; }
        [MaxLength(300)] public string? Address { get; set; }

        public ICollection<StorePrice>? StorePrices { get; set; }
    }
}
