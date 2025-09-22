using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class Receipt
    {
        [Key]
        public int ReceiptID { get; set; }

        [ForeignKey("Staff")]
        public int StaffID { get; set; }
        public User? Staff { get; set; }

        [ForeignKey("Customer")]
        public int? CustomerID { get; set; }
        public Customer? Customer { get; set; }

        public decimal Discount { get; set; } = 0;
        public DateTime CreatedAt { get; set; }

        public ICollection<ReceiptDetail>? ReceiptDetails { get; set; }
    }
}
//