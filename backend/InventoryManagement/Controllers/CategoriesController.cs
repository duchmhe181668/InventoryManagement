using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CategoriesController(AppDbContext context) => _context = context;

        // ===== DTO =====
        public sealed class CategoryDto
        {
            public int CategoryID { get; set; }
            public string CategoryName { get; set; } = string.Empty;
        }

        // ===== GET ALL =====
        // GET: /api/categories
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories(CancellationToken ct = default)
        {
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.CategoryName)
                .Select(c => new CategoryDto
                {
                    CategoryID = c.CategoryID,
                    CategoryName = c.CategoryName
                })
                .ToListAsync(ct);

            return Ok(categories);
        }

        // ===== GET BY ID =====
        // GET: /api/categories/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CategoryDto>> GetCategory(int id, CancellationToken ct = default)
        {
            var cat = await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CategoryID == id, ct);

            if (cat == null) return NotFound();

            return Ok(new CategoryDto
            {
                CategoryID = cat.CategoryID,
                CategoryName = cat.CategoryName
            });
        }

        // ===== CREATE =====
        [HttpPost]
        public async Task<ActionResult<Category>> CreateCategory([FromBody] CategoryDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.CategoryName))
                return BadRequest("CategoryName is required.");

            if (await _context.Categories.AnyAsync(c => c.CategoryName == dto.CategoryName, ct))
                return Conflict("Category already exists.");

            var entity = new Category { CategoryName = dto.CategoryName.Trim() };
            _context.Categories.Add(entity);
            await _context.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetCategory), new { id = entity.CategoryID }, entity);
        }

        // ===== UPDATE =====
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryDto dto, CancellationToken ct = default)
        {
            var entity = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryID == id, ct);
            if (entity == null) return NotFound();

            if (await _context.Categories.AnyAsync(c => c.CategoryID != id && c.CategoryName == dto.CategoryName, ct))
                return Conflict("Category already exists.");

            entity.CategoryName = dto.CategoryName.Trim();
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ===== DELETE =====
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct = default)
        {
            var entity = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryID == id, ct);
            if (entity == null) return NotFound();

            _context.Categories.Remove(entity);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
