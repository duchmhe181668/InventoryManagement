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
// Thêm thư viện này nếu chưa có
using System.Text.Json; 

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

            q = q.Where(t => t.Status != "Draft");

            if (!string.IsNullOrWhiteSpace(status)) 
            {
                q = q.Where(t => t.Status == status);
            }

            var list = await q.OrderByDescending(t => t.CreatedAt) 
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

        //
        // ***************************************************************
        // HÀM WAREHOUSE DETAIL ĐÃ ĐƯỢC SỬA (THEO Good.cs)
        // ***************************************************************
        //
        // GET /api/warehouse/transfers/{id}
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
                    RequestItems = _db.TransferItems
                        .Where(ti => ti.TransferID == x.TransferID)
                        .Select(ti => new { ti.GoodID, ti.Quantity })
                        .ToList()
                })
                .FirstOrDefaultAsync();
                
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");

            var finalDisplayList = new List<object>();

            if (t.Status == "Approved")
            {
                var fromLocId = t.FromLocationID;

                foreach (var req in t.RequestItems)
                {
                    // 1. Lấy thông tin hàng hóa (THÊM PriceCost)
                    var good = await _db.Goods.AsNoTracking()
                        .Where(g => g.GoodID == req.GoodID)
                        // Lấy đúng PriceCost từ Good.cs
                        .Select(g => new { g.SKU, g.Name, g.Barcode, g.PriceCost }) 
                        .FirstOrDefaultAsync();
                    
                    // Lấy giá vốn trực tiếp
                    var costPrice = good?.PriceCost ?? 0m; 

                    // 2. Lấy danh sách các Lô có tồn kho (FEFO)
                    var availableBatches = await _db.Stocks.AsNoTracking()
                        .Include(s => s.Batch)
                        .Where(s => s.LocationID == fromLocId && s.GoodID == req.GoodID && s.OnHand > 0)
                        .OrderBy(s => s.Batch.ExpiryDate) 
                        .ThenBy(s => s.BatchID)           
                        .Select(s => new 
                        { 
                            s.BatchID, 
                            BatchNo = s.Batch.BatchNo, 
                            ExpiryDate = s.Batch.ExpiryDate,
                            AvailableQty = s.OnHand 
                        })
                        .ToListAsync();

                    decimal remainingNeeded = req.Quantity;

                    // 3. Phân bổ số lượng vào từng lô
                    foreach (var batch in availableBatches)
                    {
                        if (remainingNeeded <= 0) break;
                        var take = Math.Min(remainingNeeded, batch.AvailableQty);
                        
                        finalDisplayList.Add(new 
                        {
                            goodID = req.GoodID, sku = good.SKU, name = good.Name, barcode = good.Barcode,
                            batchID = batch.BatchID, batchNo = batch.BatchNo, expiryDate = batch.ExpiryDate,
                            pickQty = take,
                            costPrice = costPrice, // <-- THÊM MỚI
                            totalCost = take * costPrice // <-- THÊM MỚI
                        });
                        remainingNeeded -= take;
                    }

                    // Nếu thiếu hàng
                    if (remainingNeeded > 0)
                    {
                        finalDisplayList.Add(new 
                        {
                            goodID = req.GoodID, sku = good.SKU, name = good.Name, barcode = good.Barcode,
                            batchID = (int?)null, batchNo = "KHÔNG ĐỦ HÀNG", expiryDate = (DateTime?)null,
                            pickQty = remainingNeeded,
                            isMissing = true,
                            costPrice = costPrice, // <-- THÊM MỚI
                            totalCost = remainingNeeded * costPrice // <-- THÊM MỚI
                        });
                    }
                }
            }
            else // (Received, Shipped, Cancelled...)
            {
                foreach (var req in t.RequestItems)
                {
                    // Lấy thông tin hàng hóa (THÊM PriceCost)
                    var good = await _db.Goods.AsNoTracking()
                        .Where(g => g.GoodID == req.GoodID)
                        .Select(g => new { g.SKU, g.Name, g.Barcode, g.PriceCost }) // <-- SỬA Ở ĐÂY
                        .FirstOrDefaultAsync();

                    var costPrice = good?.PriceCost ?? 0m; // <-- SỬA Ở ĐÂY

                    finalDisplayList.Add(new 
                    {
                        goodID = req.GoodID,
                        sku = good?.SKU,
                        name = good?.Name,
                        barcode = good?.Barcode,
                        
                        batchID = (int?)null,
                        batchNo = (t.Status == "Cancelled") ? "ĐÃ HỦY" : "ĐÃ XUẤT KHO", // Sẽ bị JS ẩn đi
                        expiryDate = (DateTime?)null,
                        pickQty = req.Quantity, 
                        isMissing = false,
                        costPrice = costPrice, // <-- THÊM MỚI
                        totalCost = req.Quantity * costPrice // <-- THÊM MỚI
                    });
                }
            }
            
            // Trả về đối tượng kết hợp
            return Ok(new {
                t.TransferID,
                t.FromLocationID, t.FromLocationName,
                t.ToLocationID, t.ToLocationName,
                t.Status, t.CreatedBy, t.CreatedByName, t.CreatedAt,
                PickList = finalDisplayList 
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
            
            foreach (var line in dto.Lines)
            {
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

                // 2. Trừ Tồn Đặt giữ (Reserved) của Kho
                var reservedStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.FromLocationID && s.GoodID == line.GoodID && s.BatchID == 0);
                
                if (reservedStock != null)
                {
                    reservedStock.Reserved -= line.ShipQty;
                    if (reservedStock.Reserved < 0) reservedStock.Reserved = 0;
                }
                else 
                {
                    if (fromStock.Reserved >= line.ShipQty) fromStock.Reserved -= line.ShipQty;
                }

                // 3. Tăng Tồn Kho (OnHand) tại Cửa hàng (Store)
                var toStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                    s.LocationID == t.ToLocationID && s.GoodID == line.GoodID && s.BatchID == line.BatchID);
                
                if (toStock == null)
                {
                    toStock = new Stock { 
                        LocationID = t.ToLocationID, 
                        GoodID = line.GoodID, 
                        BatchID = line.BatchID, 
                        OnHand = 0, Reserved = 0, InTransit = 0 
                    };
                    _db.Stocks.Add(toStock);
                }
                toStock.OnHand += line.ShipQty;

                // Cập nhật thông tin vào TransferItem
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
            
            if (t.Status != "Approved" && t.Status != "Draft")
                return BadRequest("Chỉ có thể từ chối phiếu ở trạng thái 'Approved' hoặc 'Draft'.");

            if (t.Status == "Approved")
            {
                var itemsGroup = t.Items.GroupBy(i => i.GoodID)
                    .Select(g => new { GoodID = g.Key, Qty = g.Sum(i => i.Quantity) });

                foreach (var item in itemsGroup)
                {
                    var reservedStock = await _db.Stocks.FirstOrDefaultAsync(s =>
                        s.LocationID == t.FromLocationID && s.GoodID == item.GoodID && s.BatchID == 0);
                    
                    if (reservedStock != null)
                    {
                        reservedStock.Reserved -= item.Qty;
                        if (reservedStock.Reserved < 0) reservedStock.Reserved = 0; 
                    }
                    else 
                    {
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