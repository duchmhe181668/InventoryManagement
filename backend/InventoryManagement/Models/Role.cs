using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Role
    {
        [Key]
        public int RoleID { get; set; }

        [Required, MaxLength(50)]
        public string RoleName { get; set; } = string.Empty;

        public ICollection<User>? Users { get; set; }
    }
}
