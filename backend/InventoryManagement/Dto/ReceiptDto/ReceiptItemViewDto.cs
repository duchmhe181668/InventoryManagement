namespace InventoryManagement.Dto.ReceiptDto
{
    public class ReceiptItemViewDto
    {
        public int ReceiptDetailID { get; set; }
        public int GoodID { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }

        public int? BatchID { get; set; }
        public string? BatchNo { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }
}
