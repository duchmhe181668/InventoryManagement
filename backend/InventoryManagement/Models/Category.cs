using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Category
    {
        [Key]
        public string CategoryID { get; set; }

        [Required]
        [MaxLength(200)]
        public string CategoryName { get; set; }

        public ICollection<Good> Goods { get; set; }
    }
}
