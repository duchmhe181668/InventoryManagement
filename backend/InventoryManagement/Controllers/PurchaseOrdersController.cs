using InventoryManagement.Data;
using InventoryManagement.Dto.PurchaseOrders;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Dto.ReceiptDto;

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


        [HttpGet("by-supplier/{supplierId:int}")]
        //[Authorize(Roles = "Supplier")]
        public async Task<ActionResult> GetBySupplier(
            int supplierId,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? poid = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            // var supplierIdClaim = User.FindFirst("supplier_id")?.Value;
            //if (string.IsNullOrEmpty(supplierIdClaim)) return Forbid();
            //supplierId = int.Parse(supplierIdClaim);

            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            var query = _context.PurchaseOrders
                .Include(po => po.Supplier)
                .Include(po => po.Lines)!.ThenInclude(l => l.Good)
                .Where(po => po.SupplierID == supplierId && po.Status != "Draft");

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(po => po.Status == status);

            if (from.HasValue)
                query = query.Where(po => po.CreatedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(po => po.CreatedAt <= to.Value.AddDays(1));

            if (poid.HasValue && poid.Value > 0)
                query = query.Where(po => po.POID == poid.Value);

            var totalCount = await query.CountAsync();

            var data = await query
                .OrderByDescending(po => po.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(po => new
                {
                    po.POID,
                    po.SupplierID,
                    SupplierName = po.Supplier != null ? po.Supplier.Name : "",
                    po.CreatedAt,
                    po.Status,
                    TotalLines = po.Lines.Count,
                    TotalAmount = po.Lines.Sum(l => l.Quantity * l.UnitPrice),

                    Lines = po.Lines.Select(l => new
                    {
                        l.POLineID,
                        l.GoodID,
                        GoodName = l.Good != null ? l.Good.Name : "",
                        Unit = l.Good != null ? l.Good.Unit : null,
                        l.Quantity,
                        l.UnitPrice
                    }).ToList()
                })
                .ToListAsync();

            if (!data.Any()) return NoContent();

            return Ok(new
            {
                total = totalCount,
                page,
                pageSize,
                data
            });
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
                        Unit = l.Good != null ? l.Good.Unit : "",
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

            var newStatus = dto.Status.Trim();

            //Cho phép Supplier hủy đơn (Submitted -> Cancelled)
            if (newStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                if (!po.Status.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Chỉ được hủy đơn ở trạng thái Submitted.");
                po.Status = "Cancelled";
                await _context.SaveChangesAsync();
                return NoContent();
            }

            //Cho phép Supplier xác nhận nhận đơn (Submitted -> Received)
            if (newStatus.Equals("Received", StringComparison.OrdinalIgnoreCase))
            {
                if (!po.Status.Equals("Submitted", StringComparison.OrdinalIgnoreCase))
                    return BadRequest($"Không thể chuyển từ {po.Status} sang {newStatus}.");
                po.Status = "Received";
                await _context.SaveChangesAsync();
                return NoContent();
            }

            return BadRequest("Bạn chỉ có thể cập nhật trạng thái sang Received hoặc Cancelled.");
        }
    }
}
