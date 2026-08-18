namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 思维导图举报记录。用户可对公开导图发起举报，管理员在后台审核。
/// </summary>
public class MindMapReport
{
    public Guid Id { get; set; }

    public Guid MindMapId { get; set; }
    public MindMapEntity MindMap { get; set; } = null!;

    /// <summary>举报人 Id（已登录用户），null 表示匿名举报。</summary>
    public Guid? ReporterId { get; set; }
    public User? Reporter { get; set; }

    /// <summary>举报理由（简述）。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>举报状态：0=待处理 1=已处理-驳回 2=已处理-下架</summary>
    public int Status { get; set; }

    /// <summary>处理备注（管理员填写）。</summary>
    public string? ResolutionNote { get; set; }

    public Guid? ResolvedById { get; set; }
    public User? ResolvedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
