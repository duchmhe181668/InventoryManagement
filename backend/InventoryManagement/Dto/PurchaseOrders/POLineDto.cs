namespace InventoryManagement.Dto.PurchaseOrders
{
    public sealed class POLineDto
    {
        public int POLineID { get; set; }
        public int GoodID { get; set; }
        public string GoodName { get; set; } = "";
        public string Unit { get; set; } = "";
        public string? SKU { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineAmount => Quantity * UnitPrice;
    }
}
