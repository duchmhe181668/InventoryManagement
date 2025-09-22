using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GoodController : ControllerBase
    {
        private readonly AppDbContext _context;

        public GoodController(AppDbContext context)
        {
            _context = context;
        }

        // DTO cho POST (không có GoodID, bắt buộc StoreID)
        public class CreateGoodDto
        {
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public DateTime? DateIn { get; set; }
            public string? ImageURL { get; set; }
            public decimal Quantity { get; set; }
            public decimal PriceCost { get; set; }
            public decimal PriceSell { get; set; }
            public int StoreID { get; set; }            // BẮT BUỘC
            public string? CategoryID { get; set; }     // NVARCHAR(200) theo DB hiện tại
            public int? SupplierID { get; set; }
        }

        // GET: api/goods
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Good>>> GetGoods()
        {
            // Nếu bạn CHƯA có nav props, bỏ 3 dòng Include để build
            return await _context.Goods
                // .Include(g => g.Category)
                // .Include(g => g.Store)
                // .Include(g => g.Supplier)
                .AsNoTracking()
                .ToListAsync();
        }

        // GET: api/goods/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Good>> GetGood(int id)
        {
            var good = await _context.Goods
                // .Include(g => g.Category)
                // .Include(g => g.Store)
                // .Include(g => g.Supplier)
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.GoodID == id);

            if (good == null) return NotFound();
            return good;
        }

        // POST: api/goods
        [HttpPost]
        public async Task<ActionResult<Good>> CreateGood([FromBody] CreateGoodDto dto)
        {
            // Tối thiểu: validate thủ công 2 field bắt buộc
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Unit)) return BadRequest("Unit is required.");
            if (dto.StoreID <= 0) return BadRequest("storeID is required and must be > 0.");

            var entity = new Good
            {
                // KHÔNG set GoodID (IDENTITY)
                Name = dto.Name,
                Unit = dto.Unit,
                DateIn = dto.DateIn?.Date, // cột DB của bạn là DATE -> cắt time
                ImageURL = dto.ImageURL,
                Quantity = dto.Quantity,
                PriceCost = dto.PriceCost,
                PriceSell = dto.PriceSell,
                StoreID = dto.StoreID,
                CategoryID = dto.CategoryID,
                SupplierID = dto.SupplierID
            };

            _context.Goods.Add(entity);
            await _context.SaveChangesAsync();

            // Nếu có nav: load lại; nếu không, trả luôn entity là đủ
            // var created = await _context.Goods
            //     .Include(g => g.Category)
            //     .Include(g => g.Store)
            //     .Include(g => g.Supplier)
            //     .FirstOrDefaultAsync(g => g.GoodID == entity.GoodID);

            // return CreatedAtAction(nameof(GetGood), new { id = entity.GoodID }, created);
            return CreatedAtAction(nameof(GetGood), new { id = entity.GoodID }, entity);
        }

        // PUT: api/goods/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateGood(int id, [FromBody] Good good)
        {
            if (id != good.GoodID) return BadRequest("ID mismatch.");

            // Đảm bảo không bị update GoodID
            _context.Entry(good).Property(x => x.GoodID).IsModified = false;
            _context.Entry(good).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Goods.AnyAsync(e => e.GoodID == id))
                    return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/goods/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteGood(int id)
        {
            var good = await _context.Goods.FindAsync(id);
            if (good == null) return NotFound();

            _context.Goods.Remove(good);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
