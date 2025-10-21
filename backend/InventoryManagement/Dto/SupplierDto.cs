namespace InventoryManagement.Dto
{
    public class PagedResult<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public IEnumerable<T> Items { get; set; } = new List<T>();
    }

    public class SupplierListItemDto
    {
        public int SupplierID { get; set; }
        public string Name { get; set; } = "";
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }

        // Thông tin tổng hợp để sort/filter
        public DateTime? LastPODate { get; set; }
        public int POCount { get; set; }
        public int ReceiptCount { get; set; }
        public decimal TotalSpend { get; set; } // Sum(ReceiptDetail.UnitCost * Quantity) của các Receipt Confirmed
    }

    public class POSummaryDto
    {
        public int POID { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "";
        public int LineCount { get; set; }
        public decimal TotalQty { get; set; }
    }

    public class ReceiptSummaryDto
    {
        public int ReceiptID { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "";
        public int DetailCount { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalCost { get; set; }
    }

    public class SupplierDetailDto
    {
        public int SupplierID { get; set; }
        public string Name { get; set; } = "";
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }

        // Tóm tắt
        public DateTime? LastPODate { get; set; }
        public int POCount { get; set; }
        public int ReceiptCount { get; set; }
        public decimal TotalSpend { get; set; }

        // Tabs
        public IEnumerable<POSummaryDto> PurchaseOrders { get; set; } = new List<POSummaryDto>();
        public IEnumerable<ReceiptSummaryDto> Receipts { get; set; } = new List<ReceiptSummaryDto>();
    }
}
