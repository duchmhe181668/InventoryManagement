using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    // PK: StoreID + GoodID + EffectiveFrom
    public class StorePrice
    {
        public int StoreID { get; set; }
        public Store? Store { get; set; }

        public int GoodID { get; set; }
        public Good? Good { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceSell { get; set; }

        // date
        public DateTime EffectiveFrom { get; set; }
    }
}
