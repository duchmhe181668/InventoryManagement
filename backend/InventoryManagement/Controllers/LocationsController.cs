using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
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
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OrdersController(AppDbContext db)
        {
            _db = db;
        }


        // ========== LOOKUPS ===========


        /// Tìm goods (theo tên/Barcode). Nếu truyền locationId sẽ trả kèm Available.
        [HttpGet("lookups/goods")]
        public async Task<IActionResult> LookupGoods([FromQuery] string? q, [FromQuery] int? locationId, [FromQuery] int top = 20)
        {
            var query = _db.Goods.AsNoTracking()
                .Select(g => new { g.GoodID, g.Name, g.Barcode, g.Unit });

            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim();
                query = query.Where(g =>
                    g.Name.Contains(key) ||
                    (g.Barcode ?? "").Contains(key));
            }

            var goods = await query.Take(top).ToListAsync();
            if (locationId == null) return Ok(goods);

            var goodIds = goods.Select(x => x.GoodID).ToList();
            var avail = await _db.Set<StockByGoodView>().AsNoTracking()
                .Where(v => v.LocationID == locationId && goodIds.Contains(v.GoodID))
                .Select(v => new { v.GoodID, v.Available })
                .ToListAsync();
            var map = avail.ToDictionary(x => x.GoodID, x => x.Available);

            var result = goods.Select(g => new
            {
                g.GoodID,
                g.Name,
                g.Barcode,
                g.Unit,
                Available = map.TryGetValue(g.GoodID, out var a) ? a : 0m
            });

            return Ok(result);
        }


        /// Danh sách locations (lọc theo type nếu cần: WAREHOUSE/STORE/BIN).
        [HttpGet("lookups/locations")]
        public async Task<IActionResult> LookupLocations([FromQuery] string? type)
        {
            var q = _db.Locations.AsNoTracking().Select(l => new
            {
                l.LocationID,
                l.Name,
                l.LocationType,
                l.ParentLocationID,
                l.IsActive
            });
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => x.LocationType == type);
            return Ok(await q.OrderBy(x => x.Name).ToListAsync());
        }

        /// Danh sách supplier.
        [HttpGet("lookups/suppliers")]
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
        public class TransferItemDto
        {
            [Required] public int GoodID { get; set; }
            public int? BatchID { get; set; } // ⚠️ BatchID bắt buộc ở bước duyệt/ship/receive
            [Range(0.0001, double.MaxValue)] public decimal Quantity { get; set; }
        }
        public class TransferCreateDto
        {
            [Required] public int FromLocationID { get; set; } // STORE
            [Required] public int ToLocationID { get; set; }   // WAREHOUSE (hoặc ngược lại tuỳ flow)
            [Required] public int CreatedBy { get; set; }      // UserID
            [MinLength(1)] public List<TransferItemDto> Items { get; set; } = new();
        }
        public class TransferUpdateDto
        {
            [Required] public int TransferID { get; set; }
            [MinLength(1)] public List<TransferItemDto> Items { get; set; } = new();
        }
        public class TransferApproveDto { [Required] public int TransferID { get; set; } }
        public class TransferShipLineDto
        {
            [Required] public int GoodID { get; set; }
            [Required] public int BatchID { get; set; }
            [Range(0.0001, double.MaxValue)] public decimal ShipQty { get; set; }
        }
        public class TransferShipDto
        {
            [Required] public int TransferID { get; set; }
            // Nếu không truyền Lines: mặc định ship hết (Quantity - ShippedQty) từng dòng
            public List<TransferShipLineDto>? Lines { get; set; }
        }
        public class TransferReceiveLineDto
        {
            [Required] public int GoodID { get; set; }
            [Required] public int BatchID { get; set; }
            [Range(0.0001, double.MaxValue)] public decimal ReceiveQty { get; set; }
        }
        public class TransferReceiveDto
        {
            [Required] public int TransferID { get; set; }
            // Nếu không truyền Lines: mặc định nhận hết (ShippedQty - ReceivedQty) từng dòng
            public List<TransferReceiveLineDto>? Lines { get; set; }
        }
        #endregion

        [HttpPost("transfers")]
        public async Task<IActionResult> CreateTransfer([FromBody] TransferCreateDto dto)
        {
            if (dto.FromLocationID == dto.ToLocationID) return BadRequest("From/To phải khác nhau.");

            var from = await _db.Locations.FindAsync(dto.FromLocationID);
            var to = await _db.Locations.FindAsync(dto.ToLocationID);
            var user = await _db.Users.FindAsync(dto.CreatedBy);
            if (from == null || to == null) return BadRequest("Location không hợp lệ.");
            if (user == null) return BadRequest("CreatedBy (UserID) không hợp lệ.");

            using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var t = new Transfer
            {
                FromLocationID = dto.FromLocationID,
                ToLocationID = dto.ToLocationID,
                CreatedBy = dto.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                Status = "Draft"
            };
            _db.Transfers.Add(t);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.TransferItems.Add(new TransferItem
                {
                    TransferID = t.TransferID,
                    GoodID = it.GoodID,
                    BatchID = it.BatchID,     // có thể null khi đang Draft
                    Quantity = it.Quantity,
                    ShippedQty = 0,
                    ReceivedQty = 0
                });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { t.TransferID, t.Status });
        }

        [HttpPut("transfers")]
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
                _db.TransferItems.Add(new TransferItem
                {
                    TransferID = t.TransferID,
                    GoodID = it.GoodID,
                    BatchID = it.BatchID,
                    Quantity = it.Quantity,
                    ShippedQty = 0,
                    ReceivedQty = 0
                });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { t.TransferID, t.Status });
        }

        /// Duyệt Transfer: chuyển Draft → Approved và **reserve** tồn ở FromLocation.
        /// ⚠️ Yêu cầu mọi dòng phải có BatchID để có thể reserve chính xác.
        [HttpPost("transfers/approve")]
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
                // Kiểm tra available (OnHand - Reserved) tại FromLocation
                var stock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);

                var available = stock != null ? (stock.OnHand - stock.Reserved) : 0m;
                if (available < it.Quantity)
                    return BadRequest($"Không đủ tồn khả dụng tại FromLocation cho Good={it.GoodID}, Batch={it.BatchID}. Cần {it.Quantity}, còn {available}.");
            }

            // Reserve
            foreach (var it in t.Items)
            {
                var stock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (stock == null)
                {
                    stock = new Stock
                    {
                        LocationID = t.FromLocationID,
                        GoodID = it.GoodID,
                        BatchID = it.BatchID!.Value,
                        OnHand = 0,
                        Reserved = 0,
                        InTransit = 0
                    };
                    _db.Stocks.Add(stock);
                }
                stock.Reserved += it.Quantity;
            }

            t.Status = "Approved";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        /// Xuất kho (Ship): trừ OnHand & Reserved tại From, +InTransit tại To. Cho phép giao 1 phần.
        [HttpPost("transfers/ship")]
        public async Task<IActionResult> ShipTransfer([FromBody] TransferShipDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Approved" && t.Status != "Shipped") return BadRequest("Chỉ ship khi trạng thái Approved/Shipped.");

            using var tx = await _db.Database.BeginTransactionAsync();

            // Chuẩn hoá Lines: nếu không truyền thì ship phần còn lại
            var plan = new List<(TransferItem item, decimal shipQty)>();
            if (dto.Lines == null || dto.Lines.Count == 0)
            {
                foreach (var it in t.Items!)
                {
                    if (it.BatchID == null) return BadRequest("BatchID là bắt buộc khi ship.");
                    var remaining = it.Quantity - it.ShippedQty;
                    if (remaining > 0) plan.Add((it, remaining));
                }
            }
            else
            {
                foreach (var line in dto.Lines)
                {
                    var it = t.Items!.FirstOrDefault(x => x.GoodID == line.GoodID && x.BatchID == line.BatchID);
                    if (it == null) return BadRequest($"Dòng không khớp Transfer: Good={line.GoodID}, Batch={line.BatchID}.");
                    var remaining = it.Quantity - it.ShippedQty;
                    if (line.ShipQty <= 0 || line.ShipQty > remaining)
                        return BadRequest($"ShipQty không hợp lệ (còn {remaining}).");
                    plan.Add((it, line.ShipQty));
                }
            }

            // Cập nhật tồn
            foreach (var (it, shipQty) in plan)
            {
                var from = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (from == null) return BadRequest($"Không tìm thấy stock tại FromLocation cho Good={it.GoodID}, Batch={it.BatchID}.");

                from.OnHand -= shipQty;
                from.Reserved -= shipQty;

                var to = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.ToLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (to == null)
                {
                    to = new Stock
                    {
                        LocationID = t.ToLocationID,
                        GoodID = it.GoodID,
                        BatchID = it.BatchID!.Value,
                        OnHand = 0,
                        Reserved = 0,
                        InTransit = 0
                    };
                    _db.Stocks.Add(to);
                }
                to.InTransit += shipQty;

                it.ShippedQty += shipQty;
            }

            // Nếu tất cả đã ship đủ thì trạng thái = Shipped
            if (t.Items!.All(x => x.ShippedQty >= x.Quantity))
                t.Status = "Shipped";
            else
                t.Status = "Approved";

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        /// Nhập kho (Receive): -InTransit & +OnHand tại To. Cho phép nhận 1 phần.
        [HttpPost("transfers/receive")]
        public async Task<IActionResult> ReceiveTransfer([FromBody] TransferReceiveDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Shipped" && t.Status != "Approved") return BadRequest("Chỉ nhận khi đã Ship/Approved.");

            using var tx = await _db.Database.BeginTransactionAsync();

            var plan = new List<(TransferItem item, decimal recvQty)>();
            if (dto.Lines == null || dto.Lines.Count == 0)
            {
                foreach (var it in t.Items!)
                {
                    if (it.BatchID == null) return BadRequest("BatchID là bắt buộc khi receive.");
                    var remaining = it.ShippedQty - it.ReceivedQty;
                    if (remaining > 0) plan.Add((it, remaining));
                }
            }
            else
            {
                foreach (var line in dto.Lines)
                {
                    var it = t.Items!.FirstOrDefault(x => x.GoodID == line.GoodID && x.BatchID == line.BatchID);
                    if (it == null) return BadRequest($"Dòng không khớp Transfer: Good={line.GoodID}, Batch={line.BatchID}.");
                    var remaining = it.ShippedQty - it.ReceivedQty;
                    if (line.ReceiveQty <= 0 || line.ReceiveQty > remaining)
                        return BadRequest($"ReceiveQty không hợp lệ (còn {remaining}).");
                    plan.Add((it, line.ReceiveQty));
                }
            }

            foreach (var (it, recvQty) in plan)
            {
                var to = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.ToLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (to == null) return BadRequest($"Không tìm thấy stock ToLocation cho Good={it.GoodID}, Batch={it.BatchID}.");

                to.InTransit -= recvQty;
                to.OnHand += recvQty;

                it.ReceivedQty += recvQty;
            }

            // Nếu đã nhận đủ tất cả -> Received
            if (t.Items!.All(x => x.ReceivedQty >= x.Quantity))
                t.Status = "Received";

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        [HttpGet("transfers/{id:int}")]
        public async Task<IActionResult> GetTransfer(int id)
        {
            var t = await _db.Transfers.AsNoTracking()
                .Where(x => x.TransferID == id)
                .Select(x => new
                {
                    x.TransferID,
                    x.FromLocationID,
                    x.ToLocationID,
                    x.Status,
                    x.CreatedBy,
                    x.CreatedAt,
                    Items = _db.TransferItems.Where(i => i.TransferID == x.TransferID)
                        .Select(i => new { i.GoodID, i.BatchID, i.Quantity, i.ShippedQty, i.ReceivedQty })
                        .ToList()
                }).FirstOrDefaultAsync();
            return t == null ? NotFound() : Ok(t);
        }

        [HttpGet("transfers")]
        public async Task<IActionResult> ListTransfers([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.Transfers.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
            var data = await q.OrderByDescending(t => t.TransferID)
                .Select(t => new { t.TransferID, t.Status, t.FromLocationID, t.ToLocationID, t.CreatedAt })
                .Take(top).ToListAsync();
            return Ok(data);
        }

        // ============================================================
        // ========== WAREHOUSE → SUPPLIER (PURCHASE ORDERS) ==========
        // ============================================================

        #region Purchase DTOs
        public class PurchaseLineDto
        {
            [Required] public int GoodID { get; set; }
            [Range(0.0001, double.MaxValue)] public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; } = 0m; // theo yêu cầu hiện tại
        }
        public class PurchaseCreateDto
        {
            [Required] public int SupplierID { get; set; }
            [Required] public int CreatedBy { get; set; }
            [MinLength(1)] public List<PurchaseLineDto> Items { get; set; } = new();
        }
        public class PurchaseUpdateDto
        {
            [Required] public int POID { get; set; }
            [MinLength(1)] public List<PurchaseLineDto> Items { get; set; } = new();
        }
        public class PurchaseSubmitDto { [Required] public int POID { get; set; } }
        #endregion

        [HttpPost("purchase-orders")]
        public async Task<IActionResult> CreatePurchase([FromBody] PurchaseCreateDto dto)
        {
            var sup = await _db.Suppliers.FindAsync(dto.SupplierID);
            var user = await _db.Users.FindAsync(dto.CreatedBy);
            if (sup == null) return BadRequest("Supplier không hợp lệ.");
            if (user == null) return BadRequest("CreatedBy (UserID) không hợp lệ.");

            using var tx = await _db.Database.BeginTransactionAsync();

            var po = new PurchaseOrder
            {
                SupplierID = dto.SupplierID,
                CreatedBy = dto.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                Status = "Draft"
            };
            _db.PurchaseOrders.Add(po);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.PurchaseOrderLines.Add(new PurchaseOrderLine
                {
                    POID = po.POID,
                    GoodID = it.GoodID,
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice
                });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { po.POID, po.Status });
        }

        [HttpPut("purchase-orders")]
        public async Task<IActionResult> UpdatePurchase([FromBody] PurchaseUpdateDto dto)
        {
            var po = await _db.PurchaseOrders.Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");
            if (po.Status != "Draft") return BadRequest("Chỉ sửa khi PO đang Draft.");

            using var tx = await _db.Database.BeginTransactionAsync();
            _db.PurchaseOrderLines.RemoveRange(po.Lines ?? []);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.PurchaseOrderLines.Add(new PurchaseOrderLine
                {
                    POID = po.POID,
                    GoodID = it.GoodID,
                    Quantity = it.Quantity,
                    UnitPrice = it.UnitPrice
                });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { po.POID, po.Status });
        }

        [HttpPost("purchase-orders/submit")]
        public async Task<IActionResult> SubmitPurchase([FromBody] PurchaseSubmitDto dto)
        {
            var po = await _db.PurchaseOrders.Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");
            if (po.Status != "Draft") return BadRequest("Chỉ submit khi đang Draft.");
            if (po.Lines == null || po.Lines.Count == 0) return BadRequest("PO không có dòng.");

            po.Status = "Submitted";
            await _db.SaveChangesAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpGet("purchase-orders/{id:int}")]
        public async Task<IActionResult> GetPurchase(int id)
        {
            var p = await _db.PurchaseOrders.AsNoTracking()
                .Where(x => x.POID == id)
                .Select(x => new
                {
                    x.POID,
                    x.SupplierID,
                    x.CreatedBy,
                    x.CreatedAt,
                    x.Status,
                    Lines = (from l in _db.PurchaseOrderLines
                     join g in _db.Goods on l.GoodID equals g.GoodID
                     where l.POID == x.POID
                     select new {
                         l.POLineID,
                         l.GoodID,
                         g.Name,
                         g.Barcode,
                         g.Unit,          
                         l.Quantity,
                         l.UnitPrice
                     }).ToList()
        })
        .FirstOrDefaultAsync();
            return p == null ? NotFound() : Ok(p);
        }

        [HttpGet("purchase-orders")]
        public async Task<IActionResult> ListPurchases([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.PurchaseOrders.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
            var data = await q.OrderByDescending(p => p.POID)
                .Select(p => new { p.POID, p.SupplierID, p.Status, p.CreatedAt })
                .Take(top).ToListAsync();
            return Ok(data);
        }
    }
}
