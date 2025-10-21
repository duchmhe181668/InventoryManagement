namespace InventoryManagement.Dto.ReceiptDto
{
    public class ReceiptListItemDto
    {
        public int ReceiptID { get; set; }
        public int? POID { get; set; }
        public int? SupplierID { get; set; }
        public int LocationID { get; set; }
        public int ReceivedBy { get; set; }
        public string Status { get; set; } = "Draft";
        public DateTime CreatedAt { get; set; }

        // tùy ý hiển thị nhanh
        public int TotalLines { get; set; }
        public decimal TotalAmount { get; set; } // Sum(Quantity*UnitCost)
    }
}
