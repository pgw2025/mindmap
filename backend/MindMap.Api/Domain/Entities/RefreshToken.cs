namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 刷新令牌（哈希存储）。每次刷新将旧令牌置 Revoked 并签发新令牌（旋转）。
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 哈希值，唯一索引。</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>当令牌旋转时，记录新令牌的哈希，便于审计链路。</summary>
    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;
}
