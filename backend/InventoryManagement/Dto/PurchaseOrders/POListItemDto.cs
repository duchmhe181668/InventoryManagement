namespace InventoryManagement.Dto.PurchaseOrders
{
    public sealed class POListItemDto
    {
        public int POID { get; set; }
        public int SupplierID { get; set; }
        public string SupplierName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "";
        public int TotalLines { get; set; }
        public decimal TotalAmount { get; set; }   // Sum(Quantity*UnitPrice)
    }
}
