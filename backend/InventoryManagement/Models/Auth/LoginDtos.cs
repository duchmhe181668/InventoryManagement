namespace InventoryManagement.Models.Auth
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public long ExpiresInSeconds { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Role { get; set; }

        //Trung
        public int? SupplierId { get; set; }
        public int? StoreId { get; set; }
    }
}
