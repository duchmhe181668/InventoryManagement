using InventoryManagement.Data;
using InventoryManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // => /api/customers
    public class CustomersController : ControllerBase
    {
        private readonly AppDbContext _db;
        public CustomersController(AppDbContext db) => _db = db;

        // DTOs
        public sealed class CustomerDto
        {
            public int CustomerID { get; set; }
            public string Name { get; set; } = "";
            public string? PhoneNumber { get; set; }
            public string? Email { get; set; }
        }

        public sealed class CreateCustomerDto
        {
            public string Name { get; set; } = "";
            public string PhoneNumber { get; set; } = "";
            public string? Email { get; set; }
        }

        // GET /api/customers/search?q=...
        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<CustomerDto>>> Search([FromQuery] string q, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(Array.Empty<CustomerDto>());

            var s = q.Trim();
            bool looksLikePhone = s.Any(char.IsDigit);

            var query = _db.Customers
                .AsNoTracking()
                .Where(c =>
                    EF.Functions.Like(c.Name, $"%{s}%") ||
                    (looksLikePhone && c.PhoneNumber != null && EF.Functions.Like(c.PhoneNumber, $"%{s}%"))
                )
                .OrderBy(c => c.Name)
                .Take(20)
                .Select(c => new CustomerDto
                {
                    CustomerID = c.CustomerID,
                    Name = c.Name,
                    PhoneNumber = c.PhoneNumber,
                    Email = c.Email
                });

            var list = await query.ToListAsync(ct);
            return Ok(list);
        }

        // POST /api/customers
        [HttpPost]
        public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.PhoneNumber))
                return BadRequest("Name và PhoneNumber là bắt buộc.");

            // Nếu muốn không trùng SĐT, kiểm tra trước:
            var existed = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.PhoneNumber == dto.PhoneNumber, ct);

            if (existed != null)
            {
                // Có thể trả 200 kèm bản ghi cũ để FE dùng luôn
                return Ok(new CustomerDto
                {
                    CustomerID = existed.CustomerID,
                    Name = existed.Name,
                    PhoneNumber = existed.PhoneNumber,
                    Email = existed.Email
                });
                // Hoặc trả 409 Conflict nếu muốn cứng rắn:
                // return Conflict("PhoneNumber already exists.");
            }

            var entity = new Customer
            {
                Name = dto.Name.Trim(),
                PhoneNumber = dto.PhoneNumber.Trim(),
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
            };
            _db.Customers.Add(entity);
            await _db.SaveChangesAsync(ct);

            var result = new CustomerDto
            {
                CustomerID = entity.CustomerID,
                Name = entity.Name,
                PhoneNumber = entity.PhoneNumber,
                Email = entity.Email
            };

            return CreatedAtAction(nameof(GetById), new { id = entity.CustomerID }, result);
        }

        // GET /api/customers/5  (tiện cho FE)
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CustomerDto>> GetById(int id, CancellationToken ct)
        {
            var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CustomerID == id, ct);
            if (c == null) return NotFound();
            return new CustomerDto
            {
                CustomerID = c.CustomerID,
                Name = c.Name,
                PhoneNumber = c.PhoneNumber,
                Email = c.Email
            };
        }
    }
}
