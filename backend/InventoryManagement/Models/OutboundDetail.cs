using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    public class OutboundDetail
    {
        public int OutboundID { get; set; }
        public Outbound? Outbound { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        public decimal Quantity { get; set; }
        public decimal Total { get; set; }//
    }
}
