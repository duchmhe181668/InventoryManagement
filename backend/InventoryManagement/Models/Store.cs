using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Store
    {
        [Key]
        public int StoreID { get; set; }

        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }

        public ICollection<Good>? Goods { get; set; }
        public ICollection<OrderDetail>? OrderDetails { get; set; } //


    }
}
