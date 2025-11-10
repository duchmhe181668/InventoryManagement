using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Dto.TransferOrders; 

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/transfers")]
    [Authorize(Roles = "StoreManager,WarehouseManager,Administrator")]
    public class TransfersController : ControllerBase
    {
        private readonly AppDbContext _db;
        public TransfersController(AppDbContext db) { _db = db; }

        // ========= Helpers =========
        private static DateTime GetVietnamTime() => DateTime.UtcNow;

        private int GetUserId()
        {
            // === SỬA ĐỔI: Đọc claim "user_id" (mà JwtService đã tạo) ===
            var s = User.FindFirstValue("user_id"); 
            return int.TryParse(s, out var id) ? id : 0;
        }

        private string GetUserName()
        {
            return User.Identity?.Name
                   ?? User.FindFirstValue("preferred_username")
                   ?? $"user#{GetUserId()}";
        }

        // === SỬA ĐỔI: Chuyển sang ToLower() để EF Core dịch được ===
        private static bool IsWarehouse(Location l)
            => l.LocationType != null && l.LocationType.ToLower() == "warehouse";
        private static bool IsStore(Location l)
            => l.LocationType != null && l.LocationType.ToLower() == "store";

        private async Task AddOnHandAsync(int locationId, int goodId, int? batchId, decimal delta)
        {
            var b = batchId ?? 0;
            var s = await _db.Stocks.FirstOrDefaultAsync(x =>
                x.LocationID == locationId && x.GoodID == goodId && x.BatchID == b);

            if (s == null)
            {
                s = new Stock { LocationID = locationId, GoodID = goodId, BatchID = b, OnHand = 0, Reserved = 0, InTransit = 0 };
                _db.Stocks.Add(s);
            }

            s.OnHand += delta;
            if (s.OnHand < 0)
                throw new Exception($"Insufficient stock at Location#{locationId} for Good#{goodId}");
        }

        // =================================================================
        //   LOOKUP endpoints (FE cần) — đặt route tuyệt đối để gom 1 file
        // =================================================================

        // GET ~/api/locations?type=WAREHOUSE&active=true
        [HttpGet("~/api/locations")]
        public async Task<IActionResult> LookupLocations([FromQuery] string? type, [FromQuery] bool? active)
        {
            var q = _db.Locations.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(type))
            {
                // === SỬA ĐỔI: Dùng ToLower() ===
                var typeLower = type.ToLower();
                q = q.Where(l => l.LocationType != null && l.LocationType.ToLower() == typeLower); 
            }
            if (active != null)
                q = q.Where(l => l.IsActive == active);

            var list = await q.OrderBy(l => l.LocationID)
                .Select(l => new { locationID = l.LocationID, name = l.Name, type = l.LocationType, active = l.IsActive })
                .ToListAsync();

            return Ok(list);
        }

        // GET ~/api/stocks/available?locationId=&kw=
        [HttpGet("~/api/stocks/available")]
        public async Task<IActionResult> StockAvailable([FromQuery] int locationId, [FromQuery] string? kw)
        {
            if (locationId <= 0) return BadRequest("locationId required.");

            // Lấy tổng tồn kho (theo giải pháp 2)
            var stockQ = _db.Stocks.AsNoTracking()
                .Where(s => s.LocationID == locationId)
                .GroupBy(s => s.GoodID)
                .Select(g => new { GoodID = g.Key, Available = g.Sum(x => x.OnHand - x.Reserved - x.InTransit) });

            // Join với bảng Goods
            var q =
                from s in stockQ
                join g in _db.Goods on s.GoodID equals g.GoodID
                select new
                {
                    s.GoodID,
                    sku = EF.Property<string>(g, "SKU"),
                    goodName = EF.Property<string>(g, "Name"),
                    unit = EF.Property<string>(g, "Unit"),
                    barcode = EF.Property<string>(g, "Barcode"), // === THÊM BARCODE ===
                    available = s.Available
                };

            if (!string.IsNullOrWhiteSpace(kw))
            {
                var key = kw.Trim().ToLower();
                q = q.Where(x =>
                    (x.goodName ?? "").ToLower().Contains(key) ||
                    (x.sku ?? "").ToLower().Contains(key) ||
                    (x.barcode ?? "").ToLower().Contains(key) // === TÌM BẰNG BARCODE ===
                );
            }

            var list = await q.OrderByDescending(x => x.available).ThenBy(x => x.goodName).Take(50).ToListAsync();
            return Ok(list);
        }

        // ========================================================
        //                    API CŨ (GIỮ NGUYÊN)
        // ========================================================

        // POST /api/transfers  -> Tạo transfer ở trạng thái Draft
        [HttpPost]
        [Authorize(Roles = "StoreManager")]
        public async Task<IActionResult> CreateTransfer([FromBody] TransferCreateDto dto)
        {
            if (dto == null) return BadRequest("Invalid payload.");
            if (dto.FromLocationID == dto.ToLocationID) return BadRequest("From/To phải khác nhau.");

            var from = await _db.Locations.FindAsync(dto.FromLocationID);
            var to = await _db.Locations.FindAsync(dto.ToLocationID);
            if (from == null || to == null) return BadRequest("Location không hợp lệ.");
            
            // === SỬA ĐỔI: Dùng hàm đã sửa (ToLower) ===
            if (!IsWarehouse(from)) return BadRequest("From must be WAREHOUSE.");
            if (!IsStore(to)) return BadRequest("To must be STORE.");

            using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var t = new Transfer
            {
                FromLocationID = dto.FromLocationID,
                ToLocationID = dto.ToLocationID,
                CreatedBy = GetUserId(), // Đã sửa GetUserId() để đọc "user_id"
                CreatedAt = GetVietnamTime(),
                Status = "Draft"
            };
            _db.Transfers.Add(t);
            await _db.SaveChangesAsync(); // Lỗi 547 (FK) đã xảy ra ở đây -> đã sửa GetUserId()

            foreach (var it in dto.Items ?? new List<TransferItemDto>())
            {
                if (it.Quantity <= 0) continue;
                _db.TransferItems.Add(new TransferItem
                {
                    TransferID = t.TransferID,
                    GoodID = it.GoodID,
                    // === SỬA ĐỔI (Giải pháp 2): Lưu BatchID là null ===
                    BatchID = null, 
                    Quantity = it.Quantity,
                    ShippedQty = 0,
                    ReceivedQty = 0
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // PUT /api/transfers  -> Sửa Draft (body có TransferID)
        [HttpPut]
        [Authorize(Roles = "StoreManager")]
        public async Task<IActionResult> UpdateTransfer([FromBody] TransferUpdateDto dto)
        {
            if (dto?.TransferID == null || dto.TransferID <= 0) return BadRequest("TransferID is required.");
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Draft") return BadRequest("Chỉ sửa được khi Transfer đang Draft.");
            if (t.CreatedBy != GetUserId()) return Forbid();

            using var tx = await _db.Database.BeginTransactionAsync();
            _db.TransferItems.RemoveRange(t.Items ?? new List<TransferItem>());
            await _db.SaveChangesAsync();

            foreach (var it in (dto.Items ?? new List<TransferItemDto>()))
            {
                if (it.Quantity <= 0) continue;
                _db.TransferItems.Add(new TransferItem
                {
                    TransferID = t.TransferID,
                    GoodID = it.GoodID,
                    // === SỬA ĐỔI (Giải pháp 2): Lưu BatchID là null ===
                    BatchID = null, 
                    Quantity = it.Quantity,
                    ShippedQty = 0,
                    ReceivedQty = 0
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // POST /api/transfers/approve
        [HttpPost("approve")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> ApproveTransfer([FromBody] TransferApproveDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Draft") return BadRequest("Chỉ duyệt khi đang Draft.");
            if (t.Items == null || t.Items.Count == 0) return BadRequest("Transfer không có dòng.");
            
            // === SỬA ĐỔI (Giải pháp 2): Xóa check BatchID và logic Reserved ===
            // if (t.Items.Any(i => i.BatchID == null)) return BadRequest("Mỗi dòng phải có BatchID trước khi duyệt.");
            // (Xóa toàn bộ vòng lặp foreach và tx.Commit/Rollback)

            t.Status = "Approved";
            await _db.SaveChangesAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // POST /api/transfers/ship
        [HttpPost("ship")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> ShipTransfer([FromBody] TransferShipDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Approved" && t.Status != "Shipping")
                return BadRequest("Chỉ ship khi trạng thái Approved hoặc Shipping.");

            using var tx = await _db.Database.BeginTransactionAsync();
            var plan = new List<(TransferItem item, decimal shipQty)>();

            // === SỬA ĐỔI (Giải pháp 2): WM phải cung cấp Lines (kèm BatchID) ===
            if (dto.Lines == null || dto.Lines.Count == 0)
            {
                return BadRequest("Shipment lines (với BatchID) là bắt buộc.");
            }
            else
            {
                foreach (var line in dto.Lines)
                {
                    // Tìm item theo GoodID (vì BatchID đang là null)
                    var it = t.Items!.FirstOrDefault(x => x.GoodID == line.GoodID); 
                    if (it == null) 
                    {
                        await tx.RollbackAsync();
                        return BadRequest($"Dòng không khớp Transfer: Good={line.GoodID}.");
                    }

                    // Gán BatchID mà WM đã chọn vào item
                    it.BatchID = line.BatchID; 
                    
                    var remaining = it.Quantity - it.ShippedQty;
                    if (line.ShipQty <= 0 || line.ShipQty > remaining)
                    {
                        await tx.RollbackAsync();
                        return BadRequest($"ShipQty không hợp lệ (còn {remaining}).");
                    }
                    plan.Add((it, line.ShipQty));
                }
            }

            foreach (var (it, shipQty) in plan)
            {
                var from = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                
                // === SỬA ĐỔI: Chỉ check OnHand (không có Reserved) ===
                if (from == null || from.OnHand < shipQty)
                {
                    await tx.RollbackAsync();
                    return BadRequest($"Không đủ tồn kho (OnHand) tại FromLocation cho Good={it.GoodID}, Batch={it.BatchID}.");
                }
                
                from.OnHand -= shipQty; 
                // from.Reserved -= shipQty; // Bỏ

                var to = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.ToLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (to == null)
                {
                    to = new Stock { LocationID = t.ToLocationID, GoodID = it.GoodID, BatchID = it.BatchID!.Value, OnHand = 0, Reserved = 0, InTransit = 0 };
                    _db.Stocks.Add(to);
                }
                to.InTransit += shipQty;
                it.ShippedQty += shipQty;
            }

            t.Status = t.Items!.All(x => x.ShippedQty >= x.Quantity) ? "Shipped" : "Shipping";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // POST /api/transfers/receive
        [HttpPost("receive")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> ReceiveTransfer([FromBody] TransferReceiveDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Shipped" && t.Status != "Shipping" && t.Status != "Receiving")
                return BadRequest("Chỉ nhận khi đã Ship hoặc đang nhận dở.");

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
                if (to == null)
                    return BadRequest($"Không tìm thấy stock ToLocation cho Good={it.GoodID}, Batch={it.BatchID}.");
                to.InTransit -= recvQty; to.OnHand += recvQty;
                it.ReceivedQty += recvQty;
            }

            t.Status = t.Items!.All(x => x.ReceivedQty >= x.Quantity) ? "Received" : "Receiving";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // GET /api/transfers/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetTransfer(int id)
        {
            var t = await _db.Transfers.AsNoTracking()
                .Where(x => x.TransferID == id)
                .Select(x => new
                {
                    x.TransferID,
                    x.FromLocationID,
                    FromLocationName = _db.Locations.Where(l => l.LocationID == x.FromLocationID).Select(l => l.Name).FirstOrDefault(),
                    x.ToLocationID,
                    ToLocationName = _db.Locations.Where(l => l.LocationID == x.ToLocationID).Select(l => l.Name).FirstOrDefault(),
                    x.Status,
                    x.CreatedBy,
                    x.CreatedAt,
                    // === SỬA ĐỔI: Thêm Barcode vào Items ===
                    Items = (from ti in _db.TransferItems
                             join g in _db.Goods on ti.GoodID equals g.GoodID
                             where ti.TransferID == x.TransferID
                             select new 
                             {
                                 ti.GoodID,
                                 g.SKU,
                                 g.Name,
                                 g.Barcode, // (Lấy từ bảng Goods)
                                 ti.BatchID, 
                                 ti.Quantity, 
                                 ti.ShippedQty, 
                                 ti.ReceivedQty
                             }).ToList()
                })
                .FirstOrDefaultAsync();
                
            return t == null ? NotFound() : Ok(t);
        }

        // GET /api/transfers  (giữ format cũ)
        [HttpGet]
        public async Task<IActionResult> ListTransfers([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.Transfers.AsNoTracking().AsQueryable();

            // === SỬA ĐỔI: Chỉ lấy transfer của User hiện tại (SM) ===
            var currentUserId = GetUserId();
            q = q.Where(t => t.CreatedBy == currentUserId);
            
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);

            // === SỬA ĐỔI: Dùng JOIN để đảm bảo luôn lấy được tên (thay vì .Include) ===
            var query = from t in q
                        join locFrom in _db.Locations on t.FromLocationID equals locFrom.LocationID
                        join locTo in _db.Locations on t.ToLocationID equals locTo.LocationID
                        orderby t.TransferID descending
                        select new
                        {
                            Id = t.TransferID,
                            Status = t.Status,
                            CreatedAt = t.CreatedAt,
                            FromName = locFrom.Name, // Lấy tên từ join
                            ToName = locTo.Name       // Lấy tên từ join
                        };

            var data = await query.Take(top).ToListAsync();
            // === KẾT THÚC SỬA ĐỔI ===

            return Ok(data);
        }

        // ========================================================
        //                   API MỚI (cho FE mới)
        // ========================================================

        // POST /api/transfers/{id}/submit  -> Draft -> Approved (map "Submitted")
        [HttpPost("{id:int}/submit")]
        [Authorize(Roles = "StoreManager")]
        public async Task<IActionResult> SubmitDraft(int id)
        {
            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == id);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (!string.Equals(t.Status, "Draft", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only Draft can be submitted.");
            if (t.CreatedBy != GetUserId()) return Forbid();

            t.Status = "Approved";
            await _db.SaveChangesAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // GET  ~/api/warehouse/transfers/submitted
        [HttpGet("~/api/warehouse/transfers/submitted")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> WarehouseList([FromQuery] string? status = null)
        {
            var q = _db.Transfers.Include(t => t.FromLocation).Include(t => t.ToLocation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
            else q = q.Where(t => t.Status == "Approved");

            var list = await q.OrderBy(t => t.CreatedAt)
                .Select(t => new
                {
                    transferID = t.TransferID,
                    status = t.Status,
                    submittedAt = t.CreatedAt,
                    fromLocationID = t.FromLocationID,
                    fromLocationName = t.FromLocation.Name,
                    toLocationID = t.ToLocationID,
                    toLocationName = t.ToLocation.Name
                })
                .ToListAsync();

            return Ok(list);
        }

        // GET  ~/api/warehouse/transfers/{id}
        [HttpGet("~/api/warehouse/transfers/{id:int}")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public Task<IActionResult> WarehouseDetail(int id) => GetTransfer(id);

        // POST /api/transfers/{id}/invoice  -> tạo Receipt Submitted (POID=TransferID)
        [HttpPost("{id:int}/invoice")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> CreateInvoice(int id, [FromBody] TransferInvoiceCreateDto dto)
        {
            if (dto?.Lines == null || dto.Lines.Count == 0) return BadRequest("No lines.");

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == id);
            if (t == null) return NotFound("Transfer không tồn tại.");
            if (t.Status != "Approved" && t.Status != "Shipped" && t.Status != "Received")
                return BadRequest("Transfer must be Approved/Shipped/Received to invoice.");

            var warehouseUserId = GetUserId(); // Receipts.ReceivedBy NOT NULL

            var r = new Receipt
            {
                POID = t.TransferID,
                SupplierID = null,
                LocationID = t.ToLocationID,
                ReceivedBy = warehouseUserId,
                CreatedAt = GetVietnamTime(),
                Status = "Submitted"
            };
            _db.Receipts.Add(r);
            await _db.SaveChangesAsync();

            foreach (var l in dto.Lines)
            {
                _db.ReceiptDetails.Add(new ReceiptDetail
                {
                    ReceiptID = r.ReceiptID,
                    GoodID = l.GoodId,
                    Quantity = l.Qty,
                    UnitCost = l.UnitPrice,
                    BatchID = l.BatchId
                });
            }
            await _db.SaveChangesAsync();

            var total = await _db.ReceiptDetails.Where(d => d.ReceiptID == r.ReceiptID)
                .SumAsync(d => d.Quantity * d.UnitCost);

            return Ok(new { receiptId = r.ReceiptID, status = r.Status, total });
        }

        // GET ~/api/store/receipts/pending
        [HttpGet("~/api/store/receipts/pending")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> StorePendingReceipts()
        {
            var uid = GetUserId();

            var q = from r in _db.Receipts.Include(x => x.Location)
                    where r.Status == "Submitted" && r.POID != null
                    join t in _db.Transfers on r.POID equals t.TransferID
                    where t.CreatedBy == uid
                    select new
                    {
                        r.ReceiptID,
                        r.Status,
                        r.CreatedAt,
                        locationID = r.LocationID,
                        locationName = r.Location != null ? r.Location.Name : null,
                        total = _db.ReceiptDetails.Where(d => d.ReceiptID == r.ReceiptID).Sum(d => d.Quantity * d.UnitCost)
                    };

            var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
            return Ok(list);
        }

        // GET ~/api/store/receipts/{receiptId}
        [HttpGet("~/api/store/receipts/{receiptId:int}")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> StoreReceiptDetail(int receiptId)
        {
            var uid = GetUserId();

            var r = await _db.Receipts.Include(x => x.Location).Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.ReceiptID == receiptId);
            if (r == null) return NotFound();

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == r.POID);
            if (t == null || t.CreatedBy != uid) return Forbid();

            return Ok(new
            {
                r.ReceiptID,
                r.Status,
                r.CreatedAt,
                locationID = r.LocationID,
                locationName = r.Location?.Name,
                lines = r.Details.Select(d => new { d.ReceiptDetailID, d.GoodID, d.BatchID, d.Quantity, d.UnitCost })
            });
        }

        // POST ~/api/transfers/receipts/{receiptId}/receive
        [HttpPost("~/api/transfers/receipts/{receiptId:int}/receive")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> ReceiveInvoice(int receiptId, [FromBody] TransferInvoiceReceiveDto dto)
        {
            if (dto?.Lines == null || dto.Lines.Count == 0) return BadRequest("No lines.");

            // === SỬA ĐỔI: Thêm khai báo 'uid' bị thiếu ===
            var uid = GetUserId();

            var r = await _db.Receipts.Include(x => x.Location).Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.ReceiptID == receiptId);
            if (r == null) return NotFound();
            if (!string.Equals(r.Status, "Submitted", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Receipt not in Submitted state.");

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == r.POID);
            if (t == null) return BadRequest("Linked transfer not found.");
            if (t.CreatedBy != uid) return Forbid();

            var fromWH = t.FromLocationID;
            var toStore = r.LocationID;

            var map = dto.Lines.ToDictionary(x => x.ReceiptDetailId);
            decimal totalAccepted = 0m;

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var d in r.Details)
                {
                    if (!map.TryGetValue(d.ReceiptDetailID, out var take))
                        return BadRequest($"Missing AcceptQty for line {d.ReceiptDetailID}");

                    var accept = Math.Max(0, Math.Min(take.AcceptQty, d.Quantity));
                    d.Quantity = accept;

                    if (accept > 0)
                    {
                        await AddOnHandAsync(fromWH, d.GoodID, d.BatchID, -accept);
                        await AddOnHandAsync(toStore, d.GoodID, d.BatchID, +accept);

                        _db.StockMovements.Add(new StockMovement
                        {
                            CreatedAt = GetVietnamTime(),
                            GoodID = d.GoodID,
                            Quantity = -accept,
                            FromLocationID = fromWH,
                            ToLocationID = toStore,
                            BatchID = d.BatchID,
                            UnitCost = d.UnitCost,
                            MovementType = "TRANSFER_RECEIVE",
                            RefTable = "Receipt",
                            RefID = r.ReceiptID,
                            Note = "WH->Store (out)"
                        });
                        _db.StockMovements.Add(new StockMovement
                        {
                            CreatedAt = GetVietnamTime(),
                            GoodID = d.GoodID,
                            Quantity = +accept,
                            FromLocationID = fromWH,
                            ToLocationID = toStore,
                            BatchID = d.BatchID,
                            UnitCost = d.UnitCost,
                            MovementType = "TRANSFER_RECEIVE",
                            RefTable = "Receipt",
                            RefID = r.ReceiptID,
                            Note = "WH->Store (in)"
                        });

                        totalAccepted += accept * d.UnitCost;
                    }
                }

                r.Status = "Confirmed";
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return BadRequest(ex.Message);
            }

            // Nếu không còn receipt nào của transfer này chưa Confirmed => Transfer = Received
            var anyOpen = await _db.Receipts.AnyAsync(x => x.POID == t.TransferID && x.Status != "Confirmed");
            if (!anyOpen)
            {
                t.Status = "Received";
                await _db.SaveChangesAsync();
            }

            return Ok(new { receiptId = r.ReceiptID, status = r.Status, totalAccepted });
        }
    }
}