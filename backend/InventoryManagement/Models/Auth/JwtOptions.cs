namespace InventoryManagement.Models.Auth
{
    public class JwtOptions
    {
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;          // bí mật ký JWT
        public int ExpiresMinutes { get; set; } = 120;            // thời gian sống token
    }
}
