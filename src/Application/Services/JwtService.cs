using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Application.Services;

public class JwtService
{
    private readonly string _secret;
    private readonly int _expiryMinutes;

    public JwtService(string secret, int expiryMinutes = 480) // 8 hours default
    {
        _secret = secret;
        _expiryMinutes = expiryMinutes;
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("must_change_password", user.MustChangePassword.ToString().ToLower()),
            // Versi ini dibandingkan dengan baris Users pada setiap permintaan. Logout dan
            // ganti sandi menaikkannya, sehingga token yang sudah tercetak langsung mati.
            new Claim("token_version", user.TokenVersion.ToString())
        };
        var token = new JwtSecurityToken(
            issuer: "seruni22bakery",
            audience: "seruni22bakery",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
