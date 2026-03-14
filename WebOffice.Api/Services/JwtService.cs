using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using bsckend.Models.User;

namespace bsckend.Services;

public class JwtService
{
    private readonly IConfiguration _cfg;
    private readonly UserManager<UserModel> _userManager;

    public JwtService(IConfiguration cfg, UserManager<UserModel> userManager)
    {
        _cfg = cfg;
        _userManager = userManager;
    }

    public async Task<string> GenerateTokenAsync(UserModel user)
    {
        var keyStr = _cfg["Jwt:Key"] ?? string.Empty;
        if (string.IsNullOrEmpty(keyStr)) throw new InvalidOperationException("Jwt:Key is not configured");

        var issuer = _cfg["Jwt:Issuer"] ?? "";
        var audience = _cfg["Jwt:Audience"] ?? "";
        var expiry = int.TryParse(_cfg["Jwt:ExpiryMinutes"], out var m) ? m : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim("UserName", user.UserName ?? string.Empty)
        };

        var userRoles = await _userManager.GetRolesAsync(user);
        foreach (var role in userRoles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiry),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}