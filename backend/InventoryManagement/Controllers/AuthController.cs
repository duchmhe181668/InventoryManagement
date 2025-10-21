﻿using BCrypt.Net;
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
using System.Security.Cryptography;
using System.Net;
using System.Net.Mail;
using System.Collections.Concurrent;

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

        /// Đăng nhập, trả về JWT.
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

            // 5) SupplierId theo Email (nếu có)
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

            // 7) Thêm extra claims và tạo token
            var extraClaims = new Dictionary<string, string>();
            if (supplierId.HasValue)
                extraClaims["supplier_id"] = supplierId.Value.ToString();
            if (storeId.HasValue)
                extraClaims["store_id"] = storeId.Value.ToString();

<<<<<<< HEAD
=======

            var token = _jwt.CreateToken(user.Username, user.Username, roles, extraClaims);
            var exp = _jwt.GetExpiry();

            // Nếu muốn nhét store_id vào token, thêm overload IJwtService:
            // var extraClaims = new Dictionary<string,string>();
            // if (supplierId.HasValue) extraClaims["supplier_id"] = supplierId.Value.ToString();
            // if (storeId.HasValue)    extraClaims["store_id"]    = storeId.Value.ToString();
            // var token = _jwt.CreateToken(user.Username, user.Username, roles, extraClaims);

>>>>>>> 321ff6ba44c688d83fcc95b43fca3d6df0f45b93
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

        /// Đăng ký tài khoản mới và tự động phát JWT (auto-login).
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

        /// Test token: trả lại danh tính & claims
        [HttpGet("me")]
        [AllowAnonymous]
        [Produces("application/json")]
        public ActionResult<object> Me()
        {
            return Ok(new
            {
                User = User.Identity?.Name,
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            });
        }

<<<<<<< HEAD
        /// Cập nhật thông tin người dùng
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

        /// Xóa người dùng
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

        /// Lấy tất cả người dùng
        [HttpGet("users")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<ActionResult<IEnumerable<object>>> GetAllUsers()
        {

            var users = await _db.Users
             .Include(u => u.Role)
             .Select(u => new
             {
                 u.UserID,
                 u.Username,
                 u.Name,
                 u.Email,
                 u.PhoneNumber,
                 RoleName = u.Role != null ? u.Role.RoleName : null
             })
             .ToListAsync();

            return Ok(users);
        }

        /// Lấy thông tin chi tiết người dùng
        [HttpGet("user/{userId}")]
        [Authorize(Roles = "Administrator")]
        [Produces("application/json")]
        public async Task<ActionResult<object>> GetUserById(int userId)
        {
            var user = await _db.Users
                .Include(u => u.Role)
                .Where(u => u.UserID == userId)
                .Select(u => new
                {
                    u.UserID,
                    u.Username,
                    u.Name,
                    u.Email,
                    u.PhoneNumber,
                    RoleName = u.Role != null ? u.Role.RoleName : null
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound("User not found.");

            return Ok(user);
        }

        /// Tự cập nhật thông tin của mình 
        [HttpGet("profile")]
        [Authorize]
        [Produces("application/json")]
        public async Task<ActionResult<object>> GetMyProfile()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized();

            var u = await _db.Users
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Username == username);

            if (u == null) return NotFound("User not found.");

            return Ok(new
            {
                userId = u.UserID,
                username = u.Username,
                name = u.Name,
                email = u.Email,
                phoneNumber = u.PhoneNumber,
                roleName = u.Role != null ? u.Role.RoleName : null
            });
        }

        public class UpdateSelfRequest
        {
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? PhoneNumber { get; set; }
        }

        [HttpPut("profile")]
        [Authorize]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult> UpdateMyProfile([FromBody] UpdateSelfRequest req)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized();

            var u = await _db.Users.FirstOrDefaultAsync(x => x.Username == username);
            if (u == null) return NotFound("User not found.");

            if (!string.IsNullOrWhiteSpace(req.Name)) u.Name = req.Name.Trim();
            if (req.Email != null) u.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
            if (req.PhoneNumber != null) u.PhoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim();

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // =================== Forgot/Reset (KHÔNG đụng DB) ===================

        private static readonly ConcurrentDictionary<string, (string Code, DateTimeOffset ExpireAt)>
            _resetCodes = new(StringComparer.OrdinalIgnoreCase);

        private static string MakeNumericCode(int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(RandomNumberGenerator.GetInt32(0, 10)); // 0..9
            return sb.ToString();
        }

        private static void PutResetCode(string username, string code, TimeSpan ttl)
        {
            _resetCodes[username] = (code, DateTimeOffset.UtcNow.Add(ttl));
        }

        private static bool CheckResetCode(string username, string code)
        {
            if (!_resetCodes.TryGetValue(username, out var entry)) return false;
            if (DateTimeOffset.UtcNow > entry.ExpireAt) return false;
            return string.Equals(entry.Code, code, StringComparison.Ordinal);
        }

        private static void ClearResetCode(string username)
        {
            _resetCodes.TryRemove(username, out _);
        }

        private async Task TrySendResetCodeEmailAsync(string? toEmail, string code, int ttlMinutes)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            // Đọc cấu hình SMTP qua ENV (đơn giản – không đụng DI/config cũ)
            var host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
            var portStr = Environment.GetEnvironmentVariable("SMTP_PORT");
            var user = Environment.GetEnvironmentVariable("SMTP_USER");
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS");
            var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? user;

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from))
                return; // chưa cấu hình SMTP → bỏ qua gửi thật

            using var smtp = new SmtpClient(host, int.TryParse(portStr, out var p) ? p : 587)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, pass)
            };

            var subject = "Mã đặt lại mật khẩu";
            var body = $"Mã xác minh của bạn là: {code}\nMã sẽ hết hạn sau {ttlMinutes} phút.";

            using var msg = new MailMessage(from!, toEmail, subject, body) { IsBodyHtml = false };
            try { await smtp.SendMailAsync(msg); } catch { /* ignore */ }
        }

        public sealed class ForgotRequest
        {
            public string? Username { get; set; }
            public string? Email { get; set; }
        }

        public sealed class ResetPasswordRequest
        {
            public string? Username { get; set; }
            public string? Email { get; set; }
            public string? Code { get; set; }
            public string? NewPassword { get; set; }
        }

        /// <summary>Gửi mã 6 số đặt lại mật khẩu (lưu In-Memory, không tạo bảng).</summary>
        [HttpPost("forgot")]
        [AllowAnonymous]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<object>> Forgot([FromBody] ForgotRequest req)
        {
            if (req == null || (string.IsNullOrWhiteSpace(req.Username) && string.IsNullOrWhiteSpace(req.Email)))
                return BadRequest("Username hoặc Email là bắt buộc.");

            var username = req.Username?.Trim();
            var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email!.Trim();

            var user = await _db.Users
                .FirstOrDefaultAsync(u =>
                    (!string.IsNullOrEmpty(username) && u.Username == username) ||
                    (!string.IsNullOrEmpty(email) && u.Email == email));

            // Phản hồi chung để tránh dò tài khoản
            if (user == null)
            {
                return Ok(new
                {
                    message = "Nếu tài khoản tồn tại, mã đã được gửi.",
                    expiresInMinutes = 10,
                    devCode = (string?)null
                });
            }

            var codeLength = 6;
            var ttlMinutes = 10;
            var code = MakeNumericCode(codeLength);

            PutResetCode(user.Username, code, TimeSpan.FromMinutes(ttlMinutes));
            await TrySendResetCodeEmailAsync(user.Email, code, ttlMinutes);

            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var isDev = string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);

            return Ok(new
            {
                message = "Nếu tài khoản tồn tại, mã đã được gửi.",
                expiresInMinutes = ttlMinutes,
                devCode = isDev ? code : null // chỉ trả code khi Development
            });
        }

        /// <summary>Xác minh mã & đặt mật khẩu mới (không tạo bảng).</summary>
        [HttpPost("reset")]               // tương thích UI cũ
        [HttpPost("reset-password")]      // route mới
        [AllowAnonymous]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
        {
            if (req == null) return BadRequest("Invalid payload.");
            var code = req.Code?.Trim();
            var newPwd = req.NewPassword ?? "";

            if (string.IsNullOrWhiteSpace(code) || code.Length < 6)
                return BadRequest("Mã xác minh không hợp lệ.");
            if (newPwd.Length < 6)
                return BadRequest("Mật khẩu mới tối thiểu 6 ký tự.");

            var username = req.Username?.Trim();
            var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email!.Trim();

            var user = await _db.Users
                .FirstOrDefaultAsync(u =>
                    (!string.IsNullOrEmpty(username) && u.Username == username) ||
                    (!string.IsNullOrEmpty(email) && u.Email == email));

            if (user == null) return NotFound("User không tồn tại.");

            if (!CheckResetCode(user.Username, code))
                return BadRequest("Mã xác minh không đúng hoặc đã hết hạn.");

            var bcrypt = BCrypt.Net.BCrypt.HashPassword(newPwd, workFactor: 11);
            user.PasswordHash = Encoding.UTF8.GetBytes(bcrypt);
            await _db.SaveChangesAsync();

            ClearResetCode(user.Username);
            return NoContent();
        }
=======
>>>>>>> 321ff6ba44c688d83fcc95b43fca3d6df0f45b93
    }
}
