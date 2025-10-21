using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Data;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StoresController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StoresController(AppDbContext db) { _db = db; }

        // GET: api/Stores/store-location/2
        [HttpGet("store-location/{storeId:int}")]
        public async Task<IActionResult> GetStoreLocation(int storeId)
        {
            var locId = await _db.Stores
                .Where(s => s.StoreID == storeId)
                .Select(s => (int?)s.LocationID)
                .FirstOrDefaultAsync();

            if (locId == null) return NotFound(new { message = "Store not found" });
            return Ok(new { storeId, locationId = locId.Value });
        }

        // GET: api/Stores/2/goods?search=abc&page=1&pageSize=20&sort=name
        [HttpGet("{storeId:int}/goods")]
        public async Task<IActionResult> GetStoreGoods(
            int storeId,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sort = "name")
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            // 1) Lấy LocationID của store
            var locationId = await _db.Stores
                .Where(s => s.StoreID == storeId)
                .Select(s => (int?)s.LocationID)
                .FirstOrDefaultAsync();

            if (locationId == null)
                return NotFound(new { message = "Store not found" });

            var today = DateTime.Today;

            // 2) Goods + Category + search (AsNoTracking để nhẹ)
            var goods = _db.Goods
                .AsNoTracking()
                .Include(g => g.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                goods = goods.Where(g =>
                    g.Name.Contains(q) ||
                    g.SKU.Contains(q) ||
                    (g.Barcode != null && g.Barcode.Contains(q))
                );
            }

            // 3) Tồn theo Location từ view v_StockByGood
            var avail = _db.StockByGood
                .AsNoTracking()
                .Where(v => v.LocationID == locationId.Value);

            // 4) Giá theo store: chọn bản có EffectiveFrom lớn nhất <= today
            var spBase = _db.StorePrices
                .AsNoTracking()
                .Where(p => p.StoreID == storeId && p.EffectiveFrom <= today);

            var latestEffPerGood = spBase
                .GroupBy(p => p.GoodID)
                .Select(g => new { GoodID = g.Key, EffectiveFrom = g.Max(x => x.EffectiveFrom) });

            var latestStorePrices =
                from p in spBase
                join l in latestEffPerGood
                    on new { p.GoodID, p.EffectiveFrom }
                    equals new { l.GoodID, l.EffectiveFrom }
                select new { p.GoodID, p.PriceSell };

            // 5) LEFT JOIN và project DTO – KHÔNG so sánh entity với null
            var query =
                from g in goods
                join a in avail on g.GoodID equals a.GoodID into ga
                from a in ga.DefaultIfEmpty()
                join lp in latestStorePrices on g.GoodID equals lp.GoodID into glp
                from lp in glp.DefaultIfEmpty()
                select new StoreGoodListItemDto
                {
                    GoodID = g.GoodID,
                    Name = g.Name,
                    SKU = g.SKU,
                    Barcode = g.Barcode,
                    Unit = g.Unit,
                    ImageURL = g.ImageURL,
                    CategoryName = g.Category.CategoryName,   // để EF tự LEFT JOIN

                    OnHand = (decimal?)a.OnHand ?? 0m,
                    Reserved = (decimal?)a.Reserved ?? 0m,
                    InTransit = (decimal?)a.InTransit ?? 0m,
                    Available = (decimal?)a.Available ?? 0m,

                    PriceSell = (decimal?)lp.PriceSell ?? g.PriceSell
                };

            // 6) Sort
            query = (sort ?? "name").ToLowerInvariant() switch
            {
                "sku" => query.OrderBy(x => x.SKU).ThenBy(x => x.Name),
                "pricesell" => query.OrderByDescending(x => x.PriceSell).ThenBy(x => x.Name),
                "available" => query.OrderByDescending(x => x.Available).ThenBy(x => x.Name),
                _ => query.OrderBy(x => x.Name).ThenBy(x => x.SKU)
            };

            // 7) Paging
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var pageCount = (int)Math.Ceiling(total / (double)pageSize);

            return Ok(new { page, pageSize, total, pageCount, items });
        }

        public class StoreGoodListItemDto
        {
            public int GoodID { get; set; }
            public string Name { get; set; } = "";
            public string SKU { get; set; } = "";
            public string? Barcode { get; set; }
            public string? Unit { get; set; }
            public string? ImageURL { get; set; }
            public string? CategoryName { get; set; }

            public decimal OnHand { get; set; }
            public decimal Reserved { get; set; }
            public decimal InTransit { get; set; }
            public decimal Available { get; set; }

            public decimal PriceSell { get; set; }
        }
    }
}
