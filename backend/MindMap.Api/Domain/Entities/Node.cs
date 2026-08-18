using MindMap.Api.Domain.Entities.Enums;

namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 思维导图节点。支持树形结构（自引用），每个导图有一个根节点。
/// </summary>
public class Node
{
    public Guid Id { get; set; }

    public Guid MindMapId { get; set; }
    public MindMap MindMap { get; set; } = null!;

    /// <summary>父节点 Id；根节点为 null。</summary>
    public Guid? ParentId { get; set; }
    public Node? Parent { get; set; }

    /// <summary>节点标题（纯文本）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>节点正文（富文本 HTML / Markdown，可选）。</summary>
    public string? Content { get; set; }

    /// <summary>备注（支持长文本，可选）。</summary>
    public string? Note { get; set; }

    /// <summary>同级排序，数值越小越靠前。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否折叠子节点。</summary>
    public bool IsCollapsed { get; set; }

    /// <summary>自由画布坐标 X。</summary>
    public double? X { get; set; }

    /// <summary>自由画布坐标 Y。</summary>
    public double? Y { get; set; }

    /// <summary>节点宽度（自适应时为 null）。</summary>
    public double? Width { get; set; }

    /// <summary>节点高度（自适应时为 null）。</summary>
    public double? Height { get; set; }

    /// <summary>节点文字颜色。</summary>
    public string? Color { get; set; }

    /// <summary>字号（像素）。</summary>
    public int? FontSize { get; set; }

    /// <summary>字体族。</summary>
    public string? FontFamily { get; set; }

    /// <summary>节点形状。</summary>
    public NodeShape? Shape { get; set; }

    /// <summary>节点图标 / Emoji。</summary>
    public string? Icon { get; set; }

    /// <summary>边框颜色。</summary>
    public string? BorderColor { get; set; }

    /// <summary>根节点直接子节点的生长方向；null 表示未指定（前端默认朝右）。</summary>
    public Direction? Direction { get; set; }

    /// <summary>背景填充颜色。</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>连线颜色。</summary>
    public string? EdgeColor { get; set; }

    /// <summary>连线样式。</summary>
    public EdgeStyle? EdgeStyle { get; set; }

    /// <summary>
    /// 扩展数据（JSON），用于存储：附件列表、超链接、子节点自定义样式等。
    /// 示例：{ "attachments": [...], "link": "...", "emoji": "👍" }
    /// </summary>
    public string? ExtraData { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Node> Children { get; set; } = new List<Node>();
}
