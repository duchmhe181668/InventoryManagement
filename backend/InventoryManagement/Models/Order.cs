using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class Order
    {
        [Key]
        public int OrderID { get; set; }

        [ForeignKey("Manager")]
        public int ManagerID { get; set; }
        public User? Manager { get; set; }

        [ForeignKey("Supplier")]
        public int SupplierID { get; set; }
        public Supplier? Supplier { get; set; }

        public DateTime CreatedAt { get; set; }

        public ICollection<OrderDetail>? OrderDetails { get; set; }
        //
    }
}
