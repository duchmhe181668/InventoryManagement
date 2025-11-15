using BCrypt.Net;
using InventoryManagement.Data;
using InventoryManagement.Models;
using InventoryManagement.Models.Auth;
using InventoryManagement.Models.Views;
using InventoryManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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

        /// Login
        [HttpPost("login")]
        [AllowAnonymous]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
        {
            var usernameInput = req?.Username?.Trim();
            var passwordInput = req?.Password;

            if (string.IsNullOrWhiteSpace(usernameInput) || string.IsNullOrWhiteSpace(passwordInput))
                return BadRequest("Tên tài khoản/mật khẩu là bắt buộc");

            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == usernameInput.ToLower());

            if (user is null || user.PasswordHash is null || user.PasswordHash.Length == 0)
                return Unauthorized("Sai tên tài khoản hoặc mật khẩu.");

            string hash = Encoding.UTF8.GetString(user.PasswordHash).Trim();
            if (!(hash.StartsWith("$2a$") || hash.StartsWith("$2b$") || hash.StartsWith("$2y$")))
            {
                hash = Encoding.Unicode.GetString(user.PasswordHash).Trim();
            }

            bool ok;
            try { ok = BCrypt.Net.BCrypt.Verify(passwordInput, hash); }
            catch { return BadRequest("PasswordHash is not a valid BCrypt string. Please update the user with a BCrypt hash."); }
            if (!ok) return Unauthorized("Sai tên tài khoản hoặc mật khẩu.");

            var roleName = user.Role?.RoleName;
            var roles = string.IsNullOrWhiteSpace(roleName)
                ? Enumerable.Empty<string>()
                : new[] { roleName! };

            /// Supplier nào / Store nào
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

            var extraClaims = new Dictionary<string, string>
            {
                { "user_id", user.UserID.ToString() }
            };
            if (supplierId.HasValue)
                extraClaims["supplier_id"] = supplierId.Value.ToString();
            if (storeId.HasValue)
                extraClaims["store_id"] = storeId.Value.ToString();




            var token = _jwt.CreateToken(user.Username, user.Username, roles, extraClaims);
            var exp = _jwt.GetExpiry();


            return Ok(new LoginResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresInSeconds = (long)(exp - DateTimeOffset.UtcNow).TotalSeconds,
                Username = user.Username,
                Role = roleName,
                UserId = user.UserID,
                SupplierId = supplierId,
                StoreId = storeId
            });
        }

        /// Đăng ký tài khoản 
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

            if (!string.IsNullOrWhiteSpace(email) &&
               !Regex.IsMatch(email, @"^[\w.+-]+@gmail\.com$", RegexOptions.IgnoreCase))
                return BadRequest("Email phải có dạng @gmail.com");

            if (!string.IsNullOrWhiteSpace(phone) &&
                !Regex.IsMatch(phone, @"^\d{10}$"))
                return BadRequest("Số điện thoại phải gồm đúng 10 số");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name))
                return BadRequest("Username, Password, Name are required.");
            if (password.Length < 6) return BadRequest("Mật khẩu tối thiểu 6 ký tự.");

            var existed = await _db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower());
            if (existed) return Conflict("Tài khoản này đã tồn tại");

            var roleName = string.IsNullOrWhiteSpace(req.RoleName) ? "Supplier" : req.RoleName!.Trim();
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName);
            if (role == null)
                return BadRequest("Invalid role name.");
            ///Hash mật khẩu và tạo User
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

            if (role.RoleName == "Supplier")
            {
                var newSupplier = new Supplier
                {
                    Name = name,
                    Email = email,
                    PhoneNumber = phone
                };

                _db.Suppliers.Add(newSupplier);
                await _db.SaveChangesAsync();

                supplierId = newSupplier.SupplierID;
            }

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

        /// Test token trả lại danh tính & claims là gì trên Swagger
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


        /// Cập nhật thông tin người dùng
        [HttpPut("update/{userId}")]
        [AllowAnonymous]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult> UpdateUser(int userId, [FromBody] UpdateUserRequest req)
        {
            var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserID == userId);

            if (user == null)
                return NotFound("User not found.");

            switch (user.Role?.RoleName) 
            { 
                case "Supplier":
                user.Name = req.Name?.Trim() ?? user.Name;
                user.PhoneNumber = req.PhoneNumber?.Trim() ?? user.PhoneNumber;
                break;

            default:
                user.Username = req.Username?.Trim() ?? user.Username;
                user.Name = req.Name?.Trim() ?? user.Name;
                user.Email = string.IsNullOrWhiteSpace(req.Email) ? user.Email : req.Email.Trim();
                user.PhoneNumber = req.PhoneNumber?.Trim() ?? user.PhoneNumber;

                if (!string.IsNullOrWhiteSpace(req.RoleName))
                {
                    var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == req.RoleName);
                    if (role != null)
                        user.RoleID = role.RoleID;
                }
                break;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// Xóa người dùng
        [HttpDelete("delete/{userId}")]
        [AllowAnonymous]
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
        [AllowAnonymous]
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

        /// Profile cá nhân
        [HttpGet("user/{userId}")]
        [AllowAnonymous]
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

            ///Lấy số Store của Supplier
            var store = await _db.Stores
                .Include(s => s.Location) 
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserID == u.UserID); 

            if (store == null && u.Role?.RoleName == "StoreManager")
            {
                var firstStore = await _db.Locations.AsNoTracking()
                   .Where(l => l.IsActive && l.LocationType != null && l.LocationType.ToLower() == "store")
                   .OrderBy(l => l.LocationID)
                   .Select(l => new { l.LocationID, l.Name })
                   .FirstOrDefaultAsync();

                return Ok(new
                {
                    userId = u.UserID,
                    username = u.Username,
                    name = u.Name,
                    email = u.Email,
                    phoneNumber = u.PhoneNumber,
                    roleName = u.Role != null ? u.Role.RoleName : null,
                    storeId = (int?)null,
                    storeDefaultLocationId = firstStore?.LocationID,
                    storeDefaultLocationName = firstStore?.Name
                });
            }

            return Ok(new
            {
                userId = u.UserID,
                username = u.Username,
                name = u.Name,
                email = u.Email,
                phoneNumber = u.PhoneNumber,
                roleName = u.Role != null ? u.Role.RoleName : null,

                
                storeId = store?.StoreID,
                storeDefaultLocationId = store?.LocationID,
                storeDefaultLocationName = store?.Location?.Name
            });
        }

        public class UpdateSelfRequest
        {
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? PhoneNumber { get; set; }
        }

        /// Tự cập nhật profile cá nhân
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

            if (!string.IsNullOrWhiteSpace(req.Email) &&
                !Regex.IsMatch(req.Email.Trim(), @"^[\w.+-]+@gmail\.com$", RegexOptions.IgnoreCase))
                return BadRequest("Email phải có dạng @gmail.com");

            if (!string.IsNullOrWhiteSpace(req.PhoneNumber) &&
                !Regex.IsMatch(req.PhoneNumber.Trim(), @"^\d{10}$"))
                return BadRequest("Số điện thoại phải gồm đúng 10 số");

            if (!string.IsNullOrWhiteSpace(req.Name)) u.Name = req.Name.Trim();
            if (req.Email != null) u.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
            if (req.PhoneNumber != null) u.PhoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim();

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// Forgot / Reset Password

        private static readonly ConcurrentDictionary<string, (string Code, DateTimeOffset ExpireAt)>
            _resetCodes = new(StringComparer.OrdinalIgnoreCase); /// Lấy user trong  RAM ko dùng DB

        private static string MakeNumericCode(int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(RandomNumberGenerator.GetInt32(0, 10)); 
            return sb.ToString();
        }
        /// Check/sửa/xoá mã
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
        /// Gửi Email
        private async Task TrySendResetCodeEmailAsync(string? toEmail, string code, int ttlMinutes)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            var host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
            var portStr = Environment.GetEnvironmentVariable("SMTP_PORT");
            var user = Environment.GetEnvironmentVariable("SMTP_USER");
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS");
            var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? user;

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from))
                return; /// chưa cấu hình cho email thì bỏ qua 

            using var smtp = new SmtpClient(host, int.TryParse(portStr, out var p) ? p : 587)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, pass)
            };

            var subject = "Mã đặt lại mật khẩu";
            var body = $"Mã xác minh của bạn là: {code}\nMã sẽ hết hạn sau {ttlMinutes} phút.";

            using var msg = new MailMessage(from!, toEmail, subject, body) { IsBodyHtml = false };
            try { await smtp.SendMailAsync(msg); } catch {}
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

        ///Gửi mã 6 số đặt lại mật khẩu (lưu In-Memory, không tạo bảng).
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
                devCode = isDev ? code : null /// trả Dev code để test trên Swagger 
            });
        }

        /// Xác minh mã / đặt mật khẩu mới 
        [HttpPost("reset")]               
        [HttpPost("reset-password")]      
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

        /// Đổi mật khẩu
        public sealed class ChangePasswordRequest
        {
            public string? CurrentPassword { get; set; }
            public string? NewPassword { get; set; }
        }

        [HttpPost("change-password")]
        [Authorize]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            if (req == null) return BadRequest("Invalid payload.");
            var current = req.CurrentPassword?.Trim() ?? "";
            var newer = req.NewPassword?.Trim() ?? "";

            if (newer.Length < 6) return BadRequest("Mật khẩu mới tối thiểu 6 ký tự.");

            // Lấy username từ JWT 
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized("Không xác định người dùng.");

            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return NotFound("User không tồn tại.");

            // Đọc hash đang lưu 
            string savedHash = Encoding.UTF8.GetString(user.PasswordHash).Trim();
            if (!(savedHash.StartsWith("$2a$") || savedHash.StartsWith("$2b$") || savedHash.StartsWith("$2y$")))
            {
                savedHash = Encoding.Unicode.GetString(user.PasswordHash).Trim();
            }

            bool ok;
            try { ok = BCrypt.Net.BCrypt.Verify(current, savedHash); }
            catch { return BadRequest("Hash mật khẩu hiện tại không hợp lệ."); }

            if (!ok) return Unauthorized("Mật khẩu hiện tại không đúng.");

            // Cập nhật mật khẩu mới
            var newHash = BCrypt.Net.BCrypt.HashPassword(newer, workFactor: 11);
            user.PasswordHash = Encoding.UTF8.GetBytes(newHash);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Đổi mật khẩu thành công." });
        }

    }
}
