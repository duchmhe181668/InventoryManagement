using System.Linq;
using System.Threading.Tasks;
using InventoryManagement.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// Ghi chú: Các using DTOs đã không còn cần thiết

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : BaseApiController // Kế thừa BaseApiController
    {
        private readonly AppDbContext _db;

        public OrdersController(AppDbContext db)
        {
            _db = db;
        }

        // === API MỚI ĐỂ LẤY DANH SÁCH TỔNG HỢP ===
        [HttpGet("all-orders")]
        [Authorize(Roles = "WarehouseManager,StoreManager,Administrator")]
        public async Task<IActionResult> ListAllOrders()
        {
            var purchases = await _db.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Select(p => new { Id = p.POID, Type = "Purchase Order", Status = p.Status, CreatedAt = p.CreatedAt, Details = $"Tới NCC: {p.Supplier.Name}" })
                .ToListAsync();

            var transfers = await _db.Transfers
                .AsNoTracking()
                .Include(t => t.FromLocation)
                .Include(t => t.ToLocation)
                .Select(t => new { Id = t.TransferID, Type = "Stock Transfer", Status = t.Status, CreatedAt = t.CreatedAt, Details = $"Từ: {t.FromLocation.Name} → Tới: {t.ToLocation.Name}" })
                .ToListAsync();

            var combinedList = purchases.Cast<dynamic>().Concat(transfers.Cast<dynamic>())
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            return Ok(combinedList);
        }
    }
}