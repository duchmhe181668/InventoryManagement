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
    [Authorize(Roles = "StoreManager,Administrator")]
    public class TransfersController : ControllerBase
    {
        private readonly AppDbContext _db;
        public TransfersController(AppDbContext db) { _db = db; }

        // ========= Helpers =========
        private static DateTime GetVietnamTime() => DateTime.UtcNow;

        private int GetUserId()
        {
            var s = User.FindFirstValue("user_id"); 
            return int.TryParse(s, out var id) ? id : 0;
        }
        
        private static bool IsWarehouse(Location l)
            => l.LocationType != null && l.LocationType.ToLower() == "warehouse";
        private static bool IsStore(Location l)
            => l.LocationType != null && l.LocationType.ToLower() == "store";
        
        // ========================================================
        //         QUY TRÌNH CỦA STORE MANAGER
        // ========================================================

        // POST /api/transfers  -> Tạo transfer (Hỗ trợ cả Xin hàng và Trả hàng)
        [HttpPost]
        public async Task<IActionResult> CreateTransfer([FromBody] TransferCreateDto dto)
        {
            if (dto == null) return BadRequest("Dữ liệu không hợp lệ.");
            if (dto.FromLocationID == dto.ToLocationID) return BadRequest("Nơi đi và Nơi đến phải khác nhau.");

            var from = await _db.Locations.FindAsync(dto.FromLocationID);
            var to = await _db.Locations.FindAsync(dto.ToLocationID);
            if (from == null || to == null) return BadRequest("Kho/Cửa hàng không hợp lệ.");
            
            // === SỬA ĐỔI QUAN TRỌNG: Logic 2 chiều ===
            bool isDistribution = IsWarehouse(from) && IsStore(to); // Chiều 1: Kho -> Store (Xin hàng)
            bool isReturn = IsStore(from) && IsWarehouse(to);       // Chiều 2: Store -> Kho (Trả hàng)

            if (!isDistribution && !isReturn)
            {
                return BadRequest("Luồng chuyển hàng không hợp lệ. Chỉ hỗ trợ: (Kho -> Store) hoặc (Store -> Kho).");
            }
            // === KẾT THÚC SỬA ĐỔI ===

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
                    BatchID = null, // Luôn là null (để WM quyết định lô khi xử lý)
                    Quantity = it.Quantity,
                    ShippedQty = 0,
                    ReceivedQty = 0
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }
        
        // PUT /api/transfers  -> SM Sửa Draft
        [HttpPut]
        public async Task<IActionResult> UpdateTransfer([FromBody] TransferUpdateDto dto)
        {
            if (dto?.TransferID == null || dto.TransferID <= 0) return BadRequest("TransferID là bắt buộc.");
            var t = await _db.Transfers.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            if (t.Status != "Draft") return BadRequest("Chỉ sửa được khi phiếu đang ở trạng thái 'Draft'.");
            if (t.CreatedBy != GetUserId()) return Forbid("Bạn không có quyền sửa phiếu này.");

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

        // POST /api/transfers/{id}/submit  -> SM Gửi Yêu Cầu (Draft -> Approved)
        [HttpPost("{id:int}/submit")]
        public async Task<IActionResult> SubmitDraft(int id)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == id);

            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            if (!string.Equals(t.Status, "Draft", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Chỉ có thể gửi (submit) phiếu 'Draft'.");
            if (t.CreatedBy != GetUserId()) return Forbid("Bạn không có quyền gửi phiếu này.");
            if (t.Items == null || !t.Items.Any())
                return BadRequest("Phiếu không có mặt hàng nào.");

            var requestedItems = t.Items
                .GroupBy(i => i.GoodID)
                .Select(g => new { GoodID = g.Key, Quantity = g.Sum(i => i.Quantity) });

            foreach (var item in requestedItems)
            {
                // Kiểm tra tồn kho tại NƠI ĐI (FromLocation)
                // Nếu là Xin hàng: Check tồn Kho. Nếu là Trả hàng: Check tồn Store.
                var stocks = await _db.Stocks
                    .Where(s => s.LocationID == t.FromLocationID && s.GoodID == item.GoodID)
                    .ToListAsync(); 

                decimal available = stocks.Sum(s => s.OnHand - s.Reserved);

                if (item.Quantity > available)
                {
                    await tx.RollbackAsync();
                    return BadRequest($"Không đủ tồn kho cho GoodID {item.GoodID}. Yêu cầu: {item.Quantity}, Chỉ còn: {available}");
                }

                // Đặt giữ hàng (Reserved)
                var stockToUpdate = stocks.FirstOrDefault(s => s.BatchID == 0) ?? stocks.FirstOrDefault();
                if (stockToUpdate == null) 
                {
                    stockToUpdate = new Stock { LocationID = t.FromLocationID, GoodID = item.GoodID, BatchID = 0 };
                    _db.Stocks.Add(stockToUpdate);
                }
                
                stockToUpdate.Reserved += item.Quantity;
            }

            t.Status = "Approved";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            
            return Ok(new { t.TransferID, t.Status });
        }

        // POST /api/transfers/receive -> SM Nhận hàng (Shipped -> Received)
        [HttpPost("receive")]
        public async Task<IActionResult> ReceiveTransfer([FromBody] TransferReceiveDto dto)
        {
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == dto.TransferID);
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            if (t.Status != "Shipped" && t.Status != "Shipping" && t.Status != "Receiving")
                return BadRequest("Chỉ nhận hàng khi trạng thái là 'Shipped' hoặc 'Shipping'/'Receiving'.");

            using var tx = await _db.Database.BeginTransactionAsync();
            var plan = new List<(TransferItem item, decimal recvQty)>();

            if (dto.Lines == null || dto.Lines.Count == 0)
            {
                foreach (var it in t.Items!)
                {
                    if (it.BatchID == null) return BadRequest("Lỗi: BatchID không được null ở bước này.");
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
                        return BadRequest($"Số lượng nhận (ReceiveQty) không hợp lệ (còn lại: {remaining}).");
                    plan.Add((it, line.ReceiveQty));
                }
            }

            foreach (var (it, recvQty) in plan)
            {
                var to = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.ToLocationID && s.GoodID == it.GoodID && s.BatchID == it.BatchID!.Value);
                if (to == null)
                    return BadRequest($"Không tìm thấy stock (InTransit) tại kho đích cho Good={it.GoodID}, Batch={it.BatchID}.");
                
                to.InTransit -= recvQty; 
                to.OnHand += recvQty;    
                it.ReceivedQty += recvQty;
            }

            t.Status = t.Items!.All(x => x.ReceivedQty >= x.Quantity) ? "Received" : "Receiving";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // GET /api/transfers/{id} -> Lấy chi tiết
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetTransfer(int id)
        {
            var currentUserId = GetUserId();
            var userRole = User.FindFirstValue(ClaimTypes.Role)?.ToLower();

            var tQuery = _db.Transfers.AsNoTracking()
                .Where(x => x.TransferID == id);

            if (userRole != "administrator")
            {
                tQuery = tQuery.Where(x => x.CreatedBy == currentUserId);
            }

            var t = await tQuery.Select(x => new
                {
                    x.TransferID,
                    x.FromLocationID,
                    FromLocationName = _db.Locations.Where(l => l.LocationID == x.FromLocationID).Select(l => l.Name).FirstOrDefault(),
                    x.ToLocationID,
                    ToLocationName = _db.Locations.Where(l => l.LocationID == x.ToLocationID).Select(l => l.Name).FirstOrDefault(),
                    x.Status,
                    x.CreatedBy,
                    CreatedByName = _db.Users.Where(u => u.UserID == x.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                    x.CreatedAt,
                    Items = (from ti in _db.TransferItems
                             join g in _db.Goods on ti.GoodID equals g.GoodID
                             where ti.TransferID == x.TransferID
                             select new 
                             {
                                 ti.GoodID,
                                 g.SKU,
                                 g.Name,
                                 g.Barcode, 
                                 ti.BatchID, 
                                 ti.Quantity, 
                                 ti.ShippedQty, 
                                 ti.ReceivedQty,
                                 // Lấy tồn kho khả dụng HIỆN TẠI của NƠI ĐI
                                 Available = (from s in _db.Stocks
                                              where s.LocationID == x.FromLocationID && s.GoodID == ti.GoodID
                                              select s.OnHand - s.Reserved - s.InTransit).Sum()
                             }).ToList()
                })
                .FirstOrDefaultAsync();
                
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            
            return Ok(t);
        }

        // GET /api/transfers -> Lấy danh sách
        [HttpGet]
        public async Task<IActionResult> ListTransfers([FromQuery] string? status, [FromQuery] int top = 50)
        {
            var q = _db.Transfers.AsNoTracking().AsQueryable();

            var currentUserId = GetUserId();
            q = q.Where(t => t.CreatedBy == currentUserId);
            
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);

            var query = from t in q
                        join locFrom in _db.Locations on t.FromLocationID equals locFrom.LocationID
                        join locTo in _db.Locations on t.ToLocationID equals locTo.LocationID
                        orderby t.TransferID descending
                        select new
                        {
                            Id = t.TransferID,
                            Status = t.Status,
                            CreatedAt = t.CreatedAt,
                            FromName = locFrom.Name, 
                            ToName = locTo.Name
                        };

            var data = await query.Take(top).ToListAsync();

            return Ok(data);
        }
    }
}