using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Dto.ReceiptDto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        // Nếu đã nhét supplier_id vào JWT claim thì dùng hàm này, còn không có thì lấy từ body dto.SupplierId
        private int? GetSupplierIdFromToken()
        {
            var s = User.FindFirst("supplier_id")?.Value;
            return int.TryParse(s, out var id) ? id : (int?)null;
        }


        /// <summary>
        /// Lấy danh sách Receipt theo SupplierID (có phân trang & lọc).
        /// GET /api/receipts/by-supplier/5?page=1&pageSize=20&status=Draft&from=2025-01-01&to=2025-12-31&poid=10
        /// </summary>
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
                .Where(r => r.SupplierID == supplierId && r.Status != "Draft" );

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



        /// <summary>
        /// Tạo Receipt từ PO: lấy LocationID, SupplierID, Lines từ PO.
        /// FE chỉ gửi (GoodID, UnitPrice, BatchNo, ExpiryDate[, Quantity]).
        /// </summary>
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
            po.Status = "Shipping";
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


        [HttpGet("{id:int}", Name = "GetReceiptById")]
        public async Task<ActionResult<ReceiptViewDto>> GetById(int id)
        {
            var r = await _context.Receipts
                .Include(x => x.Details)
                .ThenInclude(d => d.Batch)
                .FirstOrDefaultAsync(x => x.ReceiptID == id);

            if (r == null) return NotFound();

            var dto = new ReceiptViewDto
            {
                ReceiptID = r.ReceiptID,
                POID = r.POID,
                SupplierID = r.SupplierID,
                LocationID = r.LocationID,
                ReceivedBy = r.ReceivedBy,
                Status = r.Status,
                //CreatedAt = r.CreatedAt,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                Items = r.Details?.Select(d => new ReceiptItemViewDto
                {
                    ReceiptDetailID = d.ReceiptDetailID,
                    GoodID = d.GoodID,
                    Quantity = d.Quantity,
                    UnitCost = d.UnitCost,
                    BatchID = d.BatchID,
                    BatchNo = d.Batch?.BatchNo,
                    ExpiryDate = d.Batch?.ExpiryDate
                }).ToList() ?? new()
            };

            return Ok(dto);
        }

        private static string NormalizeBatch(string? s)
    => (s ?? "").Trim().ToUpperInvariant();

    }
}
