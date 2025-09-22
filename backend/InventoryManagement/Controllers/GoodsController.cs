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

        public GoodsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/goods
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Good>>> GetGoods()
        {
            return await _context.Goods
                .Include(g => g.Category)
                .Include(g => g.Store)
                .Include(g => g.Supplier)
                .ToListAsync();
        }

        // GET: api/goods/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Good>> GetGood(int id)
        {
            var good = await _context.Goods
                .Include(g => g.Category)
                .Include(g => g.Store)
                .Include(g => g.Supplier)
                .FirstOrDefaultAsync(g => g.GoodID == id);

            if (good == null) return NotFound();
            return good;
        }

        // POST: api/goods
        [HttpPost]
        public async Task<ActionResult<Good>> CreateGood(Good good)
        {
            _context.Goods.Add(good);
            await _context.SaveChangesAsync();

            // load lại để include các quan hệ
            var created = await _context.Goods
                .Include(g => g.Category)
                .Include(g => g.Store)
                .Include(g => g.Supplier)
                .FirstOrDefaultAsync(g => g.GoodID == good.GoodID);

            return CreatedAtAction(nameof(GetGood), new { id = good.GoodID }, created);
        }

        // PUT: api/goods/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateGood(int id, Good good)
        {
            if (id != good.GoodID) return BadRequest();

            _context.Entry(good).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Goods.Any(e => e.GoodID == id))
                    return NotFound();
                else
                    throw;
            }

            return NoContent();
        }

        // DELETE: api/goods/5
        [HttpDelete("{id}")]
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
