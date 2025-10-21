using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Dto.ReceiptDto
{
    public class CreateReceiptItemDto
    {
        [Required] public int GoodID { get; set; }

        // Nếu không gửi, server sẽ lấy mặc định bằng QuantityOrdered trong PO
        public decimal? Quantity { get; set; }

        [Required] public decimal UnitPrice { get; set; } 

        [Required, MaxLength(100)] public string BatchNo { get; set; } = string.Empty;

        [Required] public DateTime ExpiryDate { get; set; }
    }
}
