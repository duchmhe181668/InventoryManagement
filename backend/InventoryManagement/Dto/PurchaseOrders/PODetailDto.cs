namespace InventoryManagement.Dto.PurchaseOrders
{
    public sealed class PODetailDto
    {
        public int POID { get; set; }
        public int SupplierID { get; set; }
        public string SupplierName { get; set; } = "";
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public List<POLineDto> Lines { get; set; } = new();
    }
}