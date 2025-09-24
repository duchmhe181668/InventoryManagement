using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Location
    {
        [Key] public int LocationID { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        // 'WAREHOUSE','STORE','BIN'
        [Required, MaxLength(20)]
        public string LocationType { get; set; } = "WAREHOUSE";

        public int? ParentLocationID { get; set; }
        public Location? Parent { get; set; }
        public ICollection<Location>? Children { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Stock>? Stocks { get; set; }
    }
}
