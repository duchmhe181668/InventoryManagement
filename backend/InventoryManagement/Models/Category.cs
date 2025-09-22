using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Category
    {
        [Key]
        public int CategoryID { get; set; }

        [Required]
        public string CategoryName { get; set; } = string.Empty;

        public ICollection<Good>? Goods { get; set; }
        //
    }
}
