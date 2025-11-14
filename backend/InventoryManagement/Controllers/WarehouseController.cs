using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Dto.TransferOrders; 
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/warehouse")]
    [Authorize(Roles = "WarehouseManager,Administrator")]
    public class WarehouseController : ControllerBase
    {
        private readonly AppDbContext _db;
        public WarehouseController(AppDbContext db) { _db = db; }

        // === Helpers ===
        private int GetUserId()
        {
            var s = User.FindFirstValue("user_id"); 
            return int.TryParse(s, out var id) ? id : 0;
        }

        // GET  /api/warehouse/transfers -> Lấy danh sách (cho WM)
        [HttpGet("transfers")]
        public async Task<IActionResult> WarehouseList([FromQuery] string? status = null)
        {
            var q = _db.Transfers.AsNoTracking()
                .Include(t => t.FromLocation)
                .Include(t => t.ToLocation)
                .AsQueryable();

            // === SỬA ĐỔI: Logic lọc mới ===
            
            // 1. Quy tắc cứng: WM không bao giờ nhìn thấy phiếu Draft (Nháp)
            q = q.Where(t => t.Status != "Draft");

            // 2. Xử lý bộ lọc từ Frontend
            if (!string.IsNullOrWhiteSpace(status)) 
            {
                // Nếu có chọn cụ thể (Approved, Received...) thì lọc theo nó
                q = q.Where(t => t.Status == status);
            }
            // Nếu status rỗng (Tất cả), thì code sẽ chạy qua đây và lấy HẾT (trừ Draft)

            // === KẾT THÚC SỬA ĐỔI ===

            var list = await q.OrderByDescending(t => t.CreatedAt) // Nên sắp xếp mới nhất lên đầu
                .Select(t => new
                {
                    transferID = t.TransferID,
                    status = t.Status,
                    submittedAt = t.CreatedAt,
                    fromLocationID = t.FromLocationID,
                    fromLocationName = t.FromLocation.Name,
                    toLocationID = t.ToLocationID,
                    storeName = t.ToLocation.Name 
                })
                .ToListAsync();

            return Ok(list);
        }

        // GET /api/warehouse/transfers/{id} -> LOGIC MỚI: TỰ ĐỘNG PHÂN BỔ LÔ (FEFO)
        [HttpGet("transfers/{id:int}")]
        public async Task<IActionResult> WarehouseDetail(int id)
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
                    CreatedByName = _db.Users.Where(u => u.UserID == x.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                    x.CreatedAt,
                    // Lấy danh sách items yêu cầu
                    RequestItems = _db.TransferItems
                        .Where(ti => ti.TransferID == x.TransferID)
                        .Select(ti => new { ti.GoodID, ti.Quantity })
                        .ToList()
                })
                .FirstOrDefaultAsync();
                
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");

            // === THUẬT TOÁN FEFO (First Expired First Out) ===
            var pickList = new List<object>();
            var fromLocId = t.FromLocationID;

            foreach (var req in t.RequestItems)
            {
                // 1. Lấy thông tin hàng hóa
                var good = await _db.Goods.AsNoTracking()
                    .Where(g => g.GoodID == req.GoodID)
                    .Select(g => new { g.SKU, g.Name, g.Barcode })
                    .FirstOrDefaultAsync();

                // 2. Lấy danh sách các Lô có tồn kho (OnHand > 0) tại kho nguồn
                // Sắp xếp: Hết hạn trước (ExpiryDate) -> Nhập trước (BatchID nhỏ)
                var availableBatches = await _db.Stocks.AsNoTracking()
                    .Include(s => s.Batch)
                    .Where(s => s.LocationID == fromLocId && s.GoodID == req.GoodID && s.OnHand > 0)
                    .OrderBy(s => s.Batch.ExpiryDate) // Ưu tiên date gần
                    .ThenBy(s => s.BatchID)           // Sau đó ưu tiên lô cũ
                    .Select(s => new 
                    { 
                        s.BatchID, 
                        BatchNo = s.Batch.BatchNo, 
                        ExpiryDate = s.Batch.ExpiryDate,
                        // Tồn khả dụng để xuất = OnHand - Reserved (nếu có logic reserved riêng)
                        // Ở đây ta dùng OnHand vì logic Approved đã giữ Reserved ở Batch 0 hoặc tổng
                        AvailableQty = s.OnHand 
                    })
                    .ToListAsync();

                decimal remainingNeeded = req.Quantity;

                // 3. Phân bổ số lượng vào từng lô
                foreach (var batch in availableBatches)
                {
                    if (remainingNeeded <= 0) break;

                    var take = Math.Min(remainingNeeded, batch.AvailableQty);
                    
                    pickList.Add(new 
                    {
                        goodID = req.GoodID,
                        sku = good.SKU,
                        name = good.Name,
                        barcode = good.Barcode,
                        quantityNeeded = req.Quantity, // Tổng cần
                        
                        // Thông tin xuất
                        batchID = batch.BatchID,
                        batchNo = batch.BatchNo,
                        expiryDate = batch.ExpiryDate,
                        pickQty = take // Số lượng lấy từ lô này
                    });

                    remainingNeeded -= take;
                }

                // Nếu sau khi quét hết các lô mà vẫn thiếu hàng
                if (remainingNeeded > 0)
                {
                    // Thêm một dòng báo thiếu (BatchID = null hoặc đặc biệt) để FE hiển thị cảnh báo
                    pickList.Add(new 
                    {
                        goodID = req.GoodID,
                        sku = good.SKU,
                        name = good.Name,
                        barcode = good.Barcode,
                        quantityNeeded = req.Quantity,
                        batchID = (int?)null,
                        batchNo = "KHÔNG ĐỦ HÀNG",
                        expiryDate = (DateTime?)null,
                        pickQty = remainingNeeded,
                        isMissing = true
                    });
                }
            }
            
            // Trả về đối tượng kết hợp thông tin phiếu và danh sách gợi ý xuất kho
            return Ok(new {
                t.TransferID,
                t.FromLocationID, t.FromLocationName,
                t.ToLocationID, t.ToLocationName,
                t.Status, t.CreatedBy, t.CreatedByName, t.CreatedAt,
                PickList = pickList // Frontend sẽ vẽ bảng dựa trên list này
            });
        }

        // POST /api/warehouse/transfers/{id}/accept -> WM Chấp nhận theo PickList
        [HttpPost("transfers/{id:int}/accept")]
        public async Task<IActionResult> AcceptTransfer(int id, [FromBody] TransferShipDto dto)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == id);
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            if (t.Status != "Approved")
                return BadRequest("Chỉ chấp nhận (accept) khi trạng thái là 'Approved'.");

            if (dto.Lines == null || dto.Lines.Count == 0)
                return BadRequest("Danh sách xuất kho trống.");
            
            // Duyệt qua từng dòng xuất kho (Pick Line)
            foreach (var line in dto.Lines)
            {
                // Validate Item
                var transferItem = t.Items.FirstOrDefault(x => x.GoodID == line.GoodID);
                if (transferItem == null) 
                    return BadRequest($"Sản phẩm (GoodID {line.GoodID}) không có trong phiếu.");

                // 1. Trừ Tồn Kho (OnHand) tại Lô cụ thể (Kho)
                var fromStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == line.GoodID && s.BatchID == line.BatchID);

                if (fromStock == null || fromStock.OnHand < line.ShipQty)
                {
                    await tx.RollbackAsync();
                    return BadRequest($"Lô hàng {line.BatchID} không đủ tồn kho (OnHand) để xuất {line.ShipQty}. Vui lòng kiểm tra lại.");
                }
                fromStock.OnHand -= line.ShipQty;

                // 2. Trừ Tồn Đặt giữ (Reserved) của Kho (thường nằm ở Batch 0 hoặc tổng)
                // Logic: Tìm dòng Batch 0 để trừ reserved
                var reservedStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == line.GoodID && s.BatchID == 0);
                
                if (reservedStock != null)
                {
                    // Trừ reserved. Nếu < 0 thì set về 0 (đề phòng lệch data)
                    reservedStock.Reserved -= line.ShipQty;
                    if (reservedStock.Reserved < 0) reservedStock.Reserved = 0;
                }
                else 
                {
                    // Nếu không có Batch 0, thử trừ chính dòng batch đang xuất (nếu logic reserve lưu vào đó)
                    if (fromStock.Reserved >= line.ShipQty) fromStock.Reserved -= line.ShipQty;
                }

                // 3. Tăng Tồn Kho (OnHand) tại Cửa hàng (Store) - Giữ nguyên BatchID để truy xuất
                var toStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.ToLocationID && s.GoodID == line.GoodID && s.BatchID == line.BatchID);
                
                if (toStock == null)
                {
                    toStock = new Stock { 
                        LocationID = t.ToLocationID, 
                        GoodID = line.GoodID, 
                        BatchID = line.BatchID, // Chuyển nguyên Lô sang
                        OnHand = 0, Reserved = 0, InTransit = 0 
                    };
                    _db.Stocks.Add(toStock);
                }
                toStock.OnHand += line.ShipQty;

                // Cập nhật thông tin vào TransferItem (để biết đã xuất bao nhiêu)
                // Lưu ý: 1 TransferItem có thể được xuất từ NHIỀU batch khác nhau.
                // Ở đây ta cộng dồn ShippedQty và ReceivedQty
                transferItem.ShippedQty += line.ShipQty;
                transferItem.ReceivedQty += line.ShipQty; // 1-Bước: Nhận luôn
            }

            t.Status = "Received"; 
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { t.TransferID, t.Status });
        }

        // POST /api/warehouse/transfers/{id}/reject -> WM Từ chối (Approved -> Cancelled)
        [HttpPost("transfers/{id:int}/reject")]
        public async Task<IActionResult> RejectTransfer(int id)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            
            var t = await _db.Transfers.Include(x => x.Items)
                                       .FirstOrDefaultAsync(x => x.TransferID == id);
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            
            // Chỉ cho phép từ chối khi đang Approved (đã giữ hàng) hoặc Draft
            if (t.Status != "Approved" && t.Status != "Draft")
                return BadRequest("Chỉ có thể từ chối phiếu ở trạng thái 'Approved' hoặc 'Draft'.");

            // Nếu là Approved, cần HOÀN TRẢ lại Reserved
            if (t.Status == "Approved")
            {
                // Gom nhóm items để trả reserved
                var itemsGroup = t.Items.GroupBy(i => i.GoodID)
                    .Select(g => new { GoodID = g.Key, Qty = g.Sum(i => i.Quantity) });

                foreach (var item in itemsGroup)
                {
                    // Tìm dòng Batch 0 (hoặc dòng đang giữ reserved) để trả lại
                    var reservedStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                        s.LocationID == t.FromLocationID && s.GoodID == item.GoodID && s.BatchID == 0);
                    
                    if (reservedStock != null)
                    {
                        reservedStock.Reserved -= item.Qty;
                        if (reservedStock.Reserved < 0) reservedStock.Reserved = 0; // Safety
                    }
                    else 
                    {
                        // Nếu không có Batch 0, thử tìm dòng nào có reserved > 0 để trừ
                        var anyStock = await _db.Stocks.FirstOrDefaultAsync(s => 
                            s.LocationID == t.FromLocationID && s.GoodID == item.GoodID && s.Reserved >= item.Qty);
                        if(anyStock != null) anyStock.Reserved -= item.Qty;
                    }
                }
            }

            t.Status = "Cancelled";
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            
            return Ok(new { t.TransferID, t.Status });
        }
    }
}