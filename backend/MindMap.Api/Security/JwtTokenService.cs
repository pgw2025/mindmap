using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MindMap.Api.Common.Options;

namespace MindMap.Api.Security;

public interface IJwtTokenService
{
    /// <summary>生成 Access Token（短期）。</summary>
    (string token, DateTime expiresAt) GenerateAccessToken(Guid userId, string username, bool isAdmin);

    /// <summary>生成 Refresh Token（随机字符串，长期）。返回原始 token 与哈希。</summary>
    (string plainToken, string hashedToken, DateTime expiresAt) GenerateRefreshToken();

    /// <summary>哈希 Refresh Token，入库前调用。</summary>
    string HashRefreshToken(string plainToken);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(IOptions<JwtOptions> opt)
    {
        _opt = opt.Value;
    }

    public (string token, DateTime expiresAt) GenerateAccessToken(Guid userId, string username, bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, isAdmin ? "admin" : "user")
        };

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_opt.AccessTokenMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string plainToken, string hashedToken, DateTime expiresAt) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        var plain = Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var hashed = HashRefreshToken(plain);
        var expiresAt = DateTime.UtcNow.AddDays(_opt.RefreshTokenDays);
        return (plain, hashed, expiresAt);
    }

    public string HashRefreshToken(string plainToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        return Convert.ToBase64String(bytes);
    }
}
