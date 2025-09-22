using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Report
    {
        [Key]
        public DateTime Date { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
    }
}
//