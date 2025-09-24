namespace InventoryManagement.Models.Views
{
    // Keyless entity mapping for view dbo.v_StockByGood
    public class StockByGoodView
    {
        public int LocationID { get; set; }
        public int GoodID { get; set; }
        public decimal OnHand { get; set; }
        public decimal Reserved { get; set; }
        public decimal InTransit { get; set; }
        public decimal Available { get; set; }
    }
}
