using BCrypt.Net;
using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Models.Auth;
using InventoryManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text;
using InventoryManagement.Models.Views;

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

            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == usernameInput.ToLower());

            if (user is null || user.PasswordHash is null || user.PasswordHash.Length == 0)
                return Unauthorized("Invalid username or password.");

            string hash = Encoding.UTF8.GetString(user.PasswordHash).Trim();
            if (!(hash.StartsWith("$2a$") || hash.StartsWith("$2b$") || hash.StartsWith("$2y$")))
            {
                hash = Encoding.Unicode.GetString(user.PasswordHash).Trim();
            }

            bool ok;
            try { ok = BCrypt.Net.BCrypt.Verify(passwordInput, hash); }
            catch { return BadRequest("PasswordHash is not a valid BCrypt string. Please update the user with a BCrypt hash."); }
            if (!ok) return Unauthorized("Invalid username or password.");

            var roleName = user.Role?.RoleName;
            var roles = string.IsNullOrWhiteSpace(roleName)
                ? Enumerable.Empty<string>()
                : new[] { roleName! };

            var token = _jwt.CreateToken(user.Username, user.Username, roles);
            var exp = _jwt.GetExpiry();

            int? supplierId = null;
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                var supplier = await _db.Suppliers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Email == user.Email);
                supplierId = supplier?.SupplierID;
            }

            int? storeId = await _db.Stores
                .AsNoTracking()
                .Where(s => s.UserID == user.UserID)
                .Select(s => (int?)s.StoreID)
                .FirstOrDefaultAsync();

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

        /// <summary>
        /// Đăng ký tài khoản mới và tự động phát JWT (auto-login).
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest req)
        {
            if (req == null) return BadRequest("Invalid payload.");

            var username = req.Username?.Trim();
            var password = req.Password;
            var name = req.Name?.Trim();
            var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email!.Trim();
            var phone = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber!.Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name))
                return BadRequest("Username, Password, Name are required.");
            if (password.Length < 6) return BadRequest("Password must be at least 6 characters.");

            var existed = await _db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower());
            if (existed) return Conflict("Username already exists.");

            var roleName = string.IsNullOrWhiteSpace(req.RoleName) ? "Supplier" : req.RoleName!.Trim();
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName);
            if (role == null)
                return BadRequest("Invalid role name.");

            var bcrypt = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
            var hashBytes = Encoding.UTF8.GetBytes(bcrypt);

            var user = new InventoryManagement.Models.User
            {
                Username = username,
                PasswordHash = hashBytes,
                Name = name,
                Email = email,
                PhoneNumber = phone,
                RoleID = role.RoleID
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            int? supplierId = null;
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                var supplier = await _db.Suppliers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Email == user.Email);
                supplierId = supplier?.SupplierID;
            }

            int? storeId = await _db.Stores
                .AsNoTracking()
                .Where(s => s.UserID == user.UserID)
                .Select(s => (int?)s.StoreID)
                .FirstOrDefaultAsync();

            var roles = new[] { role.RoleName };
            var token = _jwt.CreateToken(user.Username, user.Username, roles);
            var exp = _jwt.GetExpiry();

            return Ok(new LoginResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresInSeconds = (long)(exp - DateTimeOffset.UtcNow).TotalSeconds,
                Username = user.Username,
                Role = role.RoleName,
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

        /// <summary>
        /// Cập nhật thông tin người dùng
        /// </summary>
        [HttpPut("update/{userId}")]
        [Authorize(Roles = "Administrator")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult> UpdateUser(int userId, [FromBody] UpdateUserRequest req)
        {
            var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserID == userId);

            if (user == null)
                return NotFound("User not found.");

            user.Username = req.Username?.Trim() ?? user.Username;
            user.Name = req.Name?.Trim() ?? user.Name;
            user.Email = req.Email?.Trim() ?? user.Email;
            user.PhoneNumber = req.PhoneNumber?.Trim() ?? user.PhoneNumber;

            if (!string.IsNullOrWhiteSpace(req.RoleName))
            {
                var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == req.RoleName);
                if (role != null)
                    user.RoleID = role.RoleID;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Xóa người dùng
        /// </summary>
        [HttpDelete("delete/{userId}")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<ActionResult> DeleteUser(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found.");

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Lấy tất cả người dùng
        /// </summary>
        [HttpGet("users")]
        //[Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<ActionResult<IEnumerable<User>>> GetAllUsers()
        {
            var users = await _db.Users.Include(u => u.Role).ToListAsync();
            return Ok(users);
        }

        /// <summary>
        /// Lấy thông tin chi tiết người dùng
        /// </summary>
        [HttpGet("user/{userId}")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<ActionResult<User>> GetUserById(int userId)
        {
            var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
                return NotFound("User not found.");

            return Ok(user);
        }
    }
}
