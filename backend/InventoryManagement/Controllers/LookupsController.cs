using System.Linq;
using System.Threading.Tasks;
using InventoryManagement.Data;
using InventoryManagement.Models.Views;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/lookups")]
    [Authorize(Roles = "WarehouseManager,StoreManager,Administrator")] // Quyền chung
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
        public async Task<IActionResult> LookupLocations([FromQuery] string? type)
        {
            var q = _db.Locations.AsNoTracking().Select(l => new { l.LocationID, l.Name, l.LocationType, l.ParentLocationID, l.IsActive });
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => x.LocationType == type);
            return Ok(await q.OrderBy(x => x.Name).ToListAsync());
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
    }
}