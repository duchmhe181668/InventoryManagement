using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models
{
    public class ReturnOrder
    {
        [Key] public int ReturnID { get; set; }

        public int ReceiptID { get; set; }
        [ForeignKey(nameof(ReceiptID))] public Receipt? Receipt { get; set; }

        public int SupplierID { get; set; }
        [ForeignKey(nameof(SupplierID))] public Supplier? Supplier { get; set; }

        public int CreatedBy { get; set; }
        [ForeignKey(nameof(CreatedBy))] public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(30)] public string Status { get; set; }

        public int? ConfirmedBy { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        [MaxLength(300)] public string? Note { get; set; }

        public ICollection<ReturnDetail>? Details { get; set; }
    }
}
