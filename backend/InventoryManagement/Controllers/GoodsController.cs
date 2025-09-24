using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/[controller]")] // => /api/goods
    [ApiController]
    public class GoodsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public GoodsController(AppDbContext context) => _context = context;

        // ===================== DTOs =====================
        public sealed class GoodListItemDto
        {
            public int GoodID { get; set; }
            public string Name { get; set; } = "";
            public string Unit { get; set; } = "";
            public DateTime? DateIn { get; set; }
            public decimal Quantity { get; set; }
            public decimal PriceSell { get; set; }
            public string? CategoryName { get; set; }
            public string? SupplierName { get; set; }
            public string? ImageURL { get; set; }
        }

        public sealed class PagedResult<T>
        {
            public IReadOnlyList<T> Items { get; }
            public int Total { get; }
            public int Page { get; }
            public int PageSize { get; }
            public int TotalPages { get; }

            public PagedResult(IReadOnlyList<T> items, int total, int page, int pageSize)
            {
                Items = items;
                Total = total;
                Page = page < 1 ? 1 : page;
                PageSize = pageSize < 1 ? 1 : pageSize;
                TotalPages = (int)Math.Ceiling((double)Total / PageSize);
            }
        }

        // DTO tạo & cập nhật
        public class CreateGoodDto
        {
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public DateTime? DateIn { get; set; }
            public string? ImageURL { get; set; }
            public decimal Quantity { get; set; }
            public decimal PriceCost { get; set; }
            public decimal PriceSell { get; set; }
            public int StoreID { get; set; }          // required
            public int? CategoryID { get; set; }
            public int? SupplierID { get; set; }
        }

        public class UpdateGoodDto
        {
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public DateTime? DateIn { get; set; }
            public string? ImageURL { get; set; }
            public decimal Quantity { get; set; }
            public decimal PriceCost { get; set; }
            public decimal PriceSell { get; set; }
            public int StoreID { get; set; }
            public int? CategoryID { get; set; }
            public int? SupplierID { get; set; }
        }

        // ===================== GET LIST (paged + search + filter + sort) =====================
        // GET: /api/goods/raw
        [HttpGet("raw")]
        public async Task<ActionResult<IEnumerable<object>>> GetAllGoods(CancellationToken ct)
        {
            var goods = await _context.Goods
                .AsNoTracking()
                .Select(g => new {
                    g.GoodID,
                    g.Name,
                    g.Unit,
                    g.DateIn,
                    g.Quantity,
                    g.PriceCost,
                    g.PriceSell,
                    g.StoreID,
                    g.CategoryID,
                    CategoryName = g.Category != null ? g.Category.CategoryName : null,
                    g.SupplierID,
                    SupplierName = g.Supplier != null ? g.Supplier.Name : null,
                    g.ImageURL
                })
                .ToListAsync(ct);

            return Ok(goods);
        }


        // GET: /api/goods
        [HttpGet]
        public async Task<ActionResult<PagedResult<GoodListItemDto>>> GetGoods(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] int? supplierId = null,
            [FromQuery] string? sort = "-dateIn", // name, -name, priceSell, -priceSell, quantity, -quantity, dateIn, -dateIn
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : (pageSize > 100 ? 100 : pageSize);

            var q = _context.Goods.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(g => g.Name.Contains(s));
            }

            if (categoryId is int cid)
                q = q.Where(g => g.CategoryID == cid);

            if (supplierId is int sid)
                q = q.Where(g => g.SupplierID == sid);

            sort = (sort ?? "-dateIn").Trim().ToLowerInvariant();
            q = sort switch
            {
                "name" => q.OrderBy(g => g.Name),
                "-name" => q.OrderByDescending(g => g.Name),
                "pricesell" => q.OrderBy(g => g.PriceSell),
                "-pricesell" => q.OrderByDescending(g => g.PriceSell),
                "quantity" => q.OrderBy(g => g.Quantity),
                "-quantity" => q.OrderByDescending(g => g.Quantity),
                "datein" => q.OrderBy(g => g.DateIn),
                "-datein" => q.OrderByDescending(g => g.DateIn),
                _ => q.OrderByDescending(g => g.DateIn)
            };

            var total = await q.CountAsync(ct);

            var items = await q
                .Select(g => new GoodListItemDto
                {
                    GoodID = g.GoodID,
                    Name = g.Name,
                    Unit = g.Unit,
                    DateIn = g.DateIn,
                    Quantity = g.Quantity,
                    PriceSell = g.PriceSell,
                    CategoryName = g.Category != null ? g.Category.CategoryName : null,
                    SupplierName = g.Supplier != null ? g.Supplier.Name : null,
                    ImageURL = g.ImageURL
                })
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Ok(new PagedResult<GoodListItemDto>(items, total, page, pageSize));
        }

        // ===================== GET BY ID =====================
        // GET: /api/goods/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Good>> GetGood(int id, CancellationToken ct)
        {
            var good = await _context.Goods
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.GoodID == id, ct);

            if (good == null) return NotFound();
            return Ok(good);
        }

        // ===================== CREATE =====================
        // POST: /api/goods
        [HttpPost]
        public async Task<ActionResult<Good>> CreateGood([FromBody] CreateGoodDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Unit)) return BadRequest("Unit is required.");

            // Validate FK tồn tại để tránh DbUpdateException
            if (!await _context.Stores.AnyAsync(s => s.StoreID == dto.StoreID, ct))
                return BadRequest("StoreID not found.");

            if (dto.CategoryID is int catId &&
                !await _context.Categories.AnyAsync(c => c.CategoryID == catId, ct))
                return BadRequest("CategoryID not found.");

            if (dto.SupplierID is int supId &&
                !await _context.Suppliers.AnyAsync(s => s.SupplierID == supId, ct))
                return BadRequest("SupplierID not found.");

            var entity = new Good
            {
                Name = dto.Name,
                Unit = dto.Unit,
                DateIn = dto.DateIn?.Date,
                ImageURL = dto.ImageURL,
                Quantity = dto.Quantity,
                PriceCost = dto.PriceCost,
                PriceSell = dto.PriceSell,
                StoreID = dto.StoreID,
                CategoryID = dto.CategoryID,
                SupplierID = dto.SupplierID
            };

            _context.Goods.Add(entity);
            await _context.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetGood), new { id = entity.GoodID }, entity);
        }

        // ===================== UPDATE =====================
        // PUT: /api/goods/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateGood(int id, [FromBody] UpdateGoodDto dto, CancellationToken ct)
        {
            var entity = await _context.Goods.FirstOrDefaultAsync(g => g.GoodID == id, ct);
            if (entity == null) return NotFound();

            // Validate FK tồn tại trước khi gán
            if (!await _context.Stores.AnyAsync(s => s.StoreID == dto.StoreID, ct))
                return BadRequest("StoreID not found.");

            if (dto.CategoryID is int catId &&
                !await _context.Categories.AnyAsync(c => c.CategoryID == catId, ct))
                return BadRequest("CategoryID not found.");

            if (dto.SupplierID is int supId &&
                !await _context.Suppliers.AnyAsync(s => s.SupplierID == supId, ct))
                return BadRequest("SupplierID not found.");

            entity.Name = dto.Name;
            entity.Unit = dto.Unit;
            entity.DateIn = dto.DateIn?.Date;
            entity.ImageURL = dto.ImageURL;
            entity.Quantity = dto.Quantity;
            entity.PriceCost = dto.PriceCost;
            entity.PriceSell = dto.PriceSell;
            entity.StoreID = dto.StoreID;
            entity.CategoryID = dto.CategoryID;
            entity.SupplierID = dto.SupplierID;

            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ===================== DELETE =====================
        // DELETE: /api/goods/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteGood(int id, CancellationToken ct)
        {
            var good = await _context.Goods.FirstOrDefaultAsync(g => g.GoodID == id, ct);
            if (good == null) return NotFound();

            _context.Goods.Remove(good); // nếu cần soft delete: thêm IsDeleted và set true
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
