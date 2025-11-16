
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventoryManagement.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StocksController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StocksController(AppDbContext db) => _db = db;

        [HttpGet("warehouses")]
        public async Task<IActionResult> GetWarehouses(CancellationToken ct)
        {
            var items = await _db.Locations
                .AsNoTracking()
                .Where(l => l.IsActive && l.LocationType == "WAREHOUSE")
                .OrderBy(l => l.Name)
                .Select(l => new { l.LocationID, l.Name })
                .ToListAsync(ct);

            return Ok(items);
        }

        public sealed class WarehouseStockItemDto
        {
            public int GoodId { get; set; }
            public string SKU { get; set; } = "";
            public string GoodName { get; set; } = "";
            public int? CategoryId { get; set; }
            public string? CategoryName { get; set; }
            public decimal OnHand { get; set; }
            public decimal Reserved { get; set; }
            public decimal InTransit { get; set; }
            public decimal Available => OnHand - Reserved;
        }

        public sealed class PagedResult<T>
        {
            public int Page { get; set; }
            public int PageSize { get; set; }
            public long TotalItems { get; set; }
            public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
            public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        }

        // GET: api/stocks/warehouse/10?search=...&categoryId=...&onlyAvailable=true&page=1&pageSize=20&sort=available_desc
        [HttpGet("warehouse/{warehouseLocationId:int}")]
        public async Task<ActionResult<PagedResult<WarehouseStockItemDto>>> GetWarehouseStocks(
            int warehouseLocationId,
            [FromQuery] string? search,
            [FromQuery] int? categoryId,
            [FromQuery] bool onlyAvailable = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = "sku_asc",
            CancellationToken ct = default)
        {
            if (warehouseLocationId <= 0) return BadRequest("warehouseLocationId is required.");
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            // Build descendant LocationIDs (warehouse + bins)
            var allLocs = await _db.Locations.AsNoTracking().ToListAsync(ct);
            var set = new HashSet<int>();
            var queue = new Queue<int>();
            if (!allLocs.Any(l => l.LocationID == warehouseLocationId))
                return NotFound(new { message = "Warehouse location not found" });

            set.Add(warehouseLocationId);
            queue.Enqueue(warehouseLocationId);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var child in allLocs.Where(l => l.ParentLocationID == cur))
                {
                    if (set.Add(child.LocationID))
                        queue.Enqueue(child.LocationID);
                }
            }

            var q = from s in _db.Stocks
                    join g in _db.Goods on s.GoodID equals g.GoodID
                    join c in _db.Categories on g.CategoryID equals c.CategoryID into cg
                    from c in cg.DefaultIfEmpty()
                    where set.Contains(s.LocationID)
                    select new { s, g, c };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var sTerm = search.Trim().ToLower();
                q = q.Where(x =>
                    x.g.Name.ToLower().Contains(sTerm) ||
                    x.g.SKU.ToLower().Contains(sTerm) ||
                    (x.g.Barcode != null && x.g.Barcode.ToLower().Contains(sTerm)));
            }

            if (categoryId.HasValue)
                q = q.Where(x => x.g.CategoryID == categoryId);

            var grouped = q
                .GroupBy(x => new { x.g.GoodID, x.g.SKU, x.g.Name, x.g.CategoryID, CategoryName = (string?)x.c.CategoryName })
                .Select(g => new WarehouseStockItemDto
                {
                    GoodId = g.Key.GoodID,
                    SKU = g.Key.SKU,
                    GoodName = g.Key.Name,
                    CategoryId = g.Key.CategoryID,
                    CategoryName = g.Key.CategoryName,
                    OnHand = g.Sum(x => x.s.OnHand),
                    Reserved = g.Sum(x => x.s.Reserved),
                    InTransit = g.Sum(x => x.s.InTransit)
                });

            if (onlyAvailable)
                grouped = grouped.Where(i => i.OnHand - i.Reserved > 0);

            grouped = sort?.ToLower() switch
            {
                "name_desc" => grouped.OrderByDescending(i => i.GoodName),
                "name_asc" => grouped.OrderBy(i => i.GoodName),
                "sku_desc" => grouped.OrderByDescending(i => i.SKU),
                "available_desc" => grouped.OrderByDescending(i => i.OnHand - i.Reserved),
                "available_asc" => grouped.OrderBy(i => i.OnHand - i.Reserved),
                "onhand_desc" => grouped.OrderByDescending(i => i.OnHand),
                "onhand_asc" => grouped.OrderBy(i => i.OnHand),
                _ => grouped.OrderBy(i => i.SKU)
            };

            var total = await grouped.LongCountAsync(ct);
            var items = await grouped.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            return Ok(new PagedResult<WarehouseStockItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
                Items = items
            });
        }
        // GET: api/stocks/store/2?search=...&categoryId=...&onlyAvailable=true&page=1&pageSize=20&sort=available_desc
        [HttpGet("store/{storeId:int}")]
        public async Task<ActionResult<PagedResult<WarehouseStockItemDto>>> GetStoreStocks(
            int storeId,
            [FromQuery] string? search,
            [FromQuery] int? categoryId,
            [FromQuery] bool onlyAvailable = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = "sku_asc",
            CancellationToken ct = default)
        {
            if (storeId <= 0) return BadRequest("storeId is required.");
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            // 1) Tìm LocationID root của store
            var rootLocationId = await _db.Stores
                .Where(s => s.StoreID == storeId)
                .Select(s => (int?)s.LocationID)
                .FirstOrDefaultAsync(ct);

            if (rootLocationId == null)
                return NotFound(new { message = "Store not found" });

            // 2) Build tập LocationID con (STORE + các BIN bên dưới)
            var allLocs = await _db.Locations.AsNoTracking().ToListAsync(ct);
            if (!allLocs.Any(l => l.LocationID == rootLocationId.Value))
                return NotFound(new { message = "Store location not found" });

            var set = new HashSet<int>();
            var queue = new Queue<int>();

            set.Add(rootLocationId.Value);
            queue.Enqueue(rootLocationId.Value);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var child in allLocs.Where(l => l.ParentLocationID == cur))
                {
                    if (set.Add(child.LocationID))
                        queue.Enqueue(child.LocationID);
                }
            }

            // 3) Query Stocks + Goods + Categories cho các LocationID đó
            var q =
                from s in _db.Stocks
                join g in _db.Goods on s.GoodID equals g.GoodID
                join c in _db.Categories on g.CategoryID equals c.CategoryID into cg
                from c in cg.DefaultIfEmpty()
                where set.Contains(s.LocationID)
                select new { s, g, c };

            // 4) Search
            if (!string.IsNullOrWhiteSpace(search))
            {
                var sTerm = search.Trim().ToLower();
                q = q.Where(x =>
                    x.g.Name.ToLower().Contains(sTerm) ||
                    x.g.SKU.ToLower().Contains(sTerm) ||
                    (x.g.Barcode != null && x.g.Barcode.ToLower().Contains(sTerm)));
            }

            // 5) Filter theo Category
            if (categoryId.HasValue)
                q = q.Where(x => x.g.CategoryID == categoryId);

            // 6) Group & project DTO
            var grouped =
                q.GroupBy(x => new
                {
                    x.g.GoodID,
                    x.g.SKU,
                    x.g.Name,
                    x.g.CategoryID,
                    CategoryName = (string?)x.c.CategoryName
                })
                .Select(g => new WarehouseStockItemDto
                {
                    GoodId = g.Key.GoodID,
                    SKU = g.Key.SKU,
                    GoodName = g.Key.Name,
                    CategoryId = g.Key.CategoryID,
                    CategoryName = g.Key.CategoryName,
                    OnHand = g.Sum(x => x.s.OnHand),
                    Reserved = g.Sum(x => x.s.Reserved),
                    InTransit = g.Sum(x => x.s.InTransit)
                });

            // 7) Chỉ lấy Available > 0 nếu cần
            if (onlyAvailable)
                grouped = grouped.Where(i => i.OnHand - i.Reserved > 0);

            // 8) Sort – giữ đúng convention như warehouse
            grouped = (sort ?? "sku_asc").ToLower() switch
            {
                "name_desc" => grouped.OrderByDescending(i => i.GoodName),
                "name_asc" => grouped.OrderBy(i => i.GoodName),
                "sku_desc" => grouped.OrderByDescending(i => i.SKU),
                "available_desc" => grouped.OrderByDescending(i => i.OnHand - i.Reserved),
                "available_asc" => grouped.OrderBy(i => i.OnHand - i.Reserved),
                "onhand_desc" => grouped.OrderByDescending(i => i.OnHand),
                "onhand_asc" => grouped.OrderBy(i => i.OnHand),
                _ => grouped.OrderBy(i => i.SKU)
            };

            // 9) Paging
            var total = await grouped.LongCountAsync(ct);
            var items = await grouped
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Ok(new PagedResult<WarehouseStockItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
                Items = items
            });
        }

    }
}
