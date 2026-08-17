namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 思维导图分享链接。
/// 通过唯一 ShareToken 提供临时访问；可设置过期时间和访问密码。
/// </summary>
public class MindMapShare
{
    public Guid Id { get; set; }

    public Guid MindMapId { get; set; }
    public MindMapEntity MindMap { get; set; } = null!;

    /// <summary>分享令牌（URL 中使用），唯一索引。</summary>
    public string ShareToken { get; set; } = string.Empty;

    /// <summary>访问密码（可选）。SHA256 或明文较短密码均可。</summary>
    public string? Password { get; set; }

    /// <summary>过期时间（UTC），null 表示永不过期。</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>最大访问次数，null 表示不限次。</summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>实际访问次数。</summary>
    public int AccessCount { get; set; }

    /// <summary>是否允许另存为复制，false=仅查看。</summary>
    public bool AllowCopy { get; set; }

    /// <summary>是否已禁用。</summary>
    public bool IsDisabled { get; set; }

    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}
