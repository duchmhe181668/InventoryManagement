using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SalesController : Controller
    {
        private readonly AppDbContext _context;
        public SalesController(AppDbContext context) => _context = context;

        public sealed class GoodListItemDto
        {
            public int GoodID { get; set; }
            public string Name { get; set; } = "";
            public string Unit { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal PriceSell { get; set; }
            public string? CategoryName { get; set; }
            public string? SupplierName { get; set; }
            public string? ImageURL { get; set; }
        }

        [HttpGet("{searchString}")]
        public async Task<ActionResult<Good>> GetGood(string searchString, CancellationToken ct)
        {
            try
            {
                int id = int.Parse(searchString);
                var good = await _context.Goods.AsNoTracking().Where(g => g.GoodID == id).ToListAsync(ct);
                if (good == null) return NotFound();
                else return Ok(good);
            } catch (Exception)
            {
                searchString = searchString.ToLower();
                var good = await _context.Goods.AsNoTracking().Where(g=>g.Name.ToLower().Contains(searchString)).ToListAsync(ct);
                if (good == null) return NotFound();
                else return Ok(good);
            }
        }


    }
}
