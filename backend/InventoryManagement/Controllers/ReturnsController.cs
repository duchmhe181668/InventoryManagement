using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReturnOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;

        private static DateTime NowVn() => DateTime.UtcNow.AddHours(7);
        public ReturnOrdersController(AppDbContext db) => _db = db;

        // GET: /api/ReturnOrders/by-receipt/{receiptId}
        [HttpGet("by-receipt/{receiptId:int}")]
        public async Task<ActionResult<IEnumerable<ReturnOrder>>> GetByReceipt(int receiptId)
        {
            var list = await _db.ReturnOrders
                .Include(r => r.Details).ThenInclude(d => d.Good)
                .Where(r => r.ReceiptID == receiptId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(list);
        }

        // GET: /api/ReturnOrders/by-supplier/{supplierId}?status=Confirmed&from=2025-10-01&to=2025-10-31
        [HttpGet("by-supplier/{supplierId:int}")]
        public async Task<ActionResult<IEnumerable<object>>> GetBySupplier(
     int supplierId,
     [FromQuery] string? status,
     [FromQuery] DateTime? from,
     [FromQuery] DateTime? to,
     [FromQuery] int? poid)
        {
            var q = from r in _db.ReturnOrders
                    join u in _db.Users on r.CreatedBy equals u.UserID into j1
                    from u in j1.DefaultIfEmpty()
                    join s in _db.Suppliers on r.SupplierID equals s.SupplierID into j2
                    from s in j2.DefaultIfEmpty()
                    join rc in _db.Receipts on r.ReceiptID equals rc.ReceiptID into j3
                    from rc in j3.DefaultIfEmpty()
                    where r.SupplierID == supplierId
                    select new { r, u, s, rc };

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(x => x.r.Status == status);

            if (from.HasValue)
                q = q.Where(x => x.r.CreatedAt >= from.Value);

            if (to.HasValue)
                q = q.Where(x => x.r.CreatedAt <= to.Value.AddDays(1));

            if (poid.HasValue && poid.Value > 0)
                q = q.Where(x => x.rc != null && x.rc.POID == poid.Value);

            var list = await q
                .OrderByDescending(x => x.r.CreatedAt)
                .Select(x => new
                {
                    x.r.ReturnID,
                    x.r.ReceiptID,
                    x.r.Status,
                    x.r.Note,
                    x.r.CreatedAt,
                    x.r.ConfirmedAt,
                    CreatedBy = x.u != null ? x.u.Name : null,      
                    ConfirmedBy = x.s != null ? x.s.Name : null  
                })
                .ToListAsync();

            if (!list.Any()) return NoContent();
            return Ok(list);
        }




        // GET: /api/ReturnOrders/by-warehouse/{warehouseId}?status=Submitted&from=2025-10-01&to=2025-10-31&poid=1&supplierName=ABC
        [HttpGet("by-warehouse/{warehouseId:int}")]
        public async Task<ActionResult<IEnumerable<object>>> GetByWarehouse(
            int warehouseId,
            [FromQuery] string? status,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? poid,
            [FromQuery] string? supplierName)
        {
            var q = from r in _db.ReturnOrders
                    join rc in _db.Receipts on r.ReceiptID equals rc.ReceiptID into j1
                    from rc in j1.DefaultIfEmpty()
                    join po in _db.PurchaseOrders on rc.POID equals po.POID into j2
                    from po in j2.DefaultIfEmpty()
                    join s in _db.Suppliers on po.SupplierID equals s.SupplierID into j3
                    from s in j3.DefaultIfEmpty()
                    where r.CreatedBy == warehouseId
                    select new { r, rc, po, s };

            // --- Bộ lọc ---
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(x => x.r.Status == status);

            if (from.HasValue)
                q = q.Where(x => x.r.CreatedAt >= from.Value);

            if (to.HasValue)
                q = q.Where(x => x.r.CreatedAt <= to.Value.AddDays(1));

            if (poid.HasValue && poid.Value > 0)
                q = q.Where(x => x.po.POID == poid.Value);

            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                var kw = supplierName.Trim().ToLower();
                q = q.Where(x => x.s.Name.ToLower().Contains(kw));
            }

            // --- Dựng dữ liệu ---
            var data = await q
                .OrderByDescending(x => x.r.CreatedAt)
                .Select(x => new
                {
                    x.r.ReturnID,
                    x.r.ReceiptID,
                    POID = x.po.POID,
                    SupplierName = x.s.Name,
                    x.r.CreatedAt,
                    x.r.ConfirmedAt,
                    x.r.Status,
                    Reason = _db.ReturnDetails
                        .Where(d => d.ReturnID == x.r.ReturnID)
                        .Select(d => d.Reason)
                        .FirstOrDefault(),
                    x.r.Note,
                    CreatedBy = _db.Users
                        .Where(u => u.UserID == x.r.CreatedBy)
                        .Select(u => u.Name)
                        .FirstOrDefault()
                })
                .ToListAsync();

            if (!data.Any()) return NoContent();
            return Ok(data);
        }

        // GET: /api/ReturnOrders/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult> GetById(int id)
        {
            // Lấy ReturnOrder + Receipt + Details + Good + Batch
            var ro = await _db.ReturnOrders
                .Include(r => r.Receipt)
                .Include(r => r.Details)!
                    .ThenInclude(d => d.Good)
                .Include(r => r.Details)!
                    .ThenInclude(d => d.Batch)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ReturnID == id);

            if (ro == null)
                return NotFound("Không tìm thấy phiếu trả hàng.");

            var poid = await _db.Receipts
                .Where(x => x.ReceiptID == ro.ReceiptID)
                .Select(x => x.POID)
                .FirstOrDefaultAsync();

            var poLines = await _db.PurchaseOrderLines
                .Where(p => p.POID == poid)
                .Select(p => new { p.GoodID, p.Quantity })
                .ToListAsync();

            var result = new
            {
                ro.ReturnID,
                ro.ReceiptID,
                ro.Status,
                ro.Note,
                ro.CreatedAt,
                ro.ConfirmedAt,
                Details = ro.Details!.Select(d => new
                {
                    d.ReturnDetailID,
                    d.GoodID,
                    d.Good?.Unit,
                    Good = new { d.Good!.Name },
                    Batch = d.Batch != null ? new { d.Batch.BatchNo } : null,
                    d.QuantityReturned,
                    d.UnitCost,
                    d.Reason,
                    OrderedQuantity = poLines
                        .FirstOrDefault(p => p.GoodID == d.GoodID)?.Quantity ?? 0
                })
            };

            return Ok(result);
        }


        // GET: /api/ReturnOrders/preview-confirm/{receiptId}
        [HttpGet("preview-confirm/{receiptId:int}")]
        public async Task<ActionResult> PreviewConfirm(int receiptId)
        {
            // 🔹 1. Tìm biên lai
            var receipt = await _db.Receipts
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ReceiptID == receiptId);

            if (receipt == null)
                return NotFound("Không tìm thấy biên lai.");

            // 🔹 2. Lấy POID từ biên lai
            var poId = receipt.POID;
            if (poId == null || poId == 0)
                return BadRequest("Biên lai này không liên kết với đơn đặt hàng (PO).");

            // 🔹 3. Lấy SupplierID từ PurchaseOrder
            var supplierId = await _db.PurchaseOrders
                .Where(p => p.POID == poId)
                .Select(p => p.SupplierID)
                .FirstOrDefaultAsync();

            // 🔹 4. Lấy chi tiết biên lai
            var receiptDetails = await _db.ReceiptDetails
                .Where(rd => rd.ReceiptID == receiptId)
                .Include(rd => rd.Good)
                .ToListAsync();

            if (receiptDetails.Count == 0)
                return BadRequest("Biên lai không có hàng hóa.");

            // 🔹 5. Tính toán preview
            var previewItems = new List<object>();
            foreach (var rd in receiptDetails)
            {
                var poLine = await _db.PurchaseOrderLines
                    .Where(l => l.POID == poId && l.GoodID == rd.GoodID)
                    .FirstOrDefaultAsync();
                if (poLine == null) continue;

                var qtyOrdered = poLine.Quantity;
                var qtyReceived = rd.Quantity;
                var qtyToReturn = qtyOrdered - qtyReceived;
                if (qtyToReturn < 0) qtyToReturn = 0;

                // 🔹 Lấy batchNo (nếu có)
                string? batchNo = await _db.Batches
                    .Where(b => b.BatchID == rd.BatchID)
                    .Select(b => b.BatchNo)
                    .FirstOrDefaultAsync();

                previewItems.Add(new
                {
                    goodID = rd.GoodID,
                    goodName = rd.Good?.Name,
                    batchID = rd.BatchID,
                    batchNo = batchNo,
                    quantityOrdered = qtyOrdered,
                    quantityReceived = qtyReceived,
                    quantityToReturn = qtyToReturn,
                    unitCost = rd.UnitCost
                });
            }

            // 🔹 6. Trả kết quả preview
            return Ok(new
            {
                receiptID = receiptId,
                supplierID = supplierId,
                previewItems
            });
        }

        // POST: /api/ReturnOrders  (Warehouse tạo phiếu sau khi nhập lý do)
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateReturnFullDto dto)
        {
            if (dto == null || dto.ReceiptId <= 0 || dto.SupplierId <= 0)
                return BadRequest("Thiếu thông tin phiếu trả.");

            await using var tran = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = new ReturnOrder
                {
                    ReceiptID = dto.ReceiptId,
                    SupplierID = dto.SupplierId,
                    CreatedBy = dto.UserId,
                    CreatedAt = NowVn(),
                    Status = "Submitted",
                    Note = dto.Note,
                    ConfirmedAt = null
                };

                _db.ReturnOrders.Add(order);
                await _db.SaveChangesAsync();

                foreach (var item in dto.Items)
                {
                    if (item.QuantityToReturn <= 0) continue;

                    int? batchId = item.BatchID;
                    decimal unitCost = item.UnitCost;

                    var rd = await _db.ReceiptDetails
                        .Where(r => r.ReceiptID == dto.ReceiptId && r.GoodID == item.GoodID)
                        .Select(r => new { r.BatchID, r.UnitCost })
                        .FirstOrDefaultAsync();

                    if (rd != null)
                    {
                        if (batchId == null || batchId == 0)
                            batchId = rd.BatchID;
                        if (unitCost <= 0)
                            unitCost = rd.UnitCost;
                    }

                    _db.ReturnDetails.Add(new ReturnDetail
                    {
                        ReturnID = order.ReturnID,
                        GoodID = item.GoodID,
                        BatchID = batchId,
                        QuantityReturned = item.QuantityToReturn,
                        UnitCost = unitCost,
                        Reason = item.Reason,
                        Note = "Kho tạo phiếu trả hàng"
                    });
                }


                await _db.SaveChangesAsync();
                await tran.CommitAsync();

                return Ok(new
                {
                    message = "Đã tạo phiếu trả hàng.",
                    returnId = order.ReturnID,
                    status = order.Status
                });
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync();
                return BadRequest(new { error = ex.Message });
            }
        }

        // PATCH: /api/ReturnOrders/{id}/confirm  (Supplier xác nhận nhận hàng)
        [HttpPatch("{id:int}/confirm")]
        public async Task<ActionResult> SupplierConfirm(int id, [FromQuery] int supplierId)
        {
            var ro = await _db.ReturnOrders.FirstOrDefaultAsync(r => r.ReturnID == id);
            if (ro == null)
                return NotFound("Không tìm thấy phiếu trả hàng.");

            if (ro.Status == "Confirmed")
                return BadRequest("Phiếu đã được xác nhận trước đó.");

            var supplierExists = await _db.Suppliers.AnyAsync(s => s.SupplierID == supplierId);
            if (!supplierExists)
                return BadRequest("Nhà cung cấp không hợp lệ.");

            try
            {
                ro.Status = "Confirmed";
                ro.ConfirmedAt = NowVn();
                ro.ConfirmedBy = supplierId;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    message = "Nhà cung cấp đã xác nhận nhận hàng trả.",
                    returnId = ro.ReturnID,
                    confirmedAt = ro.ConfirmedAt,
                    confirmedBy = ro.ConfirmedBy
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // PATCH: /api/ReturnDetails/update-reason
        [HttpPatch("/api/ReturnDetails/update-reason")]
        public async Task<ActionResult> UpdateReason([FromBody] UpdateReasonDto dto)
        {
            var detail = await _db.ReturnDetails
                .FirstOrDefaultAsync(d => d.ReturnID == dto.ReturnId && d.GoodID == dto.GoodId);
            if (detail == null) return NotFound();

            detail.Reason = dto.Reason;
            await _db.SaveChangesAsync();
            return NoContent();
        }



        public class UpdateReasonDto
        {
            public int ReturnId { get; set; }
            public int GoodId { get; set; }
            public string? Reason { get; set; }
        }

        public class CreateReturnDto
        {
            public int ReceiptId { get; set; }
            public int SupplierId { get; set; }
            public int UserId { get; set; }
            public string? Note { get; set; }
        }

        public class CreateReturnFullDto
        {
            public int ReceiptId { get; set; }
            public int SupplierId { get; set; }
            public int UserId { get; set; }
            public string? Note { get; set; }
            public List<ReturnItemDto> Items { get; set; } = new();
        }

        public class ReturnItemDto
        {
            public int GoodID { get; set; }
            public int? BatchID { get; set; }
            public decimal QuantityToReturn { get; set; }
            public decimal UnitCost { get; set; }
            public string? Reason { get; set; }
        }

    }
}
