using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GoodsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public GoodsController(AppDbContext context) => _context = context;

        // ========= DTOs =========
        public sealed class GoodListItemDto
        {
            public int GoodID { get; set; }
            public string SKU { get; set; } = "";
            public string Name { get; set; } = "";
            public string Unit { get; set; } = "";
            public string? Barcode { get; set; }
            public string? ImageURL { get; set; }
            public decimal PriceCost { get; set; }
            public decimal PriceSell { get; set; }
            public string? CategoryName { get; set; }
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

        public sealed class CreateGoodDto
        {
            public string SKU { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public string? Barcode { get; set; }
            public string? ImageURL { get; set; }
            public decimal PriceCost { get; set; }
            public decimal PriceSell { get; set; }
            public int? CategoryID { get; set; }
        }

        public sealed class UpdateGoodDto
        {
            public string SKU { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public string? Barcode { get; set; }
            public string? ImageURL { get; set; }
            public decimal PriceCost { get; set; }
            public decimal PriceSell { get; set; }
            public int? CategoryID { get; set; }
            public byte[]? RowVersion { get; set; } // nếu model Good có concurrency token
        }

        // ========= LIST (paged + search + sort) =========
        // sort: name, -name, sku, -sku, priceSell, -priceSell
        [HttpGet]
        public async Task<ActionResult<PagedResult<GoodListItemDto>>> GetGoods(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] string? sort = "name",
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var q = _context.Goods
                .AsNoTracking()
                .Include(g => g.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(g =>
                    EF.Functions.Like(g.Name, $"%{s}%") ||
                    EF.Functions.Like(g.SKU, $"%{s}%") ||
                    (g.Barcode != null && EF.Functions.Like(g.Barcode, $"%{s}%")));
            }

            if (categoryId.HasValue)
                q = q.Where(g => g.CategoryID == categoryId.Value);

            sort = (sort ?? "name").Trim().ToLowerInvariant();
            q = sort switch
            {
                "name" => q.OrderBy(g => g.Name),
                "-name" => q.OrderByDescending(g => g.Name),
                "sku" => q.OrderBy(g => g.SKU),
                "-sku" => q.OrderByDescending(g => g.SKU),
                "pricesell" => q.OrderBy(g => g.PriceSell),
                "-pricesell" => q.OrderByDescending(g => g.PriceSell),
                _ => q.OrderBy(g => g.Name)
            };

            var total = await q.CountAsync(ct);

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new GoodListItemDto
                {
                    GoodID = g.GoodID,
                    SKU = g.SKU,
                    Name = g.Name,
                    Unit = g.Unit,
                    Barcode = g.Barcode,
                    ImageURL = g.ImageURL,
                    PriceCost = g.PriceCost,
                    PriceSell = g.PriceSell,
                    CategoryName = g.Category != null ? g.Category.CategoryName : null
                })
                .ToListAsync(ct);

            return Ok(new PagedResult<GoodListItemDto>(items, total, page, pageSize));
        }

        // ========= GET BY ID =========
        [HttpGet("{id:int}")]
        public async Task<ActionResult<object>> GetGood(int id, CancellationToken ct = default)
        {
            var g = await _context.Goods
                .AsNoTracking()
                .Include(x => x.Category)
                .FirstOrDefaultAsync(x => x.GoodID == id, ct);

            if (g == null) return NotFound();

            return Ok(new
            {
                g.GoodID,
                g.SKU,
                g.Name,
                g.Unit,
                g.Barcode,
                g.ImageURL,
                g.PriceCost,
                g.PriceSell,
                g.CategoryID,
                CategoryName = g.Category?.CategoryName,
                // RowVersion = g.RowVersion // nếu cần trả về để update concurrency
            });
        }

        // ========= CREATE =========
        [HttpPost]
        public async Task<ActionResult<Good>> CreateGood([FromBody] CreateGoodDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.SKU)) return BadRequest("SKU is required.");
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Unit)) return BadRequest("Unit is required.");

            if (dto.CategoryID is int catId &&
                !await _context.Categories.AnyAsync(c => c.CategoryID == catId, ct))
                return BadRequest("CategoryID not found.");

            if (await _context.Goods.AnyAsync(g => g.SKU == dto.SKU, ct))
                return Conflict("SKU already exists.");

            if (!string.IsNullOrWhiteSpace(dto.Barcode) &&
                await _context.Goods.AnyAsync(g => g.Barcode == dto.Barcode, ct))
                return Conflict("Barcode already exists.");

            var entity = new Good
            {
                SKU = dto.SKU.Trim(),
                Name = dto.Name.Trim(),
                Unit = dto.Unit.Trim(),
                Barcode = string.IsNullOrWhiteSpace(dto.Barcode) ? null : dto.Barcode.Trim(),
                ImageURL = dto.ImageURL,
                PriceCost = dto.PriceCost,
                PriceSell = dto.PriceSell,
                CategoryID = dto.CategoryID
            };

            _context.Goods.Add(entity);
            await _context.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetGood), new { id = entity.GoodID }, entity);
        }

        // ========= UPDATE =========
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateGood(int id, [FromBody] UpdateGoodDto dto, CancellationToken ct = default)
        {
            var entity = await _context.Goods.FirstOrDefaultAsync(g => g.GoodID == id, ct);
            if (entity == null) return NotFound();

            if (dto.CategoryID is int catId &&
                !await _context.Categories.AnyAsync(c => c.CategoryID == catId, ct))
                return BadRequest("CategoryID not found.");

            if (await _context.Goods.AnyAsync(g => g.GoodID != id && g.SKU == dto.SKU, ct))
                return Conflict("SKU already exists.");

            if (!string.IsNullOrWhiteSpace(dto.Barcode) &&
                await _context.Goods.AnyAsync(g => g.GoodID != id && g.Barcode == dto.Barcode, ct))
                return Conflict("Barcode already exists.");

            // Concurrency (nếu sử dụng RowVersion)
            if (dto.RowVersion is not null)
                _context.Entry(entity).Property("RowVersion").OriginalValue = dto.RowVersion;

            entity.SKU = dto.SKU.Trim();
            entity.Name = dto.Name.Trim();
            entity.Unit = dto.Unit.Trim();
            entity.Barcode = string.IsNullOrWhiteSpace(dto.Barcode) ? null : dto.Barcode.Trim();
            entity.ImageURL = dto.ImageURL;
            entity.PriceCost = dto.PriceCost;
            entity.PriceSell = dto.PriceSell;
            entity.CategoryID = dto.CategoryID;

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Tuỳ UI
                return Conflict("The record was modified by another user. Please reload and try again.");
            }

            return NoContent();
        }

        // ========= DELETE =========
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteGood(int id, CancellationToken ct = default)
        {
            var entity = await _context.Goods.FirstOrDefaultAsync(g => g.GoodID == id, ct);
            if (entity == null) return NotFound();

            _context.Goods.Remove(entity);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }
        
    }
}
