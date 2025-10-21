
using InventoryManagement.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InventoryManagement.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    public class SalesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public SalesController(AppDbContext context) => _context = context;

        private int? GetStoreIdFromClaims()
        {
            var keys = new[] { "storeId", "store_id", "sid", "store", "StoreId", "StoreID" };
            foreach (var k in keys)
            {
                var v = User?.Claims?.FirstOrDefault(c => c.Type == k)?.Value;
                if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var id)) return id;
            }
            return null;
        }

        private async Task<int?> ResolveLocationIdByStoreAsync(int storeId, CancellationToken ct)
        {
            return await _context.Stores
                .AsNoTracking()
                .Where(st => st.StoreID == storeId)
                .Select(st => (int?)st.LocationID)
                .FirstOrDefaultAsync(ct);
        }

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
            var locId = await ResolveLocationIdByStoreAsync(storeId, ct);
            if (!locId.HasValue) return NotFound("Store has no LocationID configured.");
            return locId.Value;
        }

        public sealed class SellDto
        {
            public int GoodID { get; set; }
            public decimal QuantitySold { get; set; }
        }

        public sealed class CheckoutDto
        {
            public int LocationId { get; set; }
            public List<SellDto> Items { get; set; } = new();
        }

        private static decimal AvailableOf(InventoryManagement.Models.Stock s)
            => s.OnHand - s.Reserved;

        [HttpPost("sell")]
        public async Task<IActionResult> SellOne(
            [FromBody] SellDto dto,
            [FromQuery] int locationId,
            CancellationToken ct)
        {
            if (dto == null || dto.GoodID <= 0 || dto.QuantitySold <= 0)
                return BadRequest("Invalid payload.");

            var stock = await _context.Stocks
                .FirstOrDefaultAsync(s => s.GoodID == dto.GoodID && s.LocationID == locationId, ct);

            if (stock == null)
                return NotFound($"Stock not found for GoodID={dto.GoodID} at LocationID={locationId}.");

            var available = AvailableOf(stock);
            if (available < dto.QuantitySold)
                return BadRequest($"Insufficient stock for GoodID={dto.GoodID}. Available={available}, required={dto.QuantitySold}.");

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

        public sealed class CheckoutItemDto
        {
            public int GoodID { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal DiscountPercent { get; set; } = 0;
            public int? BatchID { get; set; }
        }

        public sealed class CheckoutRequest
        {
            public int LocationId { get; set; }
            public int? CustomerId { get; set; }

            public string PaymentMethod { get; set; } = "CASH";
            public string? BankName { get; set; }
            public string? BankAccount { get; set; }
            public string? BankRef { get; set; }

            public string? Email { get; set; }
            public List<CheckoutItemDto> Items { get; set; } = new();
        }

        public sealed class CheckoutResponse
        {
            public int SaleID { get; set; }
            public DateTime CreatedAt { get; set; }
            public decimal TotalGross { get; set; }
            public decimal TotalDiscount { get; set; }
            public decimal TotalFinal { get; set; }
            public IEnumerable<object> Lines { get; set; } = Array.Empty<object>();
        }

        [HttpPost("checkout")]
        public async Task<ActionResult<CheckoutResponse>> Checkout([FromBody] CheckoutRequest req, CancellationToken ct)
        {
            if (req == null || req.LocationId <= 0 || req.Items == null || req.Items.Count == 0)
                return BadRequest("locationId và items là bắt buộc.");

            // Chuẩn hóa input
            var items = req.Items
                .Where(i => i.GoodID > 0 && i.Quantity > 0 && i.UnitPrice >= 0)
                .ToList();
            if (items.Count == 0) return BadRequest("Không có dòng hàng hợp lệ.");

            decimal R2(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

            // Kiểm tra location
            var locExists = await _context.Locations.AsNoTracking()
                .AnyAsync(l => l.LocationID == req.LocationId, ct);
            if (!locExists) return BadRequest("LocationId không hợp lệ.");

            var grouped = items.GroupBy(i => i.GoodID).Select(g => new {
                GoodID = g.Key,
                TotalQty = g.Sum(x => x.Quantity),
                Lines = g.ToList()
            }).ToList();

            var goodIds = grouped.Select(g => g.GoodID).ToList();

            var stockRows = await _context.Stocks
                .Where(s => s.LocationID == req.LocationId && goodIds.Contains(s.GoodID))
                .ToListAsync(ct);

            var batchInfo = await _context.Batches
                .AsNoTracking()
                .Where(b => goodIds.Contains(b.GoodID))
                .ToListAsync(ct);
            var batchById = batchInfo.ToDictionary(b => b.BatchID, b => b);

            decimal Avail(InventoryManagement.Models.Stock s) => s.OnHand - s.Reserved;

            foreach (var g in grouped)
            {
                if (g.Lines.Any(x => x.BatchID.HasValue))
                {
                    var needPerBatch = g.Lines
                        .Where(x => x.BatchID.HasValue)
                        .GroupBy(x => x.BatchID!.Value)
                        .Select(gg => new { BatchID = gg.Key, Qty = gg.Sum(x => x.Quantity) });

                    foreach (var nb in needPerBatch)
                    {
                        var row = stockRows.FirstOrDefault(s =>
                            s.GoodID == g.GoodID &&
                            s.LocationID == req.LocationId &&
                            s.BatchID == nb.BatchID);
                        var avail = row != null ? Avail(row) : 0m;
                        if (avail < nb.Qty)
                            return BadRequest($"Không đủ tồn cho hàng {g.GoodID} (batch {nb.BatchID}). Cần {nb.Qty}, còn {avail}.");
                    }

                    var noBatchQty = g.Lines.Where(x => !x.BatchID.HasValue).Sum(x => x.Quantity);
                    if (noBatchQty > 0)
                    {
                        var rows = stockRows.Where(s => s.GoodID == g.GoodID && s.LocationID == req.LocationId);
                        var totalAvail = rows.Sum(Avail);
                        if (totalAvail < g.TotalQty)
                            return BadRequest($"Không đủ tồn cho hàng {g.GoodID}. Cần {g.TotalQty}, còn {totalAvail}.");
                    }
                }
                else
                {
                    var rows = stockRows.Where(s => s.GoodID == g.GoodID && s.LocationID == req.LocationId);
                    var totalAvail = rows.Sum(Avail);
                    if (totalAvail < g.TotalQty)
                        return BadRequest($"Không đủ tồn cho hàng {g.GoodID}. Cần {g.TotalQty}, còn {totalAvail}.");
                }
            }

            await using var trx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                var now = DateTime.UtcNow;

                // 1) Tạo Sales
                var sale = new InventoryManagement.Models.Sale
                {
                    StoreLocationID = req.LocationId,
                    CustomerID = req.CustomerId,
                    CreatedBy = await GetCurrentUserIdOrDefaultAsync(ct),
                    CreatedAt = now,
                    Discount = 0m,
                    Status = "Completed"
                };
                _context.Sales.Add(sale);
                await _context.SaveChangesAsync(ct);

                var savedLines = new List<InventoryManagement.Models.SaleLine>();
                var movements = new List<InventoryManagement.Models.StockMovement>();

                foreach (var g in grouped)
                {
                    var candidates = await _context.Stocks
                        .Where(s => s.LocationID == req.LocationId && s.GoodID == g.GoodID)
                        .ToListAsync(ct);

                    var ordered = candidates
                        .OrderBy(s => {
                            var b = batchById.TryGetValue(s.BatchID, out var bi) ? bi : null;
                            return b?.ExpiryDate ?? DateTime.MaxValue;
                        })
                        .ThenByDescending(s => s.OnHand)
                        .ToList();

                    foreach (var line in g.Lines)
                    {
                        var qtyToAlloc = line.Quantity;
                        var unitPrice = R2(line.UnitPrice);

                        var allocSources = line.BatchID.HasValue
                            ? ordered.Where(s => s.BatchID == line.BatchID.Value).ToList()
                            : ordered;

                        var saleLine = new InventoryManagement.Models.SaleLine
                        {
                            SaleID = sale.SaleID,
                            GoodID = g.GoodID,
                            Quantity = 0m,
                            UnitPrice = unitPrice,
                            BatchID = null
                        };
                        _context.SaleLines.Add(saleLine);
                        await _context.SaveChangesAsync(ct);

                        foreach (var stock in allocSources)
                        {
                            if (qtyToAlloc <= 0) break;

                            var fresh = await _context.Stocks.FirstOrDefaultAsync(s =>
                                s.LocationID == stock.LocationID &&
                                s.GoodID == stock.GoodID &&
                                s.BatchID == stock.BatchID, ct);

                            if (fresh == null)
                                throw new InvalidOperationException("Mất đồng bộ tồn kho.");

                            var available = Avail(fresh);
                            if (available <= 0) continue;

                            var take = Math.Min(available, qtyToAlloc);

                            fresh.OnHand = R2(fresh.OnHand - take);
                            _context.Stocks.Update(fresh);

                            movements.Add(new InventoryManagement.Models.StockMovement
                            {
                                CreatedAt = now,
                                GoodID = g.GoodID,
                                Quantity = R2(take),
                                FromLocationID = req.LocationId,
                                ToLocationID = null,
                                BatchID = fresh.BatchID,
                                UnitCost = null,
                                MovementType = "SALE",
                                RefTable = "Sales",
                                RefID = sale.SaleID,
                                Note = $"Checkout - line {saleLine.SaleLineID}"
                            });

                            saleLine.Quantity = R2(saleLine.Quantity + take);
                            qtyToAlloc = R2(qtyToAlloc - take);
                        }

                        if (qtyToAlloc > 0)
                            throw new InvalidOperationException($"Không đủ tồn để trừ cho hàng {g.GoodID}.");

                        savedLines.Add(saleLine);
                    }
                }

                if (movements.Count > 0)
                    _context.StockMovements.AddRange(movements);

                await _context.SaveChangesAsync(ct);

                // 3) Tổng tiền (gross=final vì đơn giá đã sau CK)
                var totalGross = savedLines.Sum(l => l.UnitPrice * l.Quantity);
                var totalDiscount = 0m;
                var totalFinal = totalGross - totalDiscount;

                await trx.CommitAsync(ct);

                decimal R2Out(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

                return Ok(new CheckoutResponse
                {
                    SaleID = sale.SaleID,
                    CreatedAt = sale.CreatedAt,
                    TotalGross = R2Out(totalGross),
                    TotalDiscount = R2Out(totalDiscount),
                    TotalFinal = R2Out(totalFinal),
                    Lines = savedLines.Select(l => new {
                        l.SaleLineID,
                        l.GoodID,
                        l.Quantity,
                        l.UnitPrice,
                        Amount = R2Out(l.UnitPrice * l.Quantity)
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(ct);
                return Problem(title: "Checkout failed", detail: ex.Message);
            }
        }

        private async Task<int> GetCurrentUserIdOrDefaultAsync(CancellationToken ct)
        {
            var uname = User?.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(uname))
            {
                var u = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Username == uname, ct);
                if (u != null) return u.UserID;
            }
            var admin = await _context.Users.AsNoTracking()
                .OrderBy(x => x.UserID).FirstOrDefaultAsync(ct);
            return admin?.UserID ?? 1;
        }

        // ================== VIEW 1 INVOICE ==================
        [HttpGet("invoice/{id:int}")]
        public async Task<ActionResult<object>> GetInvoice(
            int id,
            [FromQuery] int storeId,
            CancellationToken ct)
        {
            // Lấy sale trước
            var sale = await _context.Sales
                .AsNoTracking()
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.SaleID == id, ct);

            if (sale == null) return NotFound();

            // Map storeId -> locationId
            var locId = await _context.Stores
                .AsNoTracking()
                .Where(st => st.StoreID == storeId)
                .Select(st => (int?)st.LocationID)
                .FirstOrDefaultAsync(ct);

            if (!locId.HasValue)
                return NotFound("Store not found or has no LocationID.");


            if (sale.StoreLocationID != locId.Value)
                return NotFound();

            // Lấy line
            var lines = await _context.SaleLines
                .AsNoTracking()
                .Where(l => l.SaleID == id)
                .Join(_context.Goods.AsNoTracking(),
                      l => l.GoodID,
                      g => g.GoodID,
                      (l, g) => new {
                          l.SaleLineID,
                          l.GoodID,
                          GoodName = g.Name,
                          g.SKU,
                          g.Unit,
                          l.Quantity,
                          l.UnitPrice,
                          Amount = l.UnitPrice * l.Quantity
                      })
                .ToListAsync(ct);

            var totalGross = lines.Sum(x => x.Amount);
            var totalDiscount = 0m;
            var totalFinal = totalGross - totalDiscount;

            return Ok(new
            {
                sale.SaleID,
                sale.CreatedAt,
                sale.Status,
                LocationID = sale.StoreLocationID,
                Customer = sale.Customer == null ? null : new
                {
                    sale.Customer.CustomerID,
                    sale.Customer.Name,
                    sale.Customer.PhoneNumber,
                    sale.Customer.Email
                },
                Totals = new
                {
                    TotalGross = Math.Round(totalGross, 2),
                    TotalDiscount = Math.Round(totalDiscount, 2),
                    TotalFinal = Math.Round(totalFinal, 2)
                },
                Lines = lines
            });
        }

        [HttpGet("invoices")]
        public async Task<ActionResult<object>> ListInvoices(
            [FromQuery] int storeId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] int? customerId = null,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            CancellationToken ct = default)
        {
            if (storeId <= 0) return BadRequest("storeId is required.");

            // Map storeId -> locationId
            var locId = await _context.Stores
                .AsNoTracking()
                .Where(st => st.StoreID == storeId)
                .Select(st => (int?)st.LocationID)
                .FirstOrDefaultAsync(ct);

            if (!locId.HasValue)
                return NotFound("Store not found or has no LocationID.");

            page = page < 1 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : (pageSize > 100 ? 100 : pageSize);

            var q = _context.Sales
                .AsNoTracking()
                .Where(s => s.StoreLocationID == locId.Value);

            if (customerId.HasValue) q = q.Where(s => s.CustomerID == customerId.Value);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(s => s.Status == status);


            var fromTime = fromUtc ?? from;
            var toTime = toUtc ?? to;

            if (fromTime.HasValue) q = q.Where(s => s.CreatedAt >= fromTime.Value);
            if (toTime.HasValue) q = q.Where(s => s.CreatedAt < toTime.Value);

            var total = await q.CountAsync(ct);

            var data = await q
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new {
                    s.SaleID,
                    s.CreatedAt,
                    s.Status,
                    s.StoreLocationID,
                    s.CustomerID
                })
                .ToListAsync(ct);

            return Ok(new
            {
                items = data,
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }


    }
}
