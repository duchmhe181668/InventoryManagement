using InventoryManagement.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SalesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public SalesController(AppDbContext context) => _context = context;

        public sealed class GoodSearchDto
        {
            public int GoodID { get; set; }
            public string SKU { get; set; } = "";
            public string Name { get; set; } = "";
            public string Unit { get; set; } = "";
            public string? Barcode { get; set; }
            public string? CategoryName { get; set; }
            public string? ImageURL { get; set; }
            public decimal PriceSell { get; set; }
            public decimal QuantityAvailable { get; set; }
            public decimal OnHand { get; set; }
            public decimal Reserved { get; set; }
            public decimal InTransit { get; set; }
        }


        [HttpGet("goods")]
        public async Task<ActionResult<IEnumerable<GoodSearchDto>>> SearchGoods(
            [FromQuery] string q,
            [FromQuery] int? locationId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest("Query (q) is required.");

            var s = q.Trim();
            bool parsedId = int.TryParse(s, out var idNum);

            var goodsQ = _context.Goods
                .AsNoTracking()
                .Include(g => g.Category)
                .Where(g => (parsedId && g.GoodID == idNum) || EF.Functions.Like(g.SKU, $"%{s}%") || (g.Barcode != null && EF.Functions.Like(g.Barcode, $"%{s}%")) || EF.Functions.Like(g.Name, $"%{s}%"))
                .Select(g => new
                {
                    g.GoodID,
                    g.SKU,
                    g.Name,
                    g.Unit,
                    g.Barcode,
                    g.ImageURL,
                    BasePriceSell = g.PriceSell,
                    CategoryName = g.Category != null ? g.Category.CategoryName : null
                })
                .OrderBy(x => x.Name);

            var goods = await goodsQ.ToListAsync(ct);
            if (goods.Count == 0) return Ok(Array.Empty<GoodSearchDto>());

            var goodIds = goods.Select(x => x.GoodID).ToList();

            var stocks = _context.Stocks.AsNoTracking().Where(s2 => goodIds.Contains(s2.GoodID));
            if (locationId.HasValue)
                stocks = stocks.Where(s2 => s2.LocationID == locationId.Value);

            var stockAgg = await stocks
                .GroupBy(sg => sg.GoodID)
                .Select(g => new
                {
                    GoodID = g.Key,
                    OnHand = g.Sum(x => x.OnHand),
                    Reserved = g.Sum(x => x.Reserved),
                    InTransit = g.Sum(x => x.InTransit),
                    Available = g.Sum(x => x.OnHand - x.Reserved)
                })
                .ToListAsync(ct);

            var stockDict = stockAgg.ToDictionary(x => x.GoodID, x => x);

            int? storeId = null;
            if (locationId.HasValue)
            {
                storeId = await _context.Stores
                    .AsNoTracking()
                    .Where(st => st.LocationID == locationId.Value)
                    .Select(st => (int?)st.StoreID)
                    .FirstOrDefaultAsync(ct);
            }


            var priceDict = new Dictionary<int, decimal>();
            if (storeId.HasValue)
            {
                var latestStorePrices = await _context.StorePrices
                    .AsNoTracking()
                    .Where(sp => sp.StoreID == storeId.Value && goodIds.Contains(sp.GoodID))
                    .GroupBy(sp => sp.GoodID)
                    .Select(g => g.OrderByDescending(x => x.EffectiveFrom)
                    .Select(x => new { x.GoodID, x.PriceSell })
                    .FirstOrDefault()!)
                    .ToListAsync(ct);

                priceDict = latestStorePrices
                    .Where(x => x != null)
                    .ToDictionary(x => x.GoodID, x => x.PriceSell);
            }

            var result = goods.Select(g => new GoodSearchDto
            {
                GoodID = g.GoodID,
                SKU = g.SKU,
                Name = g.Name,
                Unit = g.Unit,
                Barcode = g.Barcode,
                ImageURL = g.ImageURL,
                CategoryName = g.CategoryName,
                OnHand = stockDict.TryGetValue(g.GoodID, out var st) ? st.OnHand : 0,
                Reserved = stockDict.TryGetValue(g.GoodID, out st) ? st.Reserved : 0,
                InTransit = stockDict.TryGetValue(g.GoodID, out st) ? st.InTransit : 0,
                QuantityAvailable = stockDict.TryGetValue(g.GoodID, out st) ? st.Available : 0,
                PriceSell = priceDict.TryGetValue(g.GoodID, out var px) ? px : g.BasePriceSell
            }).ToList();

            return Ok(result);
        }
    }
}
