using InventoryManagement.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text; 
namespace InventoryManagement.Controllers
{
    //[Authorize]                     
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly AppDbContext _db;
        public ReportController(AppDbContext db) => _db = db;


        private async Task<int?> ResolveLocationIdAsync(int storeId, CancellationToken ct)
            => await _db.Stores.AsNoTracking()
                .Where(s => s.StoreID == storeId)
                .Select(s => (int?)s.LocationID)
                .FirstOrDefaultAsync(ct);

 
        private sealed record PriceSnap(int GoodID, decimal Sell, decimal Cost);
        private async Task<Dictionary<int, PriceSnap>> GetPriceSnapshotAsync(
            int storeId, IEnumerable<int> goodIds, CancellationToken ct)
        {
            var ids = goodIds.Distinct().ToList();

            var latest = await _db.StorePrices.AsNoTracking()
                .Where(sp => sp.StoreID == storeId && ids.Contains(sp.GoodID))
                .GroupBy(sp => sp.GoodID)
                .Select(g => g.OrderByDescending(x => x.EffectiveFrom)
                              .Select(x => new { x.GoodID, x.PriceSell })
                              .FirstOrDefault()!)
                .ToListAsync(ct);

            var map = new Dictionary<int, PriceSnap>();
            foreach (var p in latest.Where(x => x != null))
            {
                var sell = p.PriceSell;
                var cost = sell > 0 ? Math.Round(sell * 0.8m, 2) : 0m;
                map[p.GoodID] = new PriceSnap(p.GoodID, sell, cost);
            }

            var missing = ids.Where(id => !map.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                var goods = await _db.Goods.AsNoTracking()
                    .Where(g => missing.Contains(g.GoodID))
                    .Select(g => new { g.GoodID, g.PriceSell })
                    .ToListAsync(ct);

                foreach (var g in goods)
                {
                    var sell = g.PriceSell;
                    var cost = sell > 0 ? Math.Round(sell * 0.8m, 2) : 0m;
                    map[g.GoodID] = new PriceSnap(g.GoodID, sell, cost);
                }
            }
            return map;
        }

        private IQueryable<Models.Sale> InvoicesQuery(int locationId, DateTime from, DateTime to)
            => _db.Sales.AsNoTracking()
                .Where(s => s.StoreLocationID == locationId
                         && s.Status == "Completed"
                         && s.CreatedAt >= from
                         && s.CreatedAt < to);

        public sealed class SalesRow { public DateTime Date { get; set; } public int InvoiceCount { get; set; } public decimal Revenue { get; set; } public decimal AOV { get; set; } }
        public sealed class MarginRow { public int GoodID { get; set; } public string Name { get; set; } = ""; public string Unit { get; set; } = ""; public decimal Qty { get; set; } public decimal Revenue { get; set; } public decimal Cost { get; set; } public decimal Margin { get; set; } public decimal MarginPct { get; set; } }
        public sealed class StockRow { public int GoodID { get; set; } public string Name { get; set; } = ""; public string Unit { get; set; } = ""; public decimal Qty { get; set; } public decimal CostPrice { get; set; } public decimal SellPrice { get; set; } public decimal StockValueCost { get; set; } public decimal StockValueSell { get; set; } }

 
        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] string type,
            [FromQuery] int storeId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct = default)
        {
            if (storeId <= 0) return BadRequest("storeId is required.");
            if (string.IsNullOrWhiteSpace(type)) return BadRequest("type is required.");

            var locId = await ResolveLocationIdAsync(storeId, ct);
            if (!locId.HasValue) return NotFound("Store not found or no LocationID.");

            var f = (from ?? DateTime.Today.AddDays(1 - DateTime.Today.Day)).Date;
            var t = (to ?? DateTime.Today.AddDays(1)).Date;

            switch (type.Trim().ToLowerInvariant())
            {
                case "sales":
                    {
                        var revPerDay = await (
                            from s in InvoicesQuery(locId.Value, f, t)
                            join l in _db.SaleLines.AsNoTracking() on s.SaleID equals l.SaleID
                            select new { Day = s.CreatedAt.Date, Amount = l.UnitPrice * l.Quantity }
                        )
                        .GroupBy(x => x.Day)
                        .Select(g => new { Day = g.Key, Revenue = g.Sum(x => x.Amount) })
                        .ToListAsync(ct);

                        var cntPerDay = await InvoicesQuery(locId.Value, f, t)
                            .GroupBy(s => s.CreatedAt.Date)
                            .Select(g => new { Day = g.Key, Count = g.Count() })
                            .ToListAsync(ct);

                        var mapRev = revPerDay.ToDictionary(x => x.Day, x => x.Revenue);
                        var mapCnt = cntPerDay.ToDictionary(x => x.Day, x => x.Count);

                        var days = mapRev.Keys.Union(mapCnt.Keys).Distinct().OrderBy(d => d);
                        var items = new List<SalesRow>();
                        int totalCount = 0; decimal totalRev = 0;

                        foreach (var d in days)
                        {
                            var rev = mapRev.TryGetValue(d, out var r) ? r : 0m;
                            var cnt = mapCnt.TryGetValue(d, out var c) ? c : 0;
                            items.Add(new SalesRow
                            {
                                Date = d,
                                InvoiceCount = cnt,
                                Revenue = Math.Round(rev, 2),
                                AOV = cnt > 0 ? Math.Round(rev / cnt, 2) : 0
                            });
                            totalCount += cnt; totalRev += rev;
                        }

                        return Ok(new
                        {
                            items,
                            summary = new
                            {
                                storeId,
                                from = f,
                                to = t,
                                totalInvoices = totalCount,
                                totalRevenue = Math.Round(totalRev, 2),
                                aov = totalCount > 0 ? Math.Round(totalRev / totalCount, 2) : 0
                            }
                        });
                    }

                case "margin":
                    {
                        var lineAgg = await (
                            from s in InvoicesQuery(locId.Value, f, t)
                            join l in _db.SaleLines.AsNoTracking() on s.SaleID equals l.SaleID
                            group l by l.GoodID into g
                            select new
                            {
                                GoodID = g.Key,
                                Qty = g.Sum(x => x.Quantity),
                                Revenue = g.Sum(x => x.Quantity * x.UnitPrice)
                            })
                            .ToListAsync(ct);

                        if (lineAgg.Count == 0)
                            return Ok(new { items = Array.Empty<MarginRow>(), summary = new { storeId, from = f, to = t, totalQty = 0, totalRevenue = 0, totalCost = 0, totalMargin = 0, marginPct = 0 } });

                        var goodIds = lineAgg.Select(x => x.GoodID).ToList();
                        var goodsMeta = await _db.Goods.AsNoTracking()
                            .Where(g => goodIds.Contains(g.GoodID))
                            .Select(g => new { g.GoodID, g.Name, g.Unit })
                            .ToDictionaryAsync(x => x.GoodID, x => x, ct);
                        var snap = await GetPriceSnapshotAsync(storeId, goodIds, ct);

                        var items = new List<MarginRow>();
                        decimal sumQty = 0, sumRev = 0, sumCost = 0;

                        foreach (var a in lineAgg.OrderByDescending(x => x.Revenue))
                        {
                            var meta = goodsMeta.TryGetValue(a.GoodID, out var m)
                                ? m : new { GoodID = a.GoodID, Name = $"#{a.GoodID}", Unit = "" };
                            snap.TryGetValue(a.GoodID, out var sp);

                            var unitCost = sp?.Cost ?? 0m;
                            var costVal = Math.Round(unitCost * a.Qty, 2);
                            var revVal = Math.Round(a.Revenue, 2);
                            var margin = Math.Round(revVal - costVal, 2);
                            var mPct = revVal > 0 ? Math.Round(margin * 100 / revVal, 2) : 0;

                            items.Add(new MarginRow
                            {
                                GoodID = a.GoodID,
                                Name = meta.Name,
                                Unit = meta.Unit ?? "",
                                Qty = Math.Round(a.Qty, 2),
                                Revenue = revVal,
                                Cost = costVal,
                                Margin = margin,
                                MarginPct = mPct
                            });

                            sumQty += a.Qty; sumRev += revVal; sumCost += costVal;
                        }

                        var totalMargin = Math.Round(sumRev - sumCost, 2);
                        var totalPct = sumRev > 0 ? Math.Round(totalMargin * 100 / sumRev, 2) : 0;

                        return Ok(new
                        {
                            items,
                            summary = new
                            {
                                storeId,
                                from = f,
                                to = t,
                                totalQty = Math.Round(sumQty, 2),
                                totalRevenue = Math.Round(sumRev, 2),
                                totalCost = Math.Round(sumCost, 2),
                                totalMargin,
                                marginPct = totalPct
                            }
                        });
                    }

                case "stock":
                    {
                        var stockAgg = await _db.Stocks.AsNoTracking()
                            .Where(s => s.LocationID == locId.Value)
                            .GroupBy(s => s.GoodID)
                            .Select(g => new { GoodID = g.Key, Qty = g.Sum(x => x.OnHand - x.Reserved) })
                            .ToListAsync(ct);

                        var ids = stockAgg.Select(x => x.GoodID).ToList();
                        var goodsMeta = await _db.Goods.AsNoTracking()
                            .Where(g => ids.Contains(g.GoodID))
                            .Select(g => new { g.GoodID, g.Name, g.Unit, g.PriceSell })
                            .ToDictionaryAsync(x => x.GoodID, x => x, ct);
                        var snap = await GetPriceSnapshotAsync(storeId, ids, ct);

                        var items = new List<StockRow>();
                        decimal sumQty = 0, sumValCost = 0, sumValSell = 0;

                        foreach (var s in stockAgg.OrderByDescending(x => x.Qty))
                        {
                            var meta = goodsMeta.TryGetValue(s.GoodID, out var m)
                                ? m : new { GoodID = s.GoodID, Name = $"#{s.GoodID}", Unit = "", PriceSell = 0m };
                            snap.TryGetValue(s.GoodID, out var sp);

                            var unitSell = sp?.Sell ?? meta.PriceSell;
                            var unitCost = sp?.Cost ?? (unitSell > 0 ? Math.Round(unitSell * 0.8m, 2) : 0m);

                            var costVal = Math.Round(unitCost * s.Qty, 2);
                            var sellVal = Math.Round(unitSell * s.Qty, 2);

                            items.Add(new StockRow
                            {
                                GoodID = s.GoodID,
                                Name = meta.Name,
                                Unit = meta.Unit ?? "",
                                Qty = Math.Round(s.Qty, 2),
                                CostPrice = unitCost,
                                SellPrice = unitSell,
                                StockValueCost = costVal,
                                StockValueSell = sellVal
                            });

                            sumQty += s.Qty; sumValCost += costVal; sumValSell += sellVal;
                        }

                        return Ok(new
                        {
                            items,
                            summary = new
                            {
                                storeId,
                                locationId = locId.Value,
                                totalQty = Math.Round(sumQty, 2),
                                totalStockValueCost = Math.Round(sumValCost, 2),
                                totalStockValueSell = Math.Round(sumValSell, 2)
                            }
                        });
                    }

                default:
                    return BadRequest("type must be one of: sales | margin | stock");
            }
        }

        public sealed class StoreListItem
        {
            public int StoreId { get; set; }
            public string? Address { get; set; }
            public int LocationId { get; set; }
        }

        [HttpGet("stores")]
        public async Task<ActionResult<object>> GetStores(
            [FromQuery] string? q,
            CancellationToken ct = default)
        {
            var query = _db.Stores.AsNoTracking()
                .Select(s => new
                {
                    s.StoreID,
                    s.Address,       
                    s.LocationID
                });

            if (!string.IsNullOrWhiteSpace(q))
            {
                var key = q.Trim();
                query = query.Where(s =>
                    (s.Address != null && EF.Functions.Like(s.Address, $"%{key}%")) ||
                    s.StoreID.ToString().Contains(key));
            }

            var data = await query
                .OrderBy(s => s.Address) 
                .ToListAsync(ct);

            return Ok(new
            {
                items = data.Select(s => new
                {
                    storeId = s.StoreID,
                    address = s.Address,      
                    locationId = s.LocationID
                })
            });
        }
    }
}
