namespace MindMap.Api.Domain.Entities.Enums;

/// <summary>
/// 用户账号状态。
/// </summary>
public enum UserStatus
{
    /// <summary>正常可用</summary>
    Active = 0,

    /// <summary>已禁用（管理员可禁用）</summary>
    Disabled = 1
}
