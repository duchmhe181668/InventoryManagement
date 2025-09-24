using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Models.Views; // StockByGoodView
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

        // ===================== DTOs =====================
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
            public decimal Available { get; set; }      // tổng tồn khả dụng (sum(OnHand - Reserved))
            public decimal OnHand { get; set; }         // tổng OnHand
            public decimal Reserved { get; set; }       // tổng Reserved
            public decimal InTransit { get; set; }      // tổng InTransit
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
            // Nếu bạn muốn dùng concurrency, mở khóa thuộc tính dưới và map RowVersion:
            // public byte[]? RowVersion { get; set; }
        }

        // ===================== GET LIST (paged + search + filter + sort) =====================
        // Có thể lọc theo categoryId và locationId (để tính tồn theo 1 địa điểm).
        // sort: name, -name, priceSell, -priceSell, available, -available
        [HttpGet]
        public async Task<ActionResult<PagedResult<GoodListItemDto>>> GetGoods(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] int? locationId = null,
            [FromQuery] string? sort = "name",
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : (pageSize > 100 ? 100 : pageSize);

            var q = _context.Goods
                .AsNoTracking()
                .Include(g => g.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(g => g.Name.Contains(s) || g.SKU.Contains(s) || (g.Barcode != null && g.Barcode.Contains(s)));
            }

            if (categoryId.HasValue)
                q = q.Where(g => g.CategoryID == categoryId.Value);

            // Chuẩn bị nguồn tồn (sử dụng view v_StockByGood nếu có, nếu không fallback từ Stocks)
            var stockAggQuery =
                locationId is int locId
                    ? _context.StockByGood    // view: đã SUM theo GoodID & LocationID
                        .Where(v => v.LocationID == locId)
                        .GroupBy(v => v.GoodID)
                        .Select(g => new
                        {
                            GoodID = g.Key,
                            OnHand = g.Sum(x => x.OnHand),
                            Reserved = g.Sum(x => x.Reserved),
                            InTransit = g.Sum(x => x.InTransit),
                            Available = g.Sum(x => x.Available)
                        })
                    : _context.StockByGood
                        .GroupBy(v => v.GoodID)
                        .Select(g => new
                        {
                            GoodID = g.Key,
                            OnHand = g.Sum(x => x.OnHand),
                            Reserved = g.Sum(x => x.Reserved),
                            InTransit = g.Sum(x => x.InTransit),
                            Available = g.Sum(x => x.Available)
                        });

            // Nếu bạn CHƯA map view v_StockByGood, uncomment fallback dưới và comment block trên:
            /*
            var stockAggQuery =
                locationId is int locId
                    ? _context.Stocks.Where(s => s.LocationID == locId)
                        .GroupBy(s => s.GoodID)
                        .Select(g => new
                        {
                            GoodID = g.Key,
                            OnHand = g.Sum(x => x.OnHand),
                            Reserved = g.Sum(x => x.Reserved),
                            InTransit = g.Sum(x => x.InTransit),
                            Available = g.Sum(x => x.OnHand - x.Reserved)
                        })
                    : _context.Stocks
                        .GroupBy(s => s.GoodID)
                        .Select(g => new
                        {
                            GoodID = g.Key,
                            OnHand = g.Sum(x => x.OnHand),
                            Reserved = g.Sum(x => x.Reserved),
                            InTransit = g.Sum(x => x.InTransit),
                            Available = g.Sum(x => x.OnHand - x.Reserved)
                        });
            */

            // Join goods với tổng tồn
            var qJoined =
                from g in q
                join st in stockAggQuery on g.GoodID equals st.GoodID into gst
                from st in gst.DefaultIfEmpty()
                select new
                {
                    g,
                    Totals = new
                    {
                        OnHand = (decimal?)(st != null ? st.OnHand : 0) ?? 0,
                        Reserved = (decimal?)(st != null ? st.Reserved : 0) ?? 0,
                        InTransit = (decimal?)(st != null ? st.InTransit : 0) ?? 0,
                        Available = (decimal?)(st != null ? st.Available : 0) ?? 0
                    }
                };

            // Sort
            sort = (sort ?? "name").Trim().ToLowerInvariant();
            qJoined = sort switch
            {
                "name" => qJoined.OrderBy(x => x.g.Name),
                "-name" => qJoined.OrderByDescending(x => x.g.Name),
                "pricesell" => qJoined.OrderBy(x => x.g.PriceSell),
                "-pricesell" => qJoined.OrderByDescending(x => x.g.PriceSell),
                "available" => qJoined.OrderBy(x => x.Totals.Available),
                "-available" => qJoined.OrderByDescending(x => x.Totals.Available),
                _ => qJoined.OrderBy(x => x.g.Name)
            };

            var total = await qJoined.CountAsync(ct);

            var items = await qJoined
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new GoodListItemDto
                {
                    GoodID = x.g.GoodID,
                    SKU = x.g.SKU,
                    Name = x.g.Name,
                    Unit = x.g.Unit,
                    Barcode = x.g.Barcode,
                    ImageURL = x.g.ImageURL,
                    PriceCost = x.g.PriceCost,
                    PriceSell = x.g.PriceSell,
                    CategoryName = x.g.Category != null ? x.g.Category.CategoryName : null,
                    OnHand = x.Totals.OnHand,
                    Reserved = x.Totals.Reserved,
                    InTransit = x.Totals.InTransit,
                    Available = x.Totals.Available
                })
                .ToListAsync(ct);

            return Ok(new PagedResult<GoodListItemDto>(items, total, page, pageSize));
        }

        // ===================== GET BY ID =====================
        [HttpGet("{id:int}")]
        public async Task<ActionResult<object>> GetGood(int id, [FromQuery] int? locationId = null, CancellationToken ct = default)
        {
            var g = await _context.Goods
                .AsNoTracking()
                .Include(x => x.Category)
                .FirstOrDefaultAsync(x => x.GoodID == id, ct);

            if (g == null) return NotFound();

            // Tổng tồn cho 1 hàng hóa (có thể theo location)
            var stockAgg =
                locationId is int locId
                    ? await _context.StockByGood
                        .Where(v => v.GoodID == id && v.LocationID == locId)
                        .GroupBy(v => v.GoodID)
                        .Select(gr => new
                        {
                            OnHand = gr.Sum(x => x.OnHand),
                            Reserved = gr.Sum(x => x.Reserved),
                            InTransit = gr.Sum(x => x.InTransit),
                            Available = gr.Sum(x => x.Available)
                        }).FirstOrDefaultAsync(ct)
                    : await _context.StockByGood
                        .Where(v => v.GoodID == id)
                        .GroupBy(v => v.GoodID)
                        .Select(gr => new
                        {
                            OnHand = gr.Sum(x => x.OnHand),
                            Reserved = gr.Sum(x => x.Reserved),
                            InTransit = gr.Sum(x => x.InTransit),
                            Available = gr.Sum(x => x.Available)
                        }).FirstOrDefaultAsync(ct);

            // Fallback nếu chưa map view:
            // var stockAgg = locationId is int locId
            //     ? await _context.Stocks.Where(s => s.GoodID == id && s.LocationID == locId)
            //         .GroupBy(s => 1).Select(gr => new {
            //             OnHand = gr.Sum(x => x.OnHand),
            //             Reserved = gr.Sum(x => x.Reserved),
            //             InTransit = gr.Sum(x => x.InTransit),
            //             Available = gr.Sum(x => x.OnHand - x.Reserved)
            //         }).FirstOrDefaultAsync(ct)
            //     : await _context.Stocks.Where(s => s.GoodID == id)
            //         .GroupBy(s => 1).Select(gr => new {
            //             OnHand = gr.Sum(x => x.OnHand),
            //             Reserved = gr.Sum(x => x.Reserved),
            //             InTransit = gr.Sum(x => x.InTransit),
            //             Available = gr.Sum(x => x.OnHand - x.Reserved)
            //         }).FirstOrDefaultAsync(ct);

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
                CategoryID = g.CategoryID,
                CategoryName = g.Category?.CategoryName,
                Stock = stockAgg ?? new { OnHand = 0m, Reserved = 0m, InTransit = 0m, Available = 0m }
            });
        }

        // ===================== CREATE =====================
        [HttpPost]
        public async Task<ActionResult<Good>> CreateGood([FromBody] CreateGoodDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.SKU)) return BadRequest("SKU is required.");
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Unit)) return BadRequest("Unit is required.");

            // Validate FK
            if (dto.CategoryID is int catId &&
                !await _context.Categories.AnyAsync(c => c.CategoryID == catId, ct))
                return BadRequest("CategoryID not found.");

            // Check unique
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

        // ===================== UPDATE =====================
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateGood(int id, [FromBody] UpdateGoodDto dto, CancellationToken ct = default)
        {
            var entity = await _context.Goods.FirstOrDefaultAsync(g => g.GoodID == id, ct);
            if (entity == null) return NotFound();

            // Validate FK
            if (dto.CategoryID is int catId &&
                !await _context.Categories.AnyAsync(c => c.CategoryID == catId, ct))
                return BadRequest("CategoryID not found.");

            // Unique checks (exclude current)
            if (await _context.Goods.AnyAsync(g => g.GoodID != id && g.SKU == dto.SKU, ct))
                return Conflict("SKU already exists.");

            if (!string.IsNullOrWhiteSpace(dto.Barcode) &&
                await _context.Goods.AnyAsync(g => g.GoodID != id && g.Barcode == dto.Barcode, ct))
                return Conflict("Barcode already exists.");

            entity.SKU = dto.SKU.Trim();
            entity.Name = dto.Name.Trim();
            entity.Unit = dto.Unit.Trim();
            entity.Barcode = string.IsNullOrWhiteSpace(dto.Barcode) ? null : dto.Barcode.Trim();
            entity.ImageURL = dto.ImageURL;
            entity.PriceCost = dto.PriceCost;
            entity.PriceSell = dto.PriceSell;
            entity.CategoryID = dto.CategoryID;

            // Nếu dùng concurrency RowVersion:
            // if (dto.RowVersion is not null)
            //     _context.Entry(entity).Property("RowVersion").OriginalValue = dto.RowVersion;

            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ===================== DELETE =====================
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteGood(int id, CancellationToken ct = default)
        {
            var entity = await _context.Goods.FirstOrDefaultAsync(g => g.GoodID == id, ct);
            if (entity == null) return NotFound();

            _context.Goods.Remove(entity);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ===================== STOCK BREAKDOWN (by location & batch) =====================
        // GET: /api/goods/5/stock-breakdown?locationId=2
        [HttpGet("{id:int}/stock-breakdown")]
        public async Task<ActionResult<IEnumerable<object>>> GetStockBreakdown(
            int id,
            [FromQuery] int? locationId = null,
            CancellationToken ct = default)
        {
            // Lấy tồn chi tiết theo location/batch cho GoodID
            var q = _context.Stocks.AsNoTracking().Where(s => s.GoodID == id);

            if (locationId.HasValue) q = q.Where(s => s.LocationID == locationId.Value);

            var data = await q
                .Select(s => new
                {
                    s.LocationID,
                    s.BatchID,
                    s.OnHand,
                    s.Reserved,
                    s.InTransit,
                    Available = s.OnHand - s.Reserved
                })
                .OrderBy(x => x.LocationID).ThenBy(x => x.BatchID)
                .ToListAsync(ct);

            return Ok(data);
        }
    }
}
