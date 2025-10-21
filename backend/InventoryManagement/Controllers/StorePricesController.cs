using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Data;
using InventoryManagement.Models;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StorePricesController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StorePricesController(AppDbContext db) { _db = db; }

        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent([FromQuery] int storeId, [FromQuery] int goodId)
        {
            var today = DateTime.Today;

            var sp = await _db.StorePrices.AsNoTracking()
                .Where(p => p.StoreID == storeId && p.GoodID == goodId && p.EffectiveFrom <= today)
                .OrderByDescending(p => p.EffectiveFrom)
                .Select(p => (decimal?)p.PriceSell)
                .FirstOrDefaultAsync();

            if (sp != null) return Ok(new { storeId, goodId, priceSell = sp.Value, source = "StorePrice" });

            var g = await _db.Goods.AsNoTracking()
                .Where(x => x.GoodID == goodId)
                .Select(x => (decimal?)x.PriceSell)
                .FirstOrDefaultAsync();

            if (g == null) return NotFound(new { message = "Good not found" });
            return Ok(new { storeId, goodId, priceSell = g.Value, source = "Goods" });
        }

        [HttpPut("current")]
        public async Task<IActionResult> UpsertCurrent([FromBody] UpsertCurrentPriceDto dto)
        {
            if (dto == null || dto.StoreId <= 0 || dto.GoodId <= 0)
                return BadRequest(new { message = "Invalid payload" });

            var effective = (dto.EffectiveFrom?.Date) ?? DateTime.Today;

            var existing = await _db.StorePrices.FirstOrDefaultAsync(p =>
                p.StoreID == dto.StoreId &&
                p.GoodID == dto.GoodId &&
                p.EffectiveFrom == effective
            );

            decimal? oldPrice = null;
            if (existing == null)
            {
                _db.StorePrices.Add(new StorePrice
                {
                    StoreID = dto.StoreId,
                    GoodID = dto.GoodId,
                    EffectiveFrom = effective,
                    PriceSell = dto.PriceSell
                });
            }
            else
            {
                oldPrice = existing.PriceSell;
                existing.PriceSell = dto.PriceSell;
                _db.StorePrices.Update(existing);
            }

            

            await _db.SaveChangesAsync();
            return Ok(new { message = "Saved", dto.StoreId, dto.GoodId, dto.PriceSell, EffectiveFrom = effective.ToString("yyyy-MM-dd") });
        }

        public class UpsertCurrentPriceDto
        {
            public int StoreId { get; set; }
            public int GoodId { get; set; }
            public decimal PriceSell { get; set; }
            public DateTime? EffectiveFrom { get; set; }
        }
    }
}
