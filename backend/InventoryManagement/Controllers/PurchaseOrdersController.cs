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
        public async Task<ActionResult<IEnumerable<POListItemDto>>> GetBySupplier(
            int supplierId,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            var query = _context.Set<PurchaseOrder>()
                .Where(po => po.SupplierID == supplierId);

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

        [HttpPost("{id:int}/confirm")]
        public async Task<IActionResult> Confirm(int id)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) return NotFound();
            if (!string.Equals(po.Status, "Draft", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Chỉ xác nhận đơn ở trạng thái Draft.");

            po.Status = "Submitted";
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id:int}/receive")]
        public async Task<IActionResult> Submitted(int id)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) return NotFound();
            if (!string.Equals(po.Status, "Submitted", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Chỉ xác nhận đơn ở trạng thái Submitted.");

            po.Status = "Received";
            await _context.SaveChangesAsync();
            return NoContent();
        }

    }
}
