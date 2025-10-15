using System.Data;
using System.Data.Common;
using System.Text;
using InventoryManagement.Data;
using InventoryManagement.Models.Auth;
using InventoryManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IJwtService _jwt;

        public AuthController(AppDbContext db, IJwtService jwt)
        {
            _db = db;
            _jwt = jwt;
        }

        /// <summary>
        /// Đăng nhập, trả về JWT.
        /// Yêu cầu dbo.Users có cột [Username] và [PasswordHash] (BCrypt).
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
        {
            var usernameInput = req?.Username?.Trim();
            var passwordInput = req?.Password;

            if (string.IsNullOrWhiteSpace(usernameInput) || string.IsNullOrWhiteSpace(passwordInput))
                return BadRequest("Username/Password is required.");

            // 1) Lấy user + role (case-insensitive)
            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == usernameInput.ToLower());

            if (user is null || user.PasswordHash is null || user.PasswordHash.Length == 0)
                return Unauthorized("Invalid username or password.");

            // 2) Giải mã varbinary -> chuỗi bcrypt (UTF8, fallback Unicode)
            string hash = Encoding.UTF8.GetString(user.PasswordHash).Trim();
            if (!(hash.StartsWith("$2a$") || hash.StartsWith("$2b$") || hash.StartsWith("$2y$")))
            {
                hash = Encoding.Unicode.GetString(user.PasswordHash).Trim();
            }

            // 3) Verify BCrypt
            if (!(hash.StartsWith("$2a$") || hash.StartsWith("$2b$") || hash.StartsWith("$2y$")))
                return BadRequest("PasswordHash is not a valid BCrypt string. Please update the user with a BCrypt hash.");

            bool ok;
            try { ok = BCrypt.Net.BCrypt.Verify(passwordInput, hash); }
            catch { return BadRequest("PasswordHash is not a valid BCrypt string. Please update the user with a BCrypt hash."); }
            if (!ok) return Unauthorized("Invalid username or password.");

            // 4) Roles + token
            var roleName = user.Role?.RoleName;
            var roles = string.IsNullOrWhiteSpace(roleName)
                ? Enumerable.Empty<string>()
                : new[] { roleName! };

            // 5) SupplierId theo Email (nếu có)
            int? supplierId = null;
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                var supplier = await _db.Suppliers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Email == user.Email);
                supplierId = supplier?.SupplierID;
            }

            // 6) StoreId theo quan hệ 1-1: Stores.UserID = Users.UserID
            int? storeId = await _db.Stores
                .AsNoTracking()
                .Where(s => s.UserID == user.UserID)
                .Select(s => (int?)s.StoreID)
                .FirstOrDefaultAsync();

            // 7) Thêm extra claims và tạo token
            var extraClaims = new Dictionary<string, string>();
            if (supplierId.HasValue)
                extraClaims["supplier_id"] = supplierId.Value.ToString();
            if (storeId.HasValue)
                extraClaims["store_id"] = storeId.Value.ToString();


            var token = _jwt.CreateToken(user.Username, user.Username, roles, extraClaims);
            var exp = _jwt.GetExpiry();

            // Nếu muốn nhét store_id vào token, thêm overload IJwtService:
            // var extraClaims = new Dictionary<string,string>();
            // if (supplierId.HasValue) extraClaims["supplier_id"] = supplierId.Value.ToString();
            // if (storeId.HasValue)    extraClaims["store_id"]    = storeId.Value.ToString();
            // var token = _jwt.CreateToken(user.Username, user.Username, roles, extraClaims);

            return Ok(new LoginResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresInSeconds = (long)(exp - DateTimeOffset.UtcNow).TotalSeconds,
                Username = user.Username,
                Role = roleName,
                SupplierId = supplierId,
                StoreId = storeId
            });
        }

        /// <summary>Test token: trả lại danh tính & claims</summary>
        [HttpGet("me")]
        [Authorize]
        [Produces("application/json")]
        public ActionResult<object> Me()
        {
            return Ok(new
            {
                User = User.Identity?.Name,
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            });
        }

    }
}
