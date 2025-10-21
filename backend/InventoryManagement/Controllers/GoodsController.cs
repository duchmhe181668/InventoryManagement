using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GoodsController : ControllerBase
    {
        private readonly AppDbContext _context; // dùng AppDbContext để khớp project
        public GoodsController(AppDbContext context) => _context = context;

        // GET: /api/goods?page=1&pageSize=20&sort=name|-name|sku|-sku|priceSell|-priceSell&search=...
        [HttpGet]
        public async Task<IActionResult> GetPaged(
            int page = 1,
            int pageSize = 20,
            string? sort = "name",
            string? search = null,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            sort = (sort ?? "name").Trim().ToLowerInvariant();

            // chỉ cho phép những khóa sort hợp lệ
            if (sort is not ("name" or "-name" or "sku" or "-sku" or "pricesell" or "-pricesell" or "pricecost" or "-pricecost"))
                sort = "name";

            var q = _context.Goods
                .AsNoTracking()
                .Include(g => g.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(g =>
                    g.SKU.Contains(s) ||
                    g.Name.Contains(s) ||
                    (g.Barcode != null && g.Barcode.Contains(s)));
            }

            q = sort switch
            {
                "name" => q.OrderBy(g => g.Name),
                "-name" => q.OrderByDescending(g => g.Name),
                "sku" => q.OrderBy(g => g.SKU),
                "-sku" => q.OrderByDescending(g => g.SKU),
                "pricesell" => q.OrderBy(g => g.PriceSell),
                "-pricesell" => q.OrderByDescending(g => g.PriceSell),
    "pricecost" => q.OrderBy(g => g.PriceCost),
    "-pricecost" => q.OrderByDescending(g => g.PriceCost),
                _ => q.OrderBy(g => g.Name)
            };

            var totalItems = await q.CountAsync(ct);

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new
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
                    CategoryName = g.Category != null ? g.Category.CategoryName : null
                })
                .ToListAsync(ct);

            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            return Ok(new
            {
                items,
                page,
                pageSize,
                totalPages,
                totalItems
            });
        }

        // GET: /api/goods/5
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
        {
            var g = await _context.Goods.AsNoTracking()
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
                CategoryName = g.Category != null ? g.Category.CategoryName : null
                // Nếu có RowVersion: RowVersion = g.RowVersion
            });
        }

        // POST: /api/goods
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Good dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // SKU unique
            var exists = await _context.Goods.AnyAsync(x => x.SKU == dto.SKU, ct);
            if (exists)
                return Conflict(new ProblemDetails { Title = "SKU already exists." });

            _context.Goods.Add(dto);
            await _context.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetById), new { id = dto.GoodID }, new
            {
                dto.GoodID,
                dto.SKU,
                dto.Name,
                dto.Unit,
                dto.Barcode,
                dto.ImageURL,
                dto.PriceCost,
                dto.PriceSell,
                dto.CategoryID
            });
        }

        // PUT: /api/goods/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Good dto, CancellationToken ct = default)
        {
            // cho phép body không có ID; nếu có thì phải khớp route
            if (dto.GoodID != 0 && dto.GoodID != id)
                return BadRequest(new ProblemDetails { Title = "Mismatched GoodID." });

            var entity = await _context.Goods.FirstOrDefaultAsync(x => x.GoodID == id, ct);
            if (entity == null) return NotFound();

            // SKU unique (exclude self)
            var skuTaken = await _context.Goods.AnyAsync(x => x.SKU == dto.SKU && x.GoodID != id, ct);
            if (skuTaken)
                return Conflict(new ProblemDetails { Title = "SKU already exists." });

            entity.SKU = dto.SKU;
            entity.Name = dto.Name;
            entity.Unit = dto.Unit;
            entity.Barcode = dto.Barcode;
            entity.ImageURL = dto.ImageURL;
            entity.PriceCost = dto.PriceCost;
            entity.PriceSell = dto.PriceSell;
            entity.CategoryID = dto.CategoryID;

            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // DELETE: /api/goods/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
        {
            var entity = await _context.Goods.FirstOrDefaultAsync(x => x.GoodID == id, ct);
            if (entity == null) return NotFound();

            _context.Goods.Remove(entity);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
