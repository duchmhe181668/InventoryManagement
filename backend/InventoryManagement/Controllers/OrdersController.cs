using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Models.Views;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OrdersController(AppDbContext db)
        {
            _db = db;
        }

        // ========== LOOKUPS ===========

        [HttpGet("lookups/goods")]
        [Authorize(Roles = "WarehouseManager,StoreManager,Administrator")]
        public async Task<IActionResult> LookupGoods([FromQuery] string? q, [FromQuery] int? locationId, [FromQuery] int top = 20)
        {
            var query = _db.Goods.AsNoTracking().Select(g => new { g.GoodID, g.Name, g.Barcode, g.Unit });
            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim();
                query = query.Where(g => g.Name.Contains(key) || (g.Barcode ?? "").Contains(key));
            }
            var goods = await query.Take(top).ToListAsync();
            if (locationId == null) return Ok(goods);

            var goodIds = goods.Select(x => x.GoodID).ToList();
            var avail = await _db.Set<StockByGoodView>().AsNoTracking()
                .Where(v => v.LocationID == locationId && goodIds.Contains(v.GoodID))
                .Select(v => new { v.GoodID, v.Available }).ToListAsync();
            var map = avail.ToDictionary(x => x.GoodID, x => x.Available);
            var result = goods.Select(g => new { g.GoodID, g.Name, g.Barcode, g.Unit, Available = map.TryGetValue(g.GoodID, out var a) ? a : 0m });
            return Ok(result);
        }

        [HttpGet("lookups/locations")]
        [Authorize(Roles = "WarehouseManager,StoreManager,Administrator")]
        public async Task<IActionResult> LookupLocations([FromQuery] string? type)
        {
            var q = _db.Locations.AsNoTracking().Select(l => new { l.LocationID, l.Name, l.LocationType, l.ParentLocationID, l.IsActive });
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => x.LocationType == type);
            return Ok(await q.OrderBy(x => x.Name).ToListAsync());
        }

        [HttpGet("lookups/suppliers")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> LookupSuppliers([FromQuery] string? q, [FromQuery] int top = 30)
        {
            var s = _db.Suppliers.AsNoTracking().Select(x => new { x.SupplierID, x.Name, x.PhoneNumber, x.Email });
            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim();
                s = s.Where(x => x.Name.Contains(key));
            }
            return Ok(await s.OrderBy(x => x.Name).Take(top).ToListAsync());
        }

        // ========== STORE → WAREHOUSE (TRANSFERS) ==============
        #region Transfer DTOs
        public class TransferItemDto { [Required] public int GoodID { get; set; } public int? BatchID { get; set; } [Range(0.01, double.MaxValue)] public decimal Quantity { get; set; } }
        public class TransferCreateDto { [Required] public int FromLocationID { get; set; } [Required] public int ToLocationID { get; set; } [MinLength(1)] public List<TransferItemDto> Items { get; set; } = new(); }
        public class TransferUpdateDto { [Required] public int TransferID { get; set; } [MinLength(1)] public List<TransferItemDto> Items { get; set; } = new(); }
        public class TransferApproveDto { [Required] public int TransferID { get; set; } }
        public class TransferShipDto { [Required] public int TransferID { get; set; } public List<TransferShipLineDto>? Lines { get; set; } }
        public class TransferReceiveDto { [Required] public int TransferID { get; set; } public List<TransferReceiveLineDto>? Lines { get; set; } }
        public class TransferShipLineDto { [Required] public int GoodID { get; set; } [Required] public int BatchID { get; set; } [Range(0.01, double.MaxValue)] public decimal ShipQty { get; set; } }
        public class TransferReceiveLineDto { [Required] public int GoodID { get; set; } [Required] public int BatchID { get; set; } [Range(0.0001, double.MaxValue)] public decimal ReceiveQty { get; set; } }
        #endregion

        [HttpPost("transfers")]
        [Authorize(Roles = "StoreManager,WarehouseManager,Administrator")]
        public async Task<IActionResult> CreateTransfer([FromBody] TransferCreateDto dto)
        {
            // SỬA LỖI: Lấy UserID một cách an toàn từ token
            var username = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(username)) return Unauthorized("Token không hợp lệ.");
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return BadRequest("Người dùng từ token không tồn tại.");
            var userId = user.UserID;

            if (dto.FromLocationID == dto.ToLocationID) return BadRequest("From/To phải khác nhau.");
            var from = await _db.Locations.FindAsync(dto.FromLocationID);
            var to = await _db.Locations.FindAsync(dto.ToLocationID);
            if (from == null || to == null) return BadRequest("Location không hợp lệ.");

            using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var t = new Transfer { FromLocationID = dto.FromLocationID, ToLocationID = dto.ToLocationID, CreatedBy = userId, CreatedAt = DateTime.UtcNow, Status = "Draft" };
            _db.Transfers.Add(t);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.TransferItems.Add(new TransferItem { TransferID = t.TransferID, GoodID = it.GoodID, BatchID = it.BatchID, Quantity = it.Quantity, ShippedQty = 0, ReceivedQty = 0 });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        [HttpPut("transfers")]
        [Authorize(Roles = "StoreManager,WarehouseManager,Administrator")]
        public async Task<IActionResult> UpdateTransfer([FromBody] TransferUpdateDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Draft") return BadRequest("Chỉ sửa được khi Transfer đang Draft.");
            using var tx = await _db.Database.BeginTransactionAsync();
            _db.TransferItems.RemoveRange(t.Items ?? []);
            await _db.SaveChangesAsync();
            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.TransferItems.Add(new TransferItem { TransferID = t.TransferID, GoodID = it.GoodID, BatchID = it.BatchID, Quantity = it.Quantity, ShippedQty = 0, ReceivedQty = 0 });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        [HttpPost("transfers/approve")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> ApproveTransfer([FromBody] TransferApproveDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Draft") return BadRequest("Chỉ duyệt khi đang Draft.");
            if (t.Items == null || t.Items.Count == 0) return BadRequest("Transfer không có dòng.");
            if (t.Items.Any(i => i.BatchID == null)) return BadRequest("Mỗi dòng phải có BatchID trước khi duyệt.");

            using var tx = await _db.Database.BeginTransactionAsync();

            foreach (var it in t.Items)
            {
                var stock = await _db.Stocks.FirstOrDefaultAsync(s => s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (stock == null || (stock.OnHand - stock.Reserved) < it.Quantity)
                {
                    await tx.RollbackAsync();
                    return BadRequest($"Không đủ tồn khả dụng tại FromLocation cho Good={it.GoodID}, Batch={it.BatchID}.");
                }
                stock.Reserved += it.Quantity;
            }

            t.Status = "Approved";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        [HttpPost("transfers/ship")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> ShipTransfer([FromBody] TransferShipDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Approved" && t.Status != "Shipping") return BadRequest("Chỉ ship khi trạng thái Approved hoặc Shipping.");

            using var tx = await _db.Database.BeginTransactionAsync();
            var plan = new List<(TransferItem item, decimal shipQty)>();
            if (dto.Lines == null || dto.Lines.Count == 0)
            {
                foreach (var it in t.Items!) { if (it.BatchID == null) return BadRequest("BatchID là bắt buộc khi ship."); var remaining = it.Quantity - it.ShippedQty; if (remaining > 0) plan.Add((it, remaining)); }
            }
            else
            {
                foreach (var line in dto.Lines) { var it = t.Items!.FirstOrDefault(x => x.GoodID == line.GoodID && x.BatchID == line.BatchID); if (it == null) return BadRequest($"Dòng không khớp Transfer: Good={line.GoodID}, Batch={line.BatchID}."); var remaining = it.Quantity - it.ShippedQty; if (line.ShipQty <= 0 || line.ShipQty > remaining) return BadRequest($"ShipQty không hợp lệ (còn {remaining})."); plan.Add((it, line.ShipQty)); }
            }

            foreach (var (it, shipQty) in plan)
            {
                var from = await _db.Stocks.FirstOrDefaultAsync(s => s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (from == null) return BadRequest($"Không tìm thấy stock tại FromLocation cho Good={it.GoodID}, Batch={it.BatchID}.");
                from.OnHand -= shipQty; from.Reserved -= shipQty;

                var to = await _db.Stocks.FirstOrDefaultAsync(s => s.LocationID == t.ToLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (to == null) { to = new Stock { LocationID = t.ToLocationID, GoodID = it.GoodID, BatchID = it.BatchID!.Value, OnHand = 0, Reserved = 0, InTransit = 0 }; _db.Stocks.Add(to); }
                to.InTransit += shipQty;
                it.ShippedQty += shipQty;
            }

            t.Status = t.Items!.All(x => x.ShippedQty >= x.Quantity) ? "Shipped" : "Shipping";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        [HttpPost("transfers/receive")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> ReceiveTransfer([FromBody] TransferReceiveDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Shipped" && t.Status != "Shipping" && t.Status != "Receiving") return BadRequest("Chỉ nhận khi đã Ship hoặc đang nhận dở.");

            using var tx = await _db.Database.BeginTransactionAsync();
            var plan = new List<(TransferItem item, decimal recvQty)>();
            if (dto.Lines == null || dto.Lines.Count == 0)
            {
                foreach (var it in t.Items!) { if (it.BatchID == null) return BadRequest("BatchID là bắt buộc khi receive."); var remaining = it.ShippedQty - it.ReceivedQty; if (remaining > 0) plan.Add((it, remaining)); }
            }
            else
            {
                foreach (var line in dto.Lines) { var it = t.Items!.FirstOrDefault(x => x.GoodID == line.GoodID && x.BatchID == line.BatchID); if (it == null) return BadRequest($"Dòng không khớp Transfer: Good={line.GoodID}, Batch={line.BatchID}."); var remaining = it.ShippedQty - it.ReceivedQty; if (line.ReceiveQty <= 0 || line.ReceiveQty > remaining) return BadRequest($"ReceiveQty không hợp lệ (còn {remaining})."); plan.Add((it, line.ReceiveQty)); }
            }

            foreach (var (it, recvQty) in plan)
            {
                var to = await _db.Stocks.FirstOrDefaultAsync(s => s.LocationID == t.ToLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (to == null) return BadRequest($"Không tìm thấy stock ToLocation cho Good={it.GoodID}, Batch={it.BatchID}.");
                to.InTransit -= recvQty; to.OnHand += recvQty;
                it.ReceivedQty += recvQty;
            }

            t.Status = t.Items!.All(x => x.ReceivedQty >= x.Quantity) ? "Received" : "Receiving";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        [HttpGet("transfers/{id:int}")]
        [Authorize(Roles = "StoreManager,WarehouseManager,Administrator")]
        public async Task<IActionResult> GetTransfer(int id)
        {
            var t = await _db.Transfers.AsNoTracking()
                .Where(x => x.TransferID == id)
                .Select(x => new { x.TransferID, x.FromLocationID, x.ToLocationID, x.Status, x.CreatedBy, x.CreatedAt, Items = _db.TransferItems.Where(i => i.TransferID == x.TransferID).Select(i => new { i.GoodID, i.BatchID, i.Quantity, i.ShippedQty, i.ReceivedQty }).ToList() })
                .FirstOrDefaultAsync();
            return t == null ? NotFound() : Ok(t);
        }

        [HttpGet("transfers")]
        [Authorize(Roles = "StoreManager,WarehouseManager,Administrator")]
        public async Task<IActionResult> ListTransfers([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.Transfers.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
            var data = await q.OrderByDescending(t => t.TransferID)
                .Select(t => new { t.TransferID, t.Status, t.FromLocationID, t.ToLocationID, t.CreatedAt })
                .Take(top).ToListAsync();
            return Ok(data);
        }

        // ========== WAREHOUSE → SUPPLIER (PURCHASE ORDERS) ==========
        #region Purchase DTOs
        public class PurchaseLineDto { [Required] public int GoodID { get; set; } [Range(0.0001, double.MaxValue)] public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } = 0m; }
        public class PurchaseCreateDto { [Required] public int SupplierID { get; set; } [MinLength(1)] public List<PurchaseLineDto> Items { get; set; } = new(); [Required] public string Status { get; set; } }
        public class PurchaseUpdateDto { [Required] public int POID { get; set; } [MinLength(1)] public List<PurchaseLineDto> Items { get; set; } = new(); }
        public class PurchaseSubmitDto { [Required] public int POID { get; set; } }
        #endregion

        [HttpPost("purchase-orders")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> CreatePurchase([FromBody] PurchaseCreateDto dto)
        {
            // SỬA LỖI: Lấy UserID một cách an toàn từ token
            var username = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(username)) return Unauthorized("Token không hợp lệ.");
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return BadRequest("Người dùng từ token không tồn tại.");
            var userId = user.UserID;

            var sup = await _db.Suppliers.FindAsync(dto.SupplierID);
            if (sup == null) return BadRequest("Supplier không hợp lệ.");

            using var tx = await _db.Database.BeginTransactionAsync();
            var status = string.Equals(dto.Status, "Submitted", StringComparison.OrdinalIgnoreCase) ? "Submitted" : "Draft";
            var po = new PurchaseOrder { SupplierID = dto.SupplierID, CreatedBy = userId, CreatedAt = DateTime.UtcNow, Status = status };
            _db.PurchaseOrders.Add(po);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.PurchaseOrderLines.Add(new PurchaseOrderLine { POID = po.POID, GoodID = it.GoodID, Quantity = it.Quantity, UnitPrice = it.UnitPrice });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpPut("purchase-orders")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> UpdatePurchase([FromBody] PurchaseUpdateDto dto)
        {
            var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");
            if (po.Status != "Draft") return BadRequest("Chỉ sửa khi PO đang Draft.");

            using var tx = await _db.Database.BeginTransactionAsync();
            _db.PurchaseOrderLines.RemoveRange(po.Lines ?? []);
            await _db.SaveChangesAsync();
            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.PurchaseOrderLines.Add(new PurchaseOrderLine { POID = po.POID, GoodID = it.GoodID, Quantity = it.Quantity, UnitPrice = it.UnitPrice });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpPost("purchase-orders/submit")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> SubmitPurchase([FromBody] PurchaseSubmitDto dto)
        {
            var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");
            if (po.Status != "Draft") return BadRequest("Chỉ submit khi đang Draft.");
            if (po.Lines == null || po.Lines.Count == 0) return BadRequest("PO không có dòng.");

            po.Status = "Submitted";
            await _db.SaveChangesAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpGet("purchase-orders/{id:int}")]
        [Authorize(Roles = "WarehouseManager,Administrator,Supplier")]
        public async Task<IActionResult> GetPurchase(int id)
        {
            var p = await _db.PurchaseOrders.AsNoTracking()
                .Where(x => x.POID == id)
                .Select(x => new { x.POID, x.SupplierID, CreatedBy = x.CreatedBy, x.CreatedAt, x.Status, Lines = (from l in _db.PurchaseOrderLines join g in _db.Goods on l.GoodID equals g.GoodID where l.POID == x.POID select new { l.POLineID, l.GoodID, g.Name, g.Barcode, g.Unit, l.Quantity, l.UnitPrice }).ToList() })
                .FirstOrDefaultAsync();
            return p == null ? NotFound() : Ok(p);
        }

        [HttpGet("purchase-orders")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> ListPurchases([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.PurchaseOrders.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
            var data = await q.OrderByDescending(p => p.POID).Select(p => new { p.POID, p.SupplierID, p.Status, p.CreatedAt }).Take(top).ToListAsync();
            return Ok(data);
        }

        // === API MỚI ĐỂ LẤY DANH SÁCH TỔNG HỢP ===
        [HttpGet("all-orders")]
        [Authorize(Roles = "WarehouseManager,StoreManager,Administrator")]
        public async Task<IActionResult> ListAllOrders()
        {
            var purchases = await _db.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Select(p => new { Id = p.POID, Type = "Purchase Order", Status = p.Status, CreatedAt = p.CreatedAt, Details = $"Tới NCC: {p.Supplier.Name}" })
                .ToListAsync();

            var transfers = await _db.Transfers
                .AsNoTracking()
                .Include(t => t.FromLocation)
                .Include(t => t.ToLocation)
                .Select(t => new { Id = t.TransferID, Type = "Stock Transfer", Status = t.Status, CreatedAt = t.CreatedAt, Details = $"Từ: {t.FromLocation.Name} → Tới: {t.ToLocation.Name}" })
                .ToListAsync();

            var combinedList = purchases.Cast<dynamic>().Concat(transfers.Cast<dynamic>())
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            return Ok(combinedList);
        }
    }
}

