using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Dto.ReceiptDto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReceiptsController : Controller
    {
        private readonly AppDbContext _context;
        private static DateTime TodayVn() => DateTime.UtcNow.AddHours(7).Date;

        public ReceiptsController(AppDbContext context)
        {
            _context = context;
        }

        private int? GetSupplierIdFromToken()
        {
            var s = User.FindFirst("supplier_id")?.Value;
            return int.TryParse(s, out var id) ? id : (int?)null;
        }

        [HttpGet]
        //[Authorize(Roles = "Warehouse,Admin")]
        public IActionResult GetAllForWarehouse([FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var q = _context.Receipts
                .Include(r => r.Supplier)
                .OrderByDescending(r => r.CreatedAt)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                q = q.Where(r =>
                    r.ReceiptID.ToString().Contains(search) ||
                    r.Supplier.Name.ToLower().Contains(search) ||
                    r.Supplier.SupplierID.ToString().Contains(search)
                );
            }

            if (from.HasValue)
                q = q.Where(r => r.CreatedAt >= from.Value);
            if (to.HasValue)
                q = q.Where(r => r.CreatedAt <= to.Value);

            var data = q.Select(r => new
            {
                r.ReceiptID,
                SupplierName = r.Supplier.Name,
                r.SupplierID,
                r.CreatedAt,
                r.Status,
                TotalQty = r.Details.Sum(d => d.Quantity),
                TotalValue = r.Details.Sum(d => d.Quantity * d.UnitCost),

                TotalOrdered = _context.PurchaseOrderLines
                   .Where(l => l.POID == r.POID)
                   .Sum(l => (decimal?)l.Quantity) ?? 0,

                TotalReceived = r.Details.Sum(d => (decimal?)d.Quantity) ?? 0,

                TotalReturned = _context.ReturnOrders
                   .Where(ro => ro.ReceiptID == r.ReceiptID)
                   .SelectMany(ro => ro.Details)
                   .Sum(d => (decimal?)d.QuantityReturned) ?? 0
            }).ToList();

            return Ok(data);
        }

        /// GET /api/receipts/by-supplier/5?page=1&pageSize=20&status=Draft&from=2025-01-01&to=2025-12-31&poid=10
        [HttpGet("by-supplier/{supplierId:int}")]
        public async Task<ActionResult<IEnumerable<ReceiptListItemDto>>> GetBySupplier(
            int supplierId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? poid = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            // (Khuyến nghị) Nếu bạn có claim supplier_id trong JWT, có thể khóa lại:
            // var claim = User.FindFirst("supplier_id")?.Value;
            // if (int.TryParse(claim, out var sidFromToken) && sidFromToken != supplierId) return Forbid();

            var q = _context.Receipts.AsNoTracking()
                .Where(r => r.SupplierID == supplierId && r.Status != "Draft");

            if (!string.IsNullOrWhiteSpace(status))
            {
                var st = status.Trim().ToLower();
                q = q.Where(r => (r.Status ?? "").ToLower() == st);
            }
            if (from.HasValue) q = q.Where(r => r.CreatedAt >= from.Value);
            if (to.HasValue) q = q.Where(r => r.CreatedAt < to.Value);
            if (poid.HasValue) q = q.Where(r => r.POID == poid.Value);

            // projection + aggregate tổng dòng/tổng tiền
            var data = await q
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReceiptListItemDto
                {
                    ReceiptID = r.ReceiptID,
                    POID = r.POID,
                    SupplierID = r.SupplierID,
                    LocationID = r.LocationID,
                    ReceivedBy = r.ReceivedBy,
                    Status = r.Status!,
                    //CreatedAt = r.CreatedAt,
                    CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    TotalLines = r.Details.Count(), // ✅ dùng navigation
                                                    // Sum nullable rồi coalesce về 0 để SQL dịch được
                    TotalAmount = r.Details
                    .Select(d => (decimal?)(d.Quantity * d.UnitCost))
                    .Sum() ?? 0m
                })
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();


            if (data.Count == 0) return NoContent();
            return Ok(data);
        }


        [HttpGet("{id:int}", Name = "GetReceiptById")]
        public async Task<ActionResult<ReceiptViewDto>> GetById(int id)
        {
            var r = await _context.Receipts
                .Include(x => x.Details)
                     .ThenInclude(d => d.Batch)
                .Include(x => x.Details)
                     .ThenInclude(d => d.Good)
                .Include(x => x.Supplier)
                .FirstOrDefaultAsync(x => x.ReceiptID == id);

            if (r == null) return NotFound();

            // ✅ Lấy toàn bộ dòng PO của đơn hàng tương ứng
            var poLines = await _context.PurchaseOrderLines
                .Where(l => l.POID == r.POID)
                .ToDictionaryAsync(l => l.GoodID, l => l.Quantity);

            var dto = new ReceiptViewDto
            {
                ReceiptID = r.ReceiptID,
                POID = r.POID,
                SupplierID = r.SupplierID,
                SupplierName = r.Supplier?.Name,
                LocationID = r.LocationID,
                ReceivedBy = r.ReceivedBy,
                Status = r.Status,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                Items = r.Details?.Select(d => new ReceiptItemViewDto
                {
                    ReceiptDetailID = d.ReceiptDetailID,
                    GoodID = d.GoodID,
                    GoodName = d.Good?.Name,
                    Unit = d.Good?.Unit,
                    OrderedQuantity = poLines.ContainsKey(d.GoodID) ? poLines[d.GoodID] : 0,
                    Quantity = d.Quantity,
                    UnitCost = d.UnitCost,
                    BatchID = d.BatchID,
                    BatchNo = d.Batch?.BatchNo,
                    ExpiryDate = d.Batch?.ExpiryDate
                }).ToList() ?? new()
            };

            return Ok(dto);
        }


        /// Tạo Receipt từ PO: lấy LocationID, SupplierID, Lines từ PO.
        /// FE chỉ gửi (GoodID, UnitPrice, BatchNo, ExpiryDate[, Quantity]).
        [HttpPost("from-po")]
        public async Task<ActionResult<ReceiptViewDto>> CreateFromPo([FromBody] CreateReceiptDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            await using var tx = await _context.Database.BeginTransactionAsync();

            // 1) Lấy PO + lines
            var po = await _context.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");

            // 2) Xác định supplier đang thao tác & kiểm quyền
            var supplierId = GetSupplierIdFromToken() ?? dto.SupplierId;
            if (supplierId == null) return Forbid("Không xác định được Supplier.");
            if (po.SupplierID != supplierId.Value) return Forbid("PO không thuộc Supplier hiện tại.");

            // Chỉ cho tạo receipt khi PO đã Submitted/Received
            var st = po.Status?.Trim();
            if (!string.Equals(st, "Submitted", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(st, "Received", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Chỉ tạo Receipt cho PO đã Submitted/Received.");

            if (dto.Items == null || dto.Items.Count == 0) return BadRequest("Items rỗng.");

            // Map GoodID -> (Quantity, UnitPrice) từ PO line
            // LƯU Ý: Ở DB của bạn line có field Quantity (KHÔNG phải QuantityOrdered)
            var poLineByGood = po.Lines
                .GroupBy(l => l.GoodID)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Qty = g.Sum(x => x.Quantity),      // ✅ dùng Quantity có sẵn
                        UnitPrice = g.First().UnitPrice    // fallback unit price
                    });

            var reqDup = dto.Items
                .GroupBy(i => new { i.GoodID, Key = i.BatchNo?.Trim().ToLower() ?? "" })
                .FirstOrDefault(g => g.Key.Key != "" && g.Count() > 1);
            if (reqDup != null)
                return BadRequest($"Trong yêu cầu có trùng BatchNo '{reqDup.Key.Key}' cho GoodID {reqDup.Key.GoodID}.");

            // 3) Validate từng item & chuẩn hóa input
            foreach (var it in dto.Items)
            {
                if (!poLineByGood.ContainsKey(it.GoodID))
                    return BadRequest($"GoodID {it.GoodID} không có trong PO.");

                if (it.Quantity.HasValue && it.Quantity.Value <= 0)
                    return BadRequest($"Quantity của GoodID {it.GoodID} phải > 0.");

                if (string.IsNullOrWhiteSpace(it.BatchNo))
                    return BadRequest("BatchNo bắt buộc.");

                if (it.ExpiryDate == default)
                    return BadRequest("ExpiryDate bắt buộc.");

                if (it.ExpiryDate.Date < TodayVn())
                    return BadRequest($"ExpiryDate của GoodID {it.GoodID} không được ở quá khứ.");

                var batchUpper = NormalizeBatch(it.BatchNo);

                var exists = await _context.Batches
                    .Where(b => b.GoodID == it.GoodID)
                    .Select(b => new { BN = b.BatchNo.ToUpper(), b.BatchID, b.ExpiryDate })
                     .FirstOrDefaultAsync(b => b.BN == batchUpper);

                if (exists != null)
                    return Conflict($"BatchNo '{batchUpper}' của GoodID {it.GoodID} đã tồn tại (BatchID {exists.BatchID}).");
            }



            // 4) Tạo Receipt 
            var currentUserId = GetSupplierIdFromToken(); // nếu có, trả int?; nếu chưa có thì bỏ qua
            var receipt = new Receipt
            {
                POID = po.POID,
                SupplierID = po.SupplierID,
                LocationID = 1,
                ReceivedBy = currentUserId ?? po.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                Status = "Submitted"
            };
            _context.Receipts.Add(receipt);
            await _context.SaveChangesAsync(); // có ReceiptID

            // 5) Tạo Batch + ReceiptDetails
            foreach (var it in dto.Items)
            {
                var qty = it.Quantity ?? poLineByGood[it.GoodID].Qty;
                var unit = it.UnitPrice; // ✅ supplier nhập, bắt buộc
                var batchUpper = NormalizeBatch(it.BatchNo);

                // Tìm hoặc tạo Batch theo (GoodID, BatchNo, ExpiryDate) để tránh trùng lặp
                var batch = await _context.Batches.FirstOrDefaultAsync(b =>
                    b.GoodID == it.GoodID &&
                    b.BatchNo.ToUpper() == batchUpper &&
                    b.ExpiryDate == it.ExpiryDate);

                if (batch == null)
                {
                    batch = new Batch
                    {
                        GoodID = it.GoodID,
                        //BatchNo = it.BatchNo.Trim(),
                        BatchNo = batchUpper,
                        ExpiryDate = it.ExpiryDate
                    };
                    _context.Batches.Add(batch);
                    await _context.SaveChangesAsync(); // ✅ BatchID tự tăng, có giá trị ở đây
                }

                _context.ReceiptDetails.Add(new ReceiptDetail
                {
                    ReceiptID = receipt.ReceiptID,
                    GoodID = it.GoodID,
                    Quantity = qty,
                    UnitCost = unit,        // ✅ lấy đúng giá supplier nhập
                    BatchID = batch.BatchID // ✅ dùng id tự tăng đã có
                });
            }
            await _context.SaveChangesAsync();

            // 7) CẬP NHẬT GIÁ từ Receipt → PO.Lines (theo GoodID)
            var priceByGood = dto.Items
                .GroupBy(i => i.GoodID)
                .ToDictionary(g => g.Key, g => g.Last().UnitPrice); // nếu trùng GoodID, lấy giá cuối

            foreach (var line in po.Lines.Where(l => priceByGood.ContainsKey(l.GoodID)))
            {
                line.UnitPrice = priceByGood[line.GoodID];
            }
            // (Tuỳ chọn) cập nhật tổng tiền PO nếu có cột:
            // po.TotalAmount = po.Lines.Sum(l => l.Quantity * l.UnitPrice);

            // 8) Cập nhật trạng thái PO sau khi tạo receipt
            po.Status = "Received";
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // 6) Trả view
            var view = await _context.Receipts
                .Where(r => r.ReceiptID == receipt.ReceiptID)
                .Select(r => new ReceiptViewDto
                {
                    ReceiptID = r.ReceiptID,
                    POID = r.POID,
                    SupplierID = r.SupplierID,
                    LocationID = r.LocationID,
                    ReceivedBy = r.ReceivedBy,
                    Status = r.Status,
                    CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    Items = _context.ReceiptDetails
                        .Where(d => d.ReceiptID == r.ReceiptID)
                        .Select(d => new ReceiptItemViewDto
                        {
                            ReceiptDetailID = d.ReceiptDetailID,
                            GoodID = d.GoodID,
                            Quantity = d.Quantity,
                            UnitCost = d.UnitCost,
                            BatchID = d.BatchID,                      // ✅ giữ nullable
                            BatchNo = d.Batch != null ? d.Batch.BatchNo : "",
                            ExpiryDate = d.Batch != null ? d.Batch.ExpiryDate : null
                        }).ToList()
                })
                .FirstAsync();
            return CreatedAtAction(nameof(GetById), new { id = view.ReceiptID }, view);
        }


        [HttpPatch("{id:int}/confirm")]
        //[Authorize(Roles = "Warehouse,Admin")]
        public async Task<IActionResult> ConfirmReceipt(int id, [FromBody] ConfirmReceiptDto dto)
        {
            var receipt = await _context.Receipts
                .Include(r => r.Details)
                .FirstOrDefaultAsync(r => r.ReceiptID == id);

            if (receipt == null)
                return NotFound("Không tìm thấy biên lai.");

            using var tran = await _context.Database.BeginTransactionAsync();
            try
            {
                // ✅ Gán thông tin
                receipt.Status = "Confirmed";
                receipt.ReceivedBy = dto.ReceivedBy;

                foreach (var item in dto.Items)
                {
                    var detail = receipt.Details.FirstOrDefault(d => d.GoodID == item.GoodID);
                    if (detail == null) continue;

                    // ✅ Lấy dòng PO tương ứng để kiểm tra giới hạn số lượng đặt
                    var poLine = await _context.PurchaseOrderLines
                        .FirstOrDefaultAsync(p => p.POID == receipt.POID && p.GoodID == item.GoodID);

                    if (poLine == null)
                        return BadRequest($"Không tìm thấy sản phẩm ID {item.GoodID} trong đơn đặt hàng gốc.");

                    // ✅ Kiểm tra số lượng nhập không vượt quá số lượng trong PO
                    if (item.Quantity > poLine.Quantity)
                    {
                        return BadRequest($"❌ Số lượng nhập ({item.Quantity}) vượt quá số lượng đặt ({poLine.Quantity}) cho hàng ID {item.GoodID}.");
                    }

                    // ✅ Cập nhật số lượng thực tế nhập
                    detail.Quantity = item.Quantity;

                    // ✅ Cập nhật tồn kho
                    var stock = await _context.Stocks
                        .FirstOrDefaultAsync(s =>
                            s.LocationID == receipt.LocationID &&
                            s.GoodID == item.GoodID &&
                            s.BatchID == detail.BatchID);

                    if (stock == null)
                    {
                        stock = new Stock
                        {
                            LocationID = receipt.LocationID,
                            GoodID = item.GoodID,
                            BatchID = detail.BatchID ?? 0,
                            OnHand = item.Quantity,
                            Reserved = 0,
                            InTransit = 0
                        };
                        _context.Stocks.Add(stock);
                    }
                    else
                    {
                        stock.OnHand += item.Quantity;
                    }
                }


                // ✅ Đánh dấu PO là Done
                var po = await _context.PurchaseOrders.FirstOrDefaultAsync(p => p.POID == receipt.POID);
                if (po != null) po.Status = "Done";

                await _context.SaveChangesAsync();
                await tran.CommitAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        //Đổi trạng thái status
        [HttpPatch("{id:int}/status")]
        //[Authorize(Roles = "Warehouse,Admin")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Status))
                return BadRequest("Trạng thái không hợp lệ.");

            var r = await _context.Receipts.FirstOrDefaultAsync(x => x.ReceiptID == id);
            if (r == null)
                return NotFound("Không tìm thấy biên lai.");

            if (!dto.Status.Equals("Confirmed", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Chỉ có thể chuyển sang trạng thái Confirmed.");

            // Cập nhật trạng thái + người xác nhận
            r.Status = "Confirmed";
            if (dto.ReceivedBy.HasValue)
                r.ReceivedBy = dto.ReceivedBy.Value;

            // Cập nhật luôn PurchaseOrder
            if (r.POID != null)
            {
                var po = await _context.PurchaseOrders.FindAsync(r.POID);
                if (po != null)
                    po.Status = "Done";
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }


        private static string NormalizeBatch(string? s)
           => (s ?? "").Trim().ToUpperInvariant();

        //DTO
        public class ReturnItemDto
        {
            public int GoodID { get; set; }
            public int? BatchID { get; set; }     // optional if you use Batch table
            public decimal Quantity { get; set; }
            public string? Note { get; set; }
        }

        public class CreateReturnDto
        {
            public int WarehouseLocationID { get; set; }   // from warehouse
            public int SupplierID { get; set; }            // supplier receiving the return
            public List<ReturnItemDto> Items { get; set; } = new();
            public string? Reference { get; set; }         // e.g. "PO#.., Receipt#.."
        }

        public class ConfirmReceiptDto
        {
            public int ReceivedBy { get; set; }
            public List<ConfirmItemDto> Items { get; set; } = new();
        }

        public class ConfirmItemDto
        {
            public int GoodID { get; set; }
            public decimal Quantity { get; set; }
        }


    }
}
