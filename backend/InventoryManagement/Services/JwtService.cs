using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InventoryManagement.Models.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InventoryManagement.Services
{
    public class JwtService : IJwtService
    {
        private readonly JwtOptions _opt;
        private DateTimeOffset _exp;

        public JwtService(IOptions<JwtOptions> options) => _opt = options.Value;

        public string CreateToken(string subject, string name, IEnumerable<string> roles, IDictionary<string, string>? extraClaims = null)
        {
            var claims = new List<Claim> {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            };
            if (roles != null) foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

            if (extraClaims != null)
            {
                foreach (var kv in extraClaims)
                    claims.Add(new Claim(kv.Key, kv.Value));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var now = DateTimeOffset.UtcNow;
            _exp = now.AddMinutes(_opt.ExpiresMinutes);

            var token = new JwtSecurityToken(
                issuer: _opt.Issuer,
                audience: _opt.Audience,
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: _exp.UtcDateTime,
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public DateTimeOffset GetExpiry() => _exp;
    }
}
