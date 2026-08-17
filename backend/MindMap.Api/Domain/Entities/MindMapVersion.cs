namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 思维导图版本快照。每次保存版本时序列化节点树为 JSON 存储。
/// </summary>
public class MindMapVersion
{
    public Guid Id { get; set; }

    public Guid MindMapId { get; set; }
    public MindMapEntity MindMap { get; set; } = null!;

    /// <summary>版本号（自动递增，1 开始）。</summary>
    public int VersionNumber { get; set; }

    /// <summary>版本备注（可选，如"项目启动阶段"）。</summary>
    public string? Remark { get; set; }

    /// <summary>节点树快照（JSON）。</summary>
    public string NodeSnapshotJson { get; set; } = string.Empty;

    /// <summary>版本创建时的节点总数。</summary>
    public int NodeCount { get; set; }

    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
