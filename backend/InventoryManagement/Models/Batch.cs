using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class Batch
    {
        [Key] public int BatchID { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Required, MaxLength(100)]
        public string BatchNo { get; set; } = string.Empty;

        public System.DateTime? ExpiryDate { get; set; }

        public ICollection<Stock>? Stocks { get; set; }
    }
}
