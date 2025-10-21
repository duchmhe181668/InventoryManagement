namespace InventoryManagement.Models.Auth
{
    public class UpdateUserRequest
    {
        public string? Username { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? RoleName { get; set; }  // Role cần chỉnh sửa
    }
}
