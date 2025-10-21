using InventoryManagement.Data;
using InventoryManagement.Dto.PurchaseOrders;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PurchaseOrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        public PurchaseOrdersController(AppDbContext context)
        {
            _context = context;
        }

        
        // 1) Danh sách PO theo SupplierID (có thể lọc theo status, phân trang)
        [HttpGet("by-supplier/{supplierId:int}")]
        [Authorize(Roles = "Supplier")]
        public async Task<ActionResult<IEnumerable<POListItemDto>>> GetBySupplier(
            int supplierId,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            // Lấy SupplierID từ claim
            var supplierIdClaim = User.FindFirst("supplier_id")?.Value; 
            if (string.IsNullOrEmpty(supplierIdClaim)) return Forbid(); 
            supplierId = int.Parse(supplierIdClaim);

            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            var query = _context.Set<PurchaseOrder>()
                .Where(po => po.SupplierID == supplierId && po.Status != "Draft");

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(po => po.Status == status);

            var data = await query
                .OrderByDescending(po => po.CreatedAt)
                .Select(po => new POListItemDto
                {
                    POID = po.POID,
                    SupplierID = po.SupplierID,
                    SupplierName = po.Supplier != null ? po.Supplier.Name : "",
                    CreatedAt = po.CreatedAt,
                    Status = po.Status,
                    TotalLines = po.Lines!.Count,
                    TotalAmount = po.Lines!.Sum(l => l.Quantity * l.UnitPrice)
                })
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            if (!data.Any()) return NoContent();
            return Ok(data);
        }

        [HttpGet("{poid:int}")]
        public async Task<ActionResult<PODetailDto>> GetDetail(int poid)
        {
            var po = await _context.Set<PurchaseOrder>()
                .Where(x => x.POID == poid)
                .Select(x => new PODetailDto
                {
                    POID = x.POID,
                    SupplierID = x.SupplierID,
                    SupplierName = x.Supplier != null ? x.Supplier.Name : "",
                    CreatedBy = x.CreatedBy,
                    CreatedByName = x.CreatedByUser != null ? x.CreatedByUser.Name : null,
                    CreatedAt = x.CreatedAt,
                    Status = x.Status,
                    TotalAmount = x.Lines!.Sum(l => l.Quantity * l.UnitPrice),
                    Lines = x.Lines!.Select(l => new POLineDto
                    {
                        POLineID = l.POLineID,
                        GoodID = l.GoodID,
                        GoodName = l.Good != null ? l.Good.Name : "",
                        SKU = l.Good != null ? l.Good.SKU : null,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (po == null) return NotFound($"PO {poid} không tồn tại");
            return Ok(po);
        }

        //update status
        [HttpPatch("{id:int}/status")]
        [Authorize(Roles = "Supplier")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Status))
                return BadRequest("Trạng thái không hợp lệ.");

            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null)
                return NotFound();

            // ✅ Supplier chỉ được phép đổi sang Received (trong giai đoạn này)
            if (!dto.Status.Equals("Received", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Bạn chỉ có thể cập nhật trạng thái sang Received.");

            // ✅ Kiểm tra luồng hợp lệ: Submitted -> Received
            if (!po.Status.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
                return BadRequest($"Không thể chuyển từ {po.Status} sang {dto.Status}.");

            po.Status = dto.Status;
            await _context.SaveChangesAsync();
            return NoContent();
        }


        public class UpdateStatusDto
        {
            public string Status { get; set; } = null!;
        }
    }
}
