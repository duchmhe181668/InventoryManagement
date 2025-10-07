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

        // -------------------- Search goods (giữ nguyên) --------------------
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
                .Where(g => (parsedId && g.GoodID == idNum)
                            || EF.Functions.Like(g.SKU, $"%{s}%")
                            || (g.Barcode != null && EF.Functions.Like(g.Barcode, $"%{s}%"))
                            || EF.Functions.Like(g.Name, $"%{s}%"))
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

        // -------------------- Map storeId -> locationId --------------------
        [HttpGet("store-location")]
        public async Task<ActionResult<int>> GetLocationIdByStore(
            [FromQuery] int storeId,
            CancellationToken ct)
        {
            var locId = await _context.Stores
                .AsNoTracking()
                .Where(st => st.StoreID == storeId)
                .Select(st => (int?)st.LocationID)
                .FirstOrDefaultAsync(ct);

            if (!locId.HasValue) return NotFound("Store has no LocationID configured.");
            return locId.Value;
        }

        // ========================= THANH TOÁN ==============================
        public sealed class SellDto
        {
            public int GoodID { get; set; }
            public decimal QuantitySold { get; set; }
        }

        public sealed class CheckoutDto
        {
            public int LocationId { get; set; }
            public List<SellDto> Items { get; set; } = new();
            // (tuỳ mở rộng) public string? PaymentMethod { get; set; }
            // (tuỳ mở rộng) public decimal? DiscountAmount { get; set; }
            // (tuỳ mở rộng) public string? Note { get; set; }
        }

        private static decimal AvailableOf(InventoryManagement.Models.Stock s)
            => s.OnHand - s.Reserved;

        // --- Bán từng mặt hàng (tương thích FE cũ): /api/sales/sell?locationId=1 ---
        [HttpPost("sell")]
        public async Task<IActionResult> SellOne(
            [FromBody] SellDto dto,
            [FromQuery] int locationId,
            CancellationToken ct)
        {
            if (dto == null || dto.GoodID <= 0 || dto.QuantitySold <= 0)
                return BadRequest("Invalid payload.");

            // Lấy 1 dòng stock của Good + Location
            var stock = await _context.Stocks
                .FirstOrDefaultAsync(s => s.GoodID == dto.GoodID && s.LocationID == locationId, ct);

            if (stock == null)
                return NotFound($"Stock not found for GoodID={dto.GoodID} at LocationID={locationId}.");

            var available = AvailableOf(stock);
            if (available < dto.QuantitySold)
                return BadRequest($"Insufficient stock for GoodID={dto.GoodID}. Available={available}, required={dto.QuantitySold}.");

            // Trừ tồn
            stock.OnHand -= dto.QuantitySold;

            await _context.SaveChangesAsync(ct);

            return Ok(new
            {
                dto.GoodID,
                LocationID = locationId,
                NewOnHand = stock.OnHand,
                NewReserved = stock.Reserved,
                NewAvailable = AvailableOf(stock)
            });
        }

        // --- Thanh toán cả giỏ: /api/sales/checkout ---
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutDto request, CancellationToken ct)
        {
            if (request == null || request.LocationId <= 0 || request.Items == null || request.Items.Count == 0)
                return BadRequest("locationId and items are required.");

            // Gom list GoodID
            var goodIds = request.Items
                .Where(i => i.GoodID > 0 && i.QuantitySold > 0)
                .Select(i => i.GoodID)
                .Distinct()
                .ToList();
            if (goodIds.Count == 0) return BadRequest("No valid item.");

            // Lấy tất cả stock liên quan
            var stocks = await _context.Stocks
                .Where(s => s.LocationID == request.LocationId && goodIds.Contains(s.GoodID))
                .ToListAsync(ct);

            // Kiểm tra thiếu dòng stock
            var missing = goodIds.Except(stocks.Select(s => s.GoodID)).ToList();
            if (missing.Count > 0)
                return NotFound($"Stock not found for goods: {string.Join(", ", missing)} at LocationID={request.LocationId}.");

            // Kiểm tra tồn khả dụng
            foreach (var item in request.Items)
            {
                var s = stocks.First(x => x.GoodID == item.GoodID);
                var available = AvailableOf(s);
                if (available < item.QuantitySold)
                    return BadRequest($"Insufficient stock for GoodID={item.GoodID}. Available={available}, required={item.QuantitySold}.");
            }

            // Transaction để trừ tồn đồng bộ
            using var trx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var item in request.Items)
                {
                    var s = stocks.First(x => x.GoodID == item.GoodID);
                    s.OnHand -= item.QuantitySold;
                }

                // TODO (nếu có entity hoá đơn/chi tiết):
                // var sale = new Sale { ... };
                // _context.Sales.Add(sale);
                // foreach(var item in request.Items) _context.SaleLines.Add(new SaleLine{...});
                // v.v... (đặt trong cùng transaction)

                await _context.SaveChangesAsync(ct);
                await trx.CommitAsync(ct);
            }
            catch
            {
                await trx.RollbackAsync(ct);
                throw;
            }

            // Trả về tồn mới
            var result = stocks.Select(s => new
            {
                s.GoodID,
                LocationID = s.LocationID,
                NewOnHand = s.OnHand,
                NewReserved = s.Reserved,
                NewAvailable = AvailableOf(s)
            }).ToList();

            return Ok(new
            {
                LocationID = request.LocationId,
                Items = result
            });
        }
    }
}
