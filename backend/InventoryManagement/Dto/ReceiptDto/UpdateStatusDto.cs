namespace InventoryManagement.Dto.ReceiptDto
{
    public class UpdateStatusDto
    {
        public string Status { get; set; } = string.Empty;
        public int? ReceivedBy { get; set; }
    }
}
