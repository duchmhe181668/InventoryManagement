using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class OrderDetail
    {

        public int OrderID { get; set; }
        public Order? Order { get; set; }


        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [ForeignKey("Store")]
        public int StoreID { get; set; }
        public Store? Store { get; set; }

        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
        //
    }
}
