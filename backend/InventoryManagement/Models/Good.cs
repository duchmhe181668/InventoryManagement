using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Models
{
    
    public class Good
    {
        [Key]
        public int GoodID { get; set; } // IDENTITY, KHÔNG nhận từ client khi POST

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Unit { get; set; } = string.Empty;

        public DateTime? DateIn { get; set; }

        public string? ImageURL { get; set; }

        // map đúng precision như DB
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceCost { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceSell { get; set; } = 0;

        // ===== THÊM CÁC KHÓA NGOẠI TƯƠNG ỨNG VỚI SCHEMA HIỆN TẠI =====

        // BẮT BUỘC theo DB: NOT NULL
        [Required]
        public int StoreID { get; set; }

        // CategoryID của bạn đang là NVARCHAR(200) ở DB -> để string?
        
        public string? CategoryID { get; set; }

        public int? SupplierID { get; set; }

        // (Tuỳ: nếu đã có các class bên dưới, bật navigation;
        // nếu chưa có thì có thể comment 3 dòng này để tránh lỗi compile)
        // public Store Store { get; set; } = null!;
        // public Category? Category { get; set; }
        // public Supplier? Supplier { get; set; }
    }
}