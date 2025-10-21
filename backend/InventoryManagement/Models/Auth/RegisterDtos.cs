using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Models.Auth
{
    public class RegisterRequest
    {
        [Required, MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, MinLength(6), MaxLength(200)]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [EmailAddress, MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? PhoneNumber { get; set; }

        /// <summary>
        /// Optional. Nếu để trống sẽ mặc định "Supplier".
        /// Hợp lệ: "Administrator","WarehouseManager","StoreManager","Supplier"
        /// </summary>
        [MaxLength(50)]
        public string? RoleName { get; set; }
    }

    public class RegisterResponse
    {
        public string Username { get; set; } = string.Empty;
        public string? Role { get; set; }
        public int? SupplierId { get; set; }
        public int? StoreId { get; set; }

        // Auto-login sau đăng ký
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public long ExpiresInSeconds { get; set; }
    }
}
