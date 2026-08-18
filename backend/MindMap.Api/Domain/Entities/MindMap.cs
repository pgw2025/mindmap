using MindMap.Api.Domain.Entities.Enums;

namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 思维导图。一个导图只有一个中心主题（根节点）。
/// </summary>
public class MindMap
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public string? CoverImage { get; set; }

    /// <summary>根节点 Id。</summary>
    public Guid? RootNodeId { get; set; }

    /// <summary>根节点导航属性。</summary>
    public Node? RootNode { get; set; }

    public MindMapLayout DefaultLayout { get; set; } = MindMapLayout.Left;
    public string? Theme { get; set; }
    public int NodeCount { get; set; }

    /// <summary>是否已被管理员下架。下架后公开列表和分享不可访问。</summary>
    public bool IsTakenDown { get; set; }
    public DateTime? TakenDownAt { get; set; }
    public string? TakenDownReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastEditedAt { get; set; }

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<Node> Nodes { get; set; } = new List<Node>();
    public ICollection<MindMapReport> Reports { get; set; } = new List<MindMapReport>();
}
