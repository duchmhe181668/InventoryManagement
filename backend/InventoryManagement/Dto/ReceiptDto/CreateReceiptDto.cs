using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Dto.ReceiptDto
{
    public class CreateReceiptDto
    {
        [Required] public int POID { get; set; }

        // Nếu bạn vẫn đang lấy supplierId từ FE, giữ field này; nếu đã gắn vào JWT claim thì bỏ.
        public int? SupplierId { get; set; }

        [Required] public List<CreateReceiptItemDto> Items { get; set; } = new();
    }
}
