namespace InventoryManagement.Dto.ReceiptDto
{
    public class ReceiptViewDto
    {
        public int ReceiptID { get; set; }
        public int? POID { get; set; }
        public int? SupplierID { get; set; }
        public int LocationID { get; set; }
        public int ReceivedBy { get; set; }

        /// <summary>Draft | Confirmed | ... tùy hệ thống của bạn (nếu đang dùng Draft/Submitted/Received thì điền đúng các giá trị đó)</summary>
        public string Status { get; set; } = "Draft";

        public DateTime CreatedAt { get; set; }

        public List<ReceiptItemViewDto> Items { get; set; } = new();
    }
}
