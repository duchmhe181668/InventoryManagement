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
using InventoryManagement.Dto.TransferOrders; // dùng DTO bạn đã gửi

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
        private static DateTime GetVietnamTime() => DateTime.UtcNow; // tránh phụ thuộc BaseApiController

        private int GetUserId()
        {
            var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(s, out var id) ? id : 0;
        }

        private string GetUserName()
        {
            return User.Identity?.Name
                   ?? User.FindFirstValue("preferred_username")
                   ?? $"user#{GetUserId()}";
        }

        private static bool IsWarehouse(Location l)
            => l.LocationType != null && l.LocationType.Equals("WAREHOUSE", StringComparison.OrdinalIgnoreCase);
        private static bool IsStore(Location l)
            => l.LocationType != null && l.LocationType.Equals("STORE", StringComparison.OrdinalIgnoreCase);

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
                q = q.Where(l => l.LocationType != null && l.LocationType.Equals(type, StringComparison.OrdinalIgnoreCase));
            if (active != null)
                q = q.Where(l => l.IsActive == active);

            var list = await q.OrderBy(l => l.LocationID)
                .Select(l => new { locationID = l.LocationID, name = l.Name, type = l.LocationType, active = l.IsActive })
                .ToListAsync();

            return Ok(list);
        }

        // GET ~/api/auth/profile  -> FE lấy storeDefaultLocation & tên người dùng
        [HttpGet("~/api/auth/profile")]
        public async Task<IActionResult> ProfileForFE()
        {
            var uid = GetUserId();
            // chọn 1 location STORE active làm mặc định (vì DB không gắn store theo user)
            var store = await _db.Locations.AsNoTracking()
                .Where(l => l.IsActive && IsStore(l))
                .OrderBy(l => l.LocationID)
                .Select(l => new { l.LocationID, l.Name })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                userId = uid,
                username = GetUserName(),
                name = GetUserName(),
                // FE ưu tiên 2 key này:
                storeDefaultLocationId = store?.LocationID,
                storeDefaultLocationName = store?.Name
            });
        }

        // GET ~/api/stocks/available?locationId=&kw=
        // Trả về: [{goodID, sku, goodName, unit, available}]
        [HttpGet("~/api/stocks/available")]
        public async Task<IActionResult> StockAvailable([FromQuery] int locationId, [FromQuery] string? kw)
        {
            if (locationId <= 0) return BadRequest("locationId required.");

            // Query base: tồn khả dụng = OnHand - Reserved - InTransit
            var stockQ = _db.Stocks.AsNoTracking()
                .Where(s => s.LocationID == locationId)
                .GroupBy(s => s.GoodID)
                .Select(g => new { GoodID = g.Key, Available = g.Sum(x => x.OnHand - x.Reserved - x.InTransit) });

            // Join với Goods (tên cột có thể khác, bạn đổi ở 3 chỗ TODO bên dưới)
            var q =
                from s in stockQ
                join g in _db.Goods on s.GoodID equals g.GoodID
                select new
                {
                    s.GoodID,
                    sku = EF.Property<string>(g, "SKU"), // TODO: nếu không có SKU, đổi thành null
                    goodName = EF.Property<string>(g, "Name") ?? EF.Property<string>(g, "GoodName"), // TODO: sửa đúng tên cột
                    unit = EF.Property<string>(g, "Unit") ?? EF.Property<string>(g, "UnitName"),      // TODO: sửa đúng tên cột
                    available = s.Available
                };

            if (!string.IsNullOrWhiteSpace(kw))
            {
                var key = kw.Trim().ToLower();
                q = q.Where(x =>
                    (x.goodName ?? "").ToLower().Contains(key) ||
                    (x.sku ?? "").ToLower().Contains(key));
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
            if (!IsWarehouse(from)) return BadRequest("From must be WAREHOUSE.");
            if (!IsStore(to)) return BadRequest("To must be STORE.");

            using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var t = new Transfer
            {
                FromLocationID = dto.FromLocationID,
                ToLocationID = dto.ToLocationID,
                CreatedBy = GetUserId(),
                CreatedAt = GetVietnamTime(),
                Status = "Draft"
            };
            _db.Transfers.Add(t);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items ?? new List<TransferItemDto>())
            {
                if (it.Quantity <= 0) continue;
                _db.TransferItems.Add(new TransferItem
                {
                    TransferID = t.TransferID,
                    GoodID = it.GoodID,
                    BatchID = it.BatchID ?? 0,
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
                    BatchID = it.BatchID ?? 0,
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
            if (t.Items.Any(i => i.BatchID == null)) return BadRequest("Mỗi dòng phải có BatchID trước khi duyệt.");

            using var tx = await _db.Database.BeginTransactionAsync();

            foreach (var it in t.Items)
            {
                var stock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
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

            foreach (var (it, shipQty) in plan)
            {
                var from = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (from == null)
                    return BadRequest($"Không tìm thấy stock tại FromLocation cho Good={it.GoodID}, Batch={it.BatchID}.");
                from.OnHand -= shipQty; from.Reserved -= shipQty;

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
                    x.ToLocationID,
                    x.Status,
                    x.CreatedBy,
                    x.CreatedAt,
                    Items = _db.TransferItems.Where(i => i.TransferID == x.TransferID)
                        .Select(i => new { i.GoodID, i.BatchID, i.Quantity, i.ShippedQty, i.ReceivedQty })
                        .ToList()
                })
                .FirstOrDefaultAsync();
            return t == null ? NotFound() : Ok(t);
        }

        // GET /api/transfers  (giữ format cũ)
        [HttpGet]
        public async Task<IActionResult> ListTransfers([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.Transfers.AsNoTracking().Include(t => t.FromLocation).Include(t => t.ToLocation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);

            var data = await q.OrderByDescending(t => t.TransferID)
                .Select(t => new
                {
                    Id = t.TransferID,
                    Type = "Stock Transfer",
                    Status = t.Status,
                    CreatedAt = t.CreatedAt,
                    Details = $"Từ: {t.FromLocation.Name} → Tới: {t.ToLocation.Name}"
                })
                .Take(top)
                .ToListAsync();

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

            var r = await _db.Receipts.Include(x => x.Location).Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.ReceiptID == receiptId);
            if (r == null) return NotFound();
            if (!string.Equals(r.Status, "Submitted", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Receipt not in Submitted state.");

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == r.POID);
            if (t == null) return BadRequest("Linked transfer not found.");
            if (t.CreatedBy != GetUserId()) return Forbid();

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
