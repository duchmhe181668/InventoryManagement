using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Dto.PurchaseOrders
{
    public class PurchaseLineDto { [Required] public int GoodID { get; set; } [Range(0.0001, double.MaxValue)] public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } = 0m; }
    public class PurchaseCreateDto { [Required] public int SupplierID { get; set; } [MinLength(1)] public List<PurchaseLineDto> Items { get; set; } = new(); [Required] public string Status { get; set; } }
    public class PurchaseUpdateDto { [Required] public int POID { get; set; } [MinLength(1)] public List<PurchaseLineDto> Items { get; set; } = new(); }
    public class PurchaseSubmitDto { [Required] public int POID { get; set; } }
}