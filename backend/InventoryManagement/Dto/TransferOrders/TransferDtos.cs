using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Dto.TransferOrders
{
    public class TransferItemDto
    {
        [Required] public int GoodID { get; set; }
        public int? BatchID { get; set; }
        [Range(0.01, double.MaxValue)] public decimal Quantity { get; set; }
    }

    public class TransferCreateDto
    {
        [Required] public int FromLocationID { get; set; }
        [Required] public int ToLocationID { get; set; }
        [MinLength(1)] public List<TransferItemDto> Items { get; set; } = new();
    }

    public class TransferUpdateDto
    {
        public int? TransferID { get; set; }
        [MinLength(1)] public List<TransferItemDto> Items { get; set; } = new();
    }

    public class TransferApproveDto { [Required] public int TransferID { get; set; } }
    public class TransferShipDto { [Required] public int TransferID { get; set; } public List<TransferShipLineDto>? Lines { get; set; } }
    public class TransferReceiveDto { [Required] public int TransferID { get; set; } public List<TransferReceiveLineDto>? Lines { get; set; } }
    public class TransferShipLineDto { [Required] public int GoodID { get; set; } [Required] public int BatchID { get; set; } [Range(0.01, double.MaxValue)] public decimal ShipQty { get; set; } }
    public class TransferReceiveLineDto { [Required] public int GoodID { get; set; } [Required] public int BatchID { get; set; } [Range(0.0001, double.MaxValue)] public decimal ReceiveQty { get; set; } }
    public class TransferInvoiceCreateDto
    {
        [MinLength(1)] public List<TransferInvoiceLineDto> Lines { get; set; } = new();
    }
    public class TransferInvoiceLineDto
    {
        [Required] public int GoodId { get; set; }
        [Range(0.0001, double.MaxValue)] public decimal Qty { get; set; }
        [Range(0.0, double.MaxValue)] public decimal UnitPrice { get; set; }
        public int? BatchId { get; set; }
    }
    public class TransferInvoiceReceiveDto
    {
        [MinLength(1)] public List<TransferInvoiceReceiveLineDto> Lines { get; set; } = new();
    }
    public class TransferInvoiceReceiveLineDto
    {
        [Required] public int ReceiptDetailId { get; set; }
        [Range(0.0, double.MaxValue)] public decimal AcceptQty { get; set; }
    }
}
