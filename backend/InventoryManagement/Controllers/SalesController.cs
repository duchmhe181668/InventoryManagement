using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Authorization;
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

        public sealed class SellRequest
        {
            public int GoodID { get; set; }
            public decimal QuantitySold { get; set; }
            public int? LocationId { get; set; }      
            public decimal? UnitPrice { get; set; }  
            public int? CustomerID { get; set; }      
            public decimal? Discount { get; set; }    
            public string? Note { get; set; }         
        }

        public sealed class SellResponse
        {
            public int SaleID { get; set; }
            public int SaleLineID { get; set; }
            public decimal QuantitySold { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal NewAvailable { get; set; }  
        }

        [HttpPost("sell")]
        [Authorize] 
        public async Task<ActionResult<SellResponse>> SellOne([FromBody] SellRequest req, CancellationToken ct)
        {
            if (req == null || req.GoodID <= 0 || req.QuantitySold <= 0)
                return BadRequest("goodID và quantitySold > 0 là bắt buộc.");

            string? username = User.Identity?.Name;
            var user = await _context.Users.AsNoTracking()
                             .FirstOrDefaultAsync(u => u.Username == username, ct);
            if (user == null) return Unauthorized("User not found.");

            int? locationId = req.LocationId;
            if (!locationId.HasValue)
            {
                locationId = await _context.Stores.AsNoTracking()
                    .Where(s => s.UserID == user.UserID)
                    .Select(s => (int?)s.LocationID)
                    .FirstOrDefaultAsync(ct);
            }
            if (!locationId.HasValue)
                return BadRequest("Không xác định được Location cho user (store).");

            var good = await _context.Goods.AsNoTracking()
                            .FirstOrDefaultAsync(g => g.GoodID == req.GoodID, ct);
            if (good == null) return NotFound("Hàng hóa không tồn tại.");

            int? storeId = await _context.Stores.AsNoTracking()
                                .Where(st => st.LocationID == locationId.Value)
                                .Select(st => (int?)st.StoreID)
                                .FirstOrDefaultAsync(ct);

            decimal unitPrice = req.UnitPrice ?? good.PriceSell;
            if (storeId.HasValue && !req.UnitPrice.HasValue)
            {
                var sp = await _context.StorePrices.AsNoTracking()
                    .Where(p => p.StoreID == storeId.Value && p.GoodID == req.GoodID)
                    .OrderByDescending(p => p.EffectiveFrom)
                    .FirstOrDefaultAsync(ct);
                if (sp != null) unitPrice = sp.PriceSell;
            }

            var availability = await _context.Stocks.AsNoTracking()
                .Where(s => s.LocationID == locationId && s.GoodID == req.GoodID)
                .GroupBy(s => 1)
                .Select(g => new
                {
                    OnHand = g.Sum(x => x.OnHand),
                    Reserved = g.Sum(x => x.Reserved),
                    Available = g.Sum(x => x.OnHand - x.Reserved)
                })
                .FirstOrDefaultAsync(ct);

            decimal avail = availability?.Available ?? 0m;
            if (avail < req.QuantitySold)
                return Conflict($"Tồn khả dụng không đủ. Còn {avail} nhưng yêu cầu {req.QuantitySold}.");

            using var tx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                var sale = new Sale
                {
                    StoreLocationID = locationId.Value,
                    CustomerID = req.CustomerID,
                    CreatedBy = user.UserID,
                    Discount = req.Discount ?? 0m,
                    Status = "Completed" 
                };
                _context.Sales.Add(sale);
                await _context.SaveChangesAsync(ct);

                decimal remaining = req.QuantitySold;

                var stockRows = await _context.Stocks
                    .Where(s => s.LocationID == locationId && s.GoodID == req.GoodID && s.OnHand > 0)
                    .OrderBy(s => s.BatchID) 
                    .ToListAsync(ct);

                foreach (var row in stockRows)
                {
                    if (remaining <= 0) break;

                    var take = Math.Min(row.OnHand, remaining);
                    row.OnHand -= take;
                    remaining -= take;

                    _context.StockMovements.Add(new StockMovement
                    {
                        GoodID = req.GoodID,
                        Quantity = take,
                        FromLocationID = locationId.Value,
                        ToLocationID = null,
                        BatchID = row.BatchID,
                        UnitCost = good.PriceCost,
                        MovementType = "SALE",
                        RefTable = "Sales",
                        RefID = sale.SaleID,
                        Note = req.Note
                    });
                }

                if (remaining > 0)
                    return Conflict("Không thể trừ tồn theo batch (dữ liệu tồn kho thay đổi).");

                var line = new SaleLine
                {
                    SaleID = sale.SaleID,
                    GoodID = req.GoodID,
                    Quantity = req.QuantitySold,
                    UnitPrice = unitPrice,
                    BatchID = null 
                };
                _context.SaleLines.Add(line);

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var newAvail = await _context.Stocks.AsNoTracking()
                    .Where(s => s.LocationID == locationId && s.GoodID == req.GoodID)
                    .GroupBy(s => 1)
                    .Select(g => g.Sum(x => x.OnHand - x.Reserved))
                    .FirstOrDefaultAsync(ct);

                return Ok(new SellResponse
                {
                    SaleID = sale.SaleID,
                    SaleLineID = line.SaleLineID,
                    QuantitySold = req.QuantitySold,
                    UnitPrice = unitPrice,
                    NewAvailable = newAvail
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                return Conflict("Dữ liệu tồn kho vừa thay đổi, vui lòng thử lại.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return StatusCode(500, new { message = "Lỗi xử lý thanh toán.", detail = ex.Message });
            }
        }
    }
}
