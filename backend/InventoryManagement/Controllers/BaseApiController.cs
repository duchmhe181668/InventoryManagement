using System.Security.Claims;
using System.Threading.Tasks;
using InventoryManagement.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Controllers
{
    [ApiController]
    [Authorize] 
    public abstract class BaseApiController : ControllerBase
    {
        protected async Task<int?> GetCurrentUserIdAsync(AppDbContext db)
        {
            var username = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(username))
            {
                return null; 
            }

            // Dùng Select để tối ưu, chỉ query UserID
            var user = await db.Users.AsNoTracking()
                           .Where(u => u.Username == username)
                           .Select(u => (int?)u.UserID) // Ép kiểu sang nullable int
                           .FirstOrDefaultAsync();

            return user; // Sẽ là null nếu user không tồn tại
        }

        protected IActionResult UserNotFound()
        {
            return Unauthorized("Token không hợp lệ hoặc người dùng không tồn tại.");
        }
        protected DateTime GetVietnamTime()
        {
            var utcNow = DateTime.UtcNow;
            try
            {
                // Windows
                var vietnamZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, vietnamZone);
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    // Linux/macOS
                    var vietnamZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcNow, vietnamZone);
                }
                catch (Exception)
                {
                    // +7 múi giờ gốc cũng được
                    return utcNow.AddHours(7);
                }
            }
            catch (Exception)
            {
                // +7 múi giờ gốc cũng được
                return utcNow.AddHours(7);
            }
        }
    }
}