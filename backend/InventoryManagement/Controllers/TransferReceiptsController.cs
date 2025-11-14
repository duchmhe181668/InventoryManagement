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
    [Route("api/transfer-receipts")]
    [Authorize(Roles = "StoreManager,WarehouseManager,Administrator")]
    public class TransferReceiptsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public TransferReceiptsController(AppDbContext db) { _db = db; }
        
        // === Helpers ===
        private static DateTime GetVietnamTime() => DateTime.UtcNow;
        private int GetUserId()
        {
            var s = User.FindFirstValue("user_id"); 
            return int.TryParse(s, out var id) ? id : 0;
        }
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
                throw new Exception($"Không đủ tồn kho tại Location#{locationId} cho Good#{goodId}");
        }

        // POST /api/transfer-receipts/from-transfer/{id} -> WM tạo Receipt (Hóa đơn/Phiếu giao)
        [HttpPost("from-transfer/{id:int}")]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> CreateInvoice(int id, [FromBody] TransferInvoiceCreateDto dto)
        {
            if (dto?.Lines == null || dto.Lines.Count == 0) return BadRequest("Không có dòng nào.");

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == id);
            if (t == null) return NotFound("Không tìm thấy phiếu transfer.");
            if (t.Status != "Approved" && t.Status != "Shipped" && t.Status != "Received")
                return BadRequest("Transfer must be Approved/Shipped/Received to invoice.");

            var warehouseUserId = GetUserId();

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

        // GET /api/transfer-receipts/pending -> SM xem các Receipt/Hóa đơn đang chờ
        [HttpGet("pending")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> StorePendingReceipts()
        {
            var uid = GetUserId();

            var q = from r in _db.Receipts.Include(x => x.Location)
                    where r.Status == "Submitted" && r.POID != null && r.SupplierID == null
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

        // GET /api/transfer-receipts/{id} -> SM xem chi tiết Receipt (của Transfer)
        [HttpGet("{id:int}")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> StoreReceiptDetail(int id)
        {
            var uid = GetUserId();

            var r = await _db.Receipts.Include(x => x.Location).Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.ReceiptID == id);
            if (r == null) return NotFound("Không tìm thấy Receipt.");
            
            if (r.SupplierID != null) return BadRequest("Phiếu này không phải là phiếu Transfer.");

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == r.POID);
            if (t == null || t.CreatedBy != uid) return Forbid("Bạn không có quyền xem phiếu này.");

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

        // POST /api/transfer-receipts/{id}/receive -> SM xác nhận Receipt (của Transfer)
        [HttpPost("{id:int}/receive")]
        [Authorize(Roles = "StoreManager,Administrator")]
        public async Task<IActionResult> ReceiveInvoice(int id, [FromBody] TransferInvoiceReceiveDto dto)
        {
            if (dto?.Lines == null || dto.Lines.Count == 0) return BadRequest("Không có dòng nào.");

            var uid = GetUserId();

            var r = await _db.Receipts.Include(x => x.Location).Include(x => x.Details)
                .FirstOrDefaultAsync(x => x.ReceiptID == id);
            if (r == null) return NotFound("Không tìm thấy Receipt.");
            if (!string.Equals(r.Status, "Submitted", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Receipt không ở trạng thái 'Submitted'.");

            var t = await _db.Transfers.FirstOrDefaultAsync(x => x.TransferID == r.POID);
            if (t == null) return BadRequest("Không tìm thấy Transfer liên kết.");
            if (t.CreatedBy != uid) return Forbid("Bạn không có quyền nhận phiếu này.");

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
                        return BadRequest($"Thiếu số lượng xác nhận cho dòng {d.ReceiptDetailID}");

                    var accept = Math.Max(0, Math.Min(take.AcceptQty, d.Quantity));
                    d.Quantity = accept;

                    if (accept > 0)
                    {
                        var toStock = await _db.Stocks.FirstOrDefaultAsync(s => s.LocationID == toStore && s.GoodID == d.GoodID && s.BatchID == d.BatchID);
                        if (toStock == null)
                        {
                            toStock = new Stock { LocationID = toStore, GoodID = d.GoodID, BatchID = d.BatchID ?? 0 };
                            _db.Stocks.Add(toStock);
                        }
                        
                        if (toStock.InTransit < accept)
                        {
                            await tx.RollbackAsync();
                            return BadRequest($"Tồn kho 'InTransit' không đủ tại Cửa hàng cho Good={d.GoodID}, Batch={d.BatchID}. (Cần: {accept}, Có: {toStock.InTransit})");
                        }
                        
                        toStock.InTransit -= accept;
                        toStock.OnHand += accept;

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
                            Note = "Store received from WH"
                        });

                        totalAccepted += accept * d.UnitCost;
                    }
                }

                r.Status = "Confirmed";
                await _db.SaveChangesAsync();
                
                var transferItems = await _db.TransferItems.Where(ti => ti.TransferID == t.TransferID).ToListAsync();
                foreach (var ti in transferItems)
                {
                    var receivedDetail = r.Details.FirstOrDefault(d => d.GoodID == ti.GoodID && d.BatchID == ti.BatchID);
                    if(receivedDetail != null)
                    {
                        ti.ReceivedQty += receivedDetail.Quantity; 
                    }
                }
                
                t.Status = transferItems.All(ti => ti.ReceivedQty >= ti.Quantity) ? "Received" : "Receiving";

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return BadRequest(ex.Message);
            }

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