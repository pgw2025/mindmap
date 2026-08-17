using System.Net;
using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Auth;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Helpers;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Domain.Entities.Enums;
using MindMap.Api.Infrastructure.Data;
using MindMap.Api.Security;

namespace MindMap.Api.Application.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;

    public AuthService(AppDbContext db, IJwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip, CancellationToken ct = default)
    {
        var exists = await _db.Users.AnyAsync(u =>
            u.Username == request.Username || u.Email == request.Email, ct);
        if (exists)
        {
            throw ApiException.Conflict("用户名或邮箱已被注册");
        }

        var (hash, salt) = PasswordHasher.Create(request.Password);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsAdmin = false,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ip);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username == request.Account || u.Email == request.Account, ct);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
 // 统一错误：避免泄露用户是否存在
            throw new ApiException("账号或密码错误", StatusCodes.Status401Unauthorized);
        }

        if (user.Status == UserStatus.Disabled)
        {
            throw new ApiException("账号已禁用，请联系管理员", StatusCodes.Status403Forbidden);
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ip);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, string? ip, CancellationToken ct = default)
    {
        var hashed = _jwt.HashRefreshToken(refreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hashed, ct);

        if (token is null || !token.IsActive)
        {
            throw new ApiException("刷新令牌无效或已过期", StatusCodes.Status401Unauthorized);
        }

        if (token.User.Status == UserStatus.Disabled)
        {
            throw ApiException.Forbidden("账号已禁用");
        }

        // 旋转：吊销旧令牌 + 签发新令牌
        var (newPlain, newHash, newExpires) = _jwt.GenerateRefreshToken();
        token.RevokedAt = DateTime.UtcNow;
        token.ReplacedByTokenHash = newHash;

        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = token.UserId,
            TokenHash = newHash,
            ExpiresAt = newExpires,
            CreatedByIp = ip,
            CreatedAt = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(newToken);
        await _db.SaveChangesAsync(ct);

        var (access, accessExpires) = _jwt.GenerateAccessToken(token.User.Id, token.User.Username, token.User.IsAdmin);
        return new AuthResponse
        {
            AccessToken = access,
            AccessTokenExpiresAt = accessExpires,
            RefreshToken = newPlain,
            RefreshTokenExpiresAt = newExpires,
            User = ToDto(token.User)
        };
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hashed = _jwt.HashRefreshToken(refreshToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hashed, ct);
        if (token is not null && token.IsActive)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object?[] { userId }, ct);
        return user is null ? null : ToDto(user);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? ip)
    {
        var (accessPlain, accessExpires) = _jwt.GenerateAccessToken(user.Id, user.Username, user.IsAdmin);
        var (refreshPlain, refreshHash, refreshExpires) = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpires,
            CreatedByIp = ip,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessPlain,
            AccessTokenExpiresAt = accessExpires,
            RefreshToken = refreshPlain,
            RefreshTokenExpiresAt = refreshExpires,
            User = ToDto(user)
        };
    }

    private static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        Avatar = user.Avatar,
        IsAdmin = user.IsAdmin,
        CreatedAt = user.CreatedAt
    };
}
