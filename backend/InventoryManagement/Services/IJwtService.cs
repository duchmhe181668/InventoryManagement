namespace InventoryManagement.Services
{
    public interface IJwtService
    {
        string CreateToken(string subject, string name, IEnumerable<string> roles);
        DateTimeOffset GetExpiry();
    }
}
