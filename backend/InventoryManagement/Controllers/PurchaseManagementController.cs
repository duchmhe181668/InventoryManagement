using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Dto.PurchaseOrders;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/purchase-management")]
    [Authorize(Roles = "WarehouseManager,Administrator")]
    public class PurchaseManagementController : BaseApiController
    {
        private readonly AppDbContext _db;

        public PurchaseManagementController(AppDbContext db)
        {
            _db = db;
        }

        // ========== WAREHOUSE → SUPPLIER (PURCHASE ORDERS) ==========


        [HttpPost]
        public async Task<IActionResult> CreatePurchase([FromBody] PurchaseCreateDto dto)
        {
            // === CẬP NHẬT: Dùng hàm từ BaseApiController ===
            var userId = await GetCurrentUserIdAsync(_db);
            if (userId == null) return UserNotFound();

            var sup = await _db.Suppliers.FindAsync(dto.SupplierID);
            if (sup == null) return BadRequest("Supplier không hợp lệ.");

            using var tx = await _db.Database.BeginTransactionAsync();
            var status = string.Equals(dto.Status, "Submitted", StringComparison.OrdinalIgnoreCase) ? "Submitted" : "Draft";

            // Dùng userId.Value
            // VÀ GỌI HÀM GetVietnamTime() KẾ THỪA TỪ BASEAPICONTROLLER
            var po = new PurchaseOrder
            {
                SupplierID = dto.SupplierID,
                CreatedBy = userId.Value,
                CreatedAt = GetVietnamTime(),
                Status = status
            };
            _db.PurchaseOrders.Add(po);
            await _db.SaveChangesAsync();

            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.PurchaseOrderLines.Add(new PurchaseOrderLine { POID = po.POID, GoodID = it.GoodID, Quantity = it.Quantity, UnitPrice = it.UnitPrice });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpPut]
        public async Task<IActionResult> UpdatePurchase([FromBody] PurchaseUpdateDto dto)
        {
            var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");
            if (po.Status != "Draft") return BadRequest("Chỉ sửa khi PO đang Draft.");

            using var tx = await _db.Database.BeginTransactionAsync();
            _db.PurchaseOrderLines.RemoveRange(po.Lines ?? []);
            await _db.SaveChangesAsync();
            foreach (var it in dto.Items)
            {
                if (it.Quantity <= 0) continue;
                _db.PurchaseOrderLines.Add(new PurchaseOrderLine { POID = po.POID, GoodID = it.GoodID, Quantity = it.Quantity, UnitPrice = it.UnitPrice });
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitPurchase([FromBody] PurchaseSubmitDto dto)
        {
            var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.POID == dto.POID);
            if (po == null) return NotFound("PO không tồn tại.");
            if (po.Status != "Draft") return BadRequest("Chỉ submit khi đang Draft.");
            if (po.Lines == null || po.Lines.Count == 0) return BadRequest("PO không có dòng.");

            po.Status = "Submitted";
            await _db.SaveChangesAsync();
            return Ok(new { po.POID, po.Status });
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = "WarehouseManager,Administrator,Supplier")]
        public async Task<IActionResult> GetPurchase(int id)
        {
            var p = await _db.PurchaseOrders.AsNoTracking()
                .Where(x => x.POID == id)
                .Select(x => new { x.POID, x.SupplierID, CreatedBy = x.CreatedBy, x.CreatedAt, x.Status, Lines = (from l in _db.PurchaseOrderLines join g in _db.Goods on l.GoodID equals g.GoodID where l.POID == x.POID select new { l.POLineID, l.GoodID, g.Name, g.Barcode, g.Unit, l.Quantity, l.UnitPrice }).ToList() })
                .FirstOrDefaultAsync();
            return p == null ? NotFound() : Ok(p);
        }

        [HttpGet]
        [Authorize(Roles = "WarehouseManager,Administrator")]
        public async Task<IActionResult> ListPurchases([FromQuery] string? status, [FromQuery] int top = 50)
        {
            // === SỬA LỖI: ===
            // Thêm kiểu "IQueryable<PurchaseOrder>" một cách tường minh
            // để biến 'q' không bị lỗi khi gán lại ở dưới
            IQueryable<PurchaseOrder> q = _db.PurchaseOrders.AsNoTracking()
                                           .Include(p => p.Supplier); // Include vẫn hoạt động

            if (!string.IsNullOrWhiteSpace(status))
            {
                q = q.Where(p => p.Status == status); // Dòng này giờ sẽ OK
            }

            var data = await q.OrderByDescending(p => p.POID)
                .Select(p => new {
                    Id = p.POID,
                    Type = "Purchase Order",
                    Status = p.Status,
                    CreatedAt = p.CreatedAt,
                    Details = $"Tới NCC: {p.Supplier.Name}"
                })
                .Take(top)
                .ToListAsync();

            return Ok(data);
        }
    }
}