namespace InventoryManagement.Dto.ReceiptDto
{
    public class ReceiptViewDto
    {
        public int ReceiptID { get; set; }
        public int? POID { get; set; }
        public int? SupplierID { get; set; }
        public string SupplierName { get; set; }
        public int LocationID { get; set; }
        public int ReceivedBy { get; set; }
        public string Status { get; set; } = "Draft";

        public DateTime CreatedAt { get; set; }

        public List<ReceiptItemViewDto> Items { get; set; } = new();
    }
}
