using System.Linq;
using System.Threading.Tasks;
using InventoryManagement.Data;
using InventoryManagement.Models; // Cần using Models
using InventoryManagement.Models.Views;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/lookups")]
    [Authorize(Roles = "WarehouseManager,StoreManager,Administrator")] 
    public class LookupsController : BaseApiController 
    {
        private readonly AppDbContext _db;

        public LookupsController(AppDbContext db)
        {
            _db = db; 
        }

        // ========== LOOKUPS ===========

        [HttpGet("goods")]
        public async Task<IActionResult> LookupGoods([FromQuery] string? q, [FromQuery] int? locationId, [FromQuery] int top = 20)
        {
            var query = _db.Goods.AsNoTracking().Select(g => new { g.GoodID, g.Name, g.Barcode, g.Unit });
            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim();
                query = query.Where(g => g.Name.Contains(key) || (g.Barcode ?? "").Contains(key));
            }
            var goods = await query.Take(top).ToListAsync();
            if (locationId == null) return Ok(goods);

            var goodIds = goods.Select(x => x.GoodID).ToList();
            var avail = await _db.Set<StockByGoodView>().AsNoTracking()
                .Where(v => v.LocationID == locationId && goodIds.Contains(v.GoodID))
                .Select(v => new { v.GoodID, v.Available }).ToListAsync();
            var map = avail.ToDictionary(x => x.GoodID, x => x.Available);
            var result = goods.Select(g => new { g.GoodID, g.Name, g.Barcode, g.Unit, Available = map.TryGetValue(g.GoodID, out var a) ? a : 0m });
            return Ok(result);
        }

        [HttpGet("locations")]
        public async Task<IActionResult> LookupLocations([FromQuery] string? type, [FromQuery] bool? active) // Thêm active
        {
            var q = _db.Locations.AsNoTracking().AsQueryable();
            
            if (!string.IsNullOrWhiteSpace(type))
            {
                // Sửa lỗi LINQ
                var typeLower = type.ToLower();
                q = q.Where(l => l.LocationType != null && l.LocationType.ToLower() == typeLower); 
            }
            if (active != null)
                q = q.Where(l => l.IsActive == active);

            var list = await q.OrderBy(l => l.Name) // Sửa: OrderBy Name
                .Select(l => new { locationID = l.LocationID, name = l.Name, type = l.LocationType, active = l.IsActive })
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("suppliers")]
        [Authorize(Roles = "WarehouseManager,Administrator")] 
        public async Task<IActionResult> LookupSuppliers([FromQuery] string? q, [FromQuery] int top = 30)
        {
            var s = _db.Suppliers.AsNoTracking().Select(x => new { x.SupplierID, x.Name, x.PhoneNumber, x.Email });
            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim();
                s = s.Where(x => x.Name.Contains(key));
            }
            return Ok(await s.OrderBy(x => x.Name).Take(top).ToListAsync());
        }
        
        // === API MỚI (Chuyển từ TransferController) ===
        // GET /api/lookups/stock-available?locationId=...&kw=...
        [HttpGet("stock-available")]
        [Authorize(Roles = "StoreManager,Administrator")] // Chỉ SM cần
        public async Task<IActionResult> StockAvailable([FromQuery] int locationId, [FromQuery] string? kw)
        {
            if (locationId <= 0) return BadRequest("locationId là bắt buộc.");

            var stockQ = _db.Stocks.AsNoTracking()
                .Where(s => s.LocationID == locationId)
                .GroupBy(s => s.GoodID)
                .Select(g => new { GoodID = g.Key, Available = g.Sum(x => x.OnHand - x.Reserved - x.InTransit) });

            var q =
                from s in stockQ
                join g in _db.Goods on s.GoodID equals g.GoodID
                select new
                {
                    s.GoodID,
                    sku = EF.Property<string>(g, "SKU"),
                    goodName = EF.Property<string>(g, "Name"),
                    unit = EF.Property<string>(g, "Unit"),
                    barcode = EF.Property<string>(g, "Barcode"),
                    available = s.Available
                };

            if (!string.IsNullOrWhiteSpace(kw))
            {
                var key = kw.Trim().ToLower();
                q = q.Where(x =>
                    (x.goodName ?? "").ToLower().Contains(key) ||
                    (x.sku ?? "").ToLower().Contains(key) ||
                    (x.barcode ?? "").ToLower().Contains(key) 
                );
            }

            var list = await q.OrderByDescending(x => x.available).ThenBy(x => x.goodName).Take(50).ToListAsync();
            return Ok(list);
        }
    }
}