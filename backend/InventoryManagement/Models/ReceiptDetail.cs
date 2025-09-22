using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class ReceiptDetail
    {
 
        public int ReceiptID { get; set; }
        public Receipt? Receipt { get; set; }


        public int GoodID { get; set; }//
        public Good? Good { get; set; }

        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
    }
}
