using MindMap.Api.Domain.Entities.Enums;

namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 用户账号。
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsAdmin { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// 回收站个人保留天数（覆盖全局默认）。null = 使用全局默认（RecycleBin:DefaultRetentionDays）。
    /// 缩短后已超期的内容会在下一次清理时被物理删除。
    /// </summary>
    public int? RecycleBinRetentionDays { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<MindMapEntity> MindMaps { get; set; } = new List<MindMapEntity>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
